namespace GameLauncherGUI;

internal sealed record GameDiskUsage(long? Bytes,bool ExeFolder=false)
{
    internal string Label=>Bytes is not long bytes?"Размер недоступен":(ExeFolder?"Папка EXE: ":"")+Format(bytes);
    internal static string Format(long bytes)=>bytes>=1073741824?$"{bytes/1073741824.0:N1} ГБ":bytes>=1048576?$"{bytes/1048576.0:N1} МБ":$"{bytes/1024.0:N1} КБ";
    internal static (string? Root,bool Fallback) ResolveRoot(string? installDirectory,string? executable)
    {
        if(!File.Exists(executable)){
            if(!Directory.Exists(installDirectory))return(null,false);
            var container=Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory!)));
            if(new[]{"Games","Игры","Steam","SteamLibrary","steamapps","common","Epic Games","Riot Games","Ubisoft Game Launcher"}.Contains(container,StringComparer.OrdinalIgnoreCase))return(null,false);
            return(installDirectory,false);
        }
        var executableFolder=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(executable!))!);
        // A real Steam common/<game> boundary is stronger evidence than a stored installation hint.
        for(var current=executableFolder;current.Parent!=null;current=current.Parent)
            if(current.Parent.Name.Equals("common",StringComparison.OrdinalIgnoreCase)&&current.Parent.Parent?.Name.Equals("steamapps",StringComparison.OrdinalIgnoreCase)==true)return(current.FullName,false);
        var root=executableFolder;
        var technical=new HashSet<string>(new[]{"bin","bin64","bin32","binaries","win64","win32","x64","x86","release","debug"},StringComparer.OrdinalIgnoreCase);
        while(root.Parent!=null&&technical.Contains(root.Name))root=root.Parent;
        // Unreal games commonly keep the shipping EXE in <root>/<project>/Binaries/Win64.
        if(root.Parent!=null&&Directory.Exists(Path.Combine(root.Parent.FullName,"Engine"))&&Directory.Exists(Path.Combine(root.FullName,"Content")))root=root.Parent;
        if(Directory.Exists(installDirectory)){
            string hint=Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory!));
            var containers=new HashSet<string>(new[]{"Games","Игры","Steam","SteamLibrary","steamapps","common","Epic Games","Riot Games","Ubisoft Game Launcher","Program Files","Program Files (x86)"},StringComparer.OrdinalIgnoreCase);
            if(!containers.Contains(Path.GetFileName(hint))&&root.FullName.StartsWith(hint+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){
                // Do not trust an arbitrary ancestor: it must directly contain the resolved game project.
                if(root.Parent?.FullName.Equals(hint,StringComparison.OrdinalIgnoreCase)==true&&Directory.Exists(Path.Combine(hint,"Engine")))return(hint,false);
            }
        }
        return(root.FullName,root.FullName.Equals(executableFolder.FullName,StringComparison.OrdinalIgnoreCase));
    }
    internal static GameDiskUsage Measure(string? installDirectory,string? executable,CancellationToken token)
    {
        try{
            var resolved=ResolveRoot(installDirectory,executable);string? root=resolved.Root;bool fallback=resolved.Fallback;
            if(string.IsNullOrEmpty(root))return new((long?)null);
            root=Path.GetFullPath(root);if(root.StartsWith("\\\\",StringComparison.Ordinal))return new((long?)null);ManagedPaths.NoLinks(root);
            var protectedPaths=new[]{Path.GetPathRoot(root),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.Windows)};
            if(protectedPaths.Where(p=>!string.IsNullOrEmpty(p)).Any(p=>Path.TrimEndingDirectorySeparator(p!).Equals(Path.TrimEndingDirectorySeparator(root),StringComparison.OrdinalIgnoreCase)))return new((long?)null);
            long total=0;int entries=0;var pending=new Stack<string>();pending.Push(root);
            while(pending.TryPop(out var dir))foreach(string path in Directory.EnumerateFileSystemEntries(dir)){
                token.ThrowIfCancellationRequested();if(++entries>500000)return new((long?)null);
                var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)return new((long?)null);
                if((attributes&FileAttributes.Directory)!=0)pending.Push(path);else total=checked(total+new FileInfo(path).Length);
            }
            return new(total,fallback);
        }catch(OperationCanceledException){throw;}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or OverflowException){return new((long?)null);}
    }
}

internal static class GameCardText
{
    internal static string Summary(GameInfo game,string? disk=null)
    {
        string source=!string.IsNullOrEmpty(game.SteamAppId)?"Steam":game.Name.Equals("VALORANT",StringComparison.OrdinalIgnoreCase)?"Riot Games":"Локальная игра";
        string time=game.SteamPlaytimeMinutes.HasValue?$"{game.SteamPlaytimeMinutes.Value/60.0:N1} ч":!string.IsNullOrEmpty(game.SteamAppId)?"время не загружено":$"{game.PlayedSeconds/3600:0.0} ч";
        string text=source+" · "+time;
        if(!string.IsNullOrEmpty(disk))text+=" · "+disk;
        if(game.Performance!=null)text+=$" · Цель {game.Performance.Copy().TargetFps} FPS";
        if(game.AchievementSummary!=null)text+=" · "+game.AchievementSummary;
        return text;
    }
}

public partial class Form1
{
    private readonly Dictionary<string,(DateTimeOffset Created,Task<GameDiskUsage> Task)> diskSizes=new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim diskWorkers=new(2,2);
    private readonly CancellationTokenSource diskCancellation=new();
    private bool diskSizingStopped;
    private static string DiskKey(GameInfo game)=>game.InstallDirectory+"|"+game.ExePath;
    private string SizeLabel(GameInfo game)
    {
        if(diskSizes.TryGetValue(DiskKey(game),out var item)&&item.Task.IsCompletedSuccessfully)return item.Task.Result.Label;
        return "Считаем размер…";
    }
    private string CardSubtitle(GameInfo game)=>GameCardText.Summary(game,SizeLabel(game))+(game.LastLaunchUtc.HasValue?$" · Запуск {game.LastLaunchUtc.Value.ToLocalTime():dd.MM HH:mm}":"");
    private async Task<GameDiskUsage> ReadDiskSize(GameInfo game,CancellationToken token)
    {
        string? folder=game.InstallDirectory;string executable=game.ExePath;
        await diskWorkers.WaitAsync(token);
        try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));return await Task.Run(()=>GameDiskUsage.Measure(folder,executable,timeout.Token),timeout.Token);}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){return new((long?)null);}
        finally{diskWorkers.Release();}
    }
    private async Task LoadDiskSizesAsync(int version)
    {
        if(diskSizingStopped||IsDisposed)return;
        var token=diskCancellation.Token;
        var games=visibleGames.ToArray();var items=listBoxGames.Items.ToArray();
        async Task Load(int index){
            var game=games[index];string key=DiskKey(game);
            if(!diskSizes.TryGetValue(key,out var value)||DateTimeOffset.UtcNow-value.Created>TimeSpan.FromMinutes(5))diskSizes[key]=value=(DateTimeOffset.UtcNow,ReadDiskSize(game,token));
            try{await value.Task;if(IsDisposed||token.IsCancellationRequested||version!=iconLoadVersion)return;items[index].Subtitle=CardSubtitle(game);listBoxGames.Invalidate();if(ReferenceEquals(SelectedGame,game))UpdateSpotlight(game);}
            catch(OperationCanceledException){}
        }
        await Task.WhenAll(Enumerable.Range(0,Math.Min(games.Length,items.Length)).Select(Load));
    }
}
