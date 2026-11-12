namespace GameLauncherGUI;

internal enum LaunchIssueKind { NotObserved, ClosedQuickly, StartError }
internal sealed record LaunchIssue(string GameId,string GameName,DateTimeOffset At,LaunchIssueKind Kind,string Detail);
internal sealed class LaunchAttemptTracker
{
    private sealed class Attempt(GameInfo game,DateTimeOffset start){internal GameInfo Game=game;internal DateTimeOffset Start=start;internal DateTimeOffset? Seen,Missing;}
    private readonly Dictionary<string,Attempt> attempts=new();
    private readonly int thread=Environment.CurrentManagedThreadId;
    private void Verify(){if(thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Launch tracking belongs to the UI thread.");}
    internal void Arm(GameInfo game,DateTimeOffset now,bool processObserved=false){Verify();attempts[game.LibraryId]=new(game,now){Seen=processObserved?now:null};if(attempts.Count>100)attempts.Remove(attempts.OrderBy(p=>p.Value.Start).First().Key);}
    internal IReadOnlyList<LaunchIssue> Observe(DateTimeOffset now,Func<GameInfo,bool> running,IEnumerable<GameInfo> library)
    {
        Verify();var known=library.Select(g=>g.LibraryId).ToHashSet();var issues=new List<LaunchIssue>();
        foreach(var pair in attempts.ToArray())
        {
            var attempt=pair.Value;
            if(!known.Contains(pair.Key)){attempts.Remove(pair.Key);continue;}
            if(running(attempt.Game))
            {
                attempt.Seen??=now;attempt.Missing=null;
                if(now-attempt.Seen>=TimeSpan.FromSeconds(45))attempts.Remove(pair.Key);
            }
            else if(attempt.Seen.HasValue)
            {
                attempt.Missing??=now;
                if(now-attempt.Missing<TimeSpan.FromSeconds(15))continue;
                if(attempt.Missing-attempt.Seen<TimeSpan.FromSeconds(45))issues.Add(new(pair.Key,attempt.Game.Name,now,LaunchIssueKind.ClosedQuickly,"Отслеживаемый процесс появился и быстро завершился. Это может быть обычный выход, передача запуска другому процессу или сбой. Код завершения и причина этим наблюдением не определяются."));
                attempts.Remove(pair.Key);
            }
            else if(now-attempt.Start>=TimeSpan.FromMinutes(3))
            {
                issues.Add(new(pair.Key,attempt.Game.Name,now,LaunchIssueKind.NotObserved,"За три минуты не удалось увидеть выбранный процесс игры. Возможно, клиент ожидает входа, обновляет игру или использует другой EXE. Это не подтверждение сбоя."));attempts.Remove(pair.Key);
            }
        }
        return issues;
    }
}
internal static class LaunchIssues
{
    internal static LaunchIssue[] Load(string folder)
    {
        string file=Path.Combine(folder,"launch-issues.json");
        try
        {
            if(File.Exists(file)&&new FileInfo(file).Length>1024*1024)return Array.Empty<LaunchIssue>();
            return LocalJson.Read(file,()=>Array.Empty<LaunchIssue>()).OfType<LaunchIssue>().Where(i=>Enum.IsDefined(i.Kind)&&i.GameName!=null&&i.Detail!=null&&i.GameName.Length<=1000&&i.Detail.Length<=8000).TakeLast(30).ToArray();
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return Array.Empty<LaunchIssue>();}
    }
    internal static void Add(string folder,LaunchIssue issue)=>LocalJson.Write(Path.Combine(folder,"launch-issues.json"),Load(folder).Append(issue).TakeLast(30).ToArray());
}
