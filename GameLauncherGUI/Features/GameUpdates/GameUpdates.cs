using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GameLauncherGUI;

internal enum GameUpdateState { CheckInClient, Pending, Partial, Unknown, ReadError }
internal sealed record GameUpdateEntry(GameInfo Game,string Client,GameUpdateState State,string Detail)
{
    internal string Label=>State switch{GameUpdateState.Pending=>"Нужна загрузка",GameUpdateState.Partial=>"Загружено частично",GameUpdateState.ReadError=>"Ошибка чтения",GameUpdateState.Unknown=>"Нет данных клиента",_=>"Проверить в клиенте"};
}
internal static class GameUpdates
{
    private static string? LocalFolder(string? path)
    {
        try{return string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.StartsWith("\\\\")?null:Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));}
        catch(Exception ex)when(ex is ArgumentException or NotSupportedException){return null;}
    }
    private static HashSet<string> EpicRoots()
    {
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Epic","EpicGamesLauncher","Data","Manifests");
        try{if(Directory.Exists(folder))foreach(string file in Directory.EnumerateFiles(folder,"*.item").Take(2000))try{if(new FileInfo(file).Length>1024*1024)continue;var data=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));if(data.Value<string>("InstallLocation") is string path&&!string.IsNullOrWhiteSpace(path))roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or Newtonsoft.Json.JsonException){}}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
        return roots;
    }
    internal static bool SteamId(string? id)=>!string.IsNullOrWhiteSpace(id)&&id.All(char.IsAsciiDigit)&&uint.TryParse(id,out var value)&&value>0;
    private static bool ManifestMatches(GameInfo game,string manifest)
    {
        try
        {
            if(new FileInfo(manifest).Length>4*1024*1024)return false;
            string directory=Field(File.ReadAllText(manifest),"installdir");if(string.IsNullOrWhiteSpace(directory))return false;
            string? installed=LocalFolder(Path.Combine(Path.GetDirectoryName(manifest)!,"common",directory));
            if(installed==null)return false;
            if(Directory.Exists(installed)&&(File.GetAttributes(installed)&FileAttributes.ReparsePoint)!=0)installed=Directory.ResolveLinkTarget(installed,true)?.FullName??installed;
            return string.Equals(LocalFolder(game.InstallDirectory),LocalFolder(installed),StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){return false;}
    }
    internal static string Client(GameInfo game)
    {
        if(SteamId(game.SteamAppId))return "Steam";
        if(UbisoftLaunch.Recognized(game))return "Ubisoft Connect";
        string path=(game.ExePath+" "+game.InstallDirectory).Replace('\\','/');
        if(game.Name.Equals("VALORANT",StringComparison.OrdinalIgnoreCase)||path.Contains("/Riot Games/",StringComparison.OrdinalIgnoreCase))return "Riot Client";
        if(path.Contains("/Epic Games/",StringComparison.OrdinalIgnoreCase))return "Epic Games";
        return "Локальная игра";
    }
    internal static string Field(string text,string key)=>Regex.Match(text,"\""+Regex.Escape(key)+"\"\\s+\"([^\"]*)\"",RegexOptions.IgnoreCase).Groups[1].Value;
    internal static GameUpdateEntry Steam(GameInfo game,string text)
    {
        if(!SteamId(game.SteamAppId)||Field(text,"appid")!=game.SteamAppId)throw new InvalidDataException("Манифест не принадлежит этой игре.");
        ulong Number(string key)=>ulong.TryParse(Field(text,key),out var value)?value:0;
        ulong total=Number("BytesToDownload"),received=Number("BytesDownloaded"),build=Number("buildid"),target=Number("TargetBuildID");
        bool pending=total>received||(target>0&&target!=build)||(Number("StateFlags")&2)!=0;
        var state=pending?(received>0&&total>received?GameUpdateState.Partial:GameUpdateState.Pending):GameUpdateState.CheckInClient;
        string branch=Field(text,"BetaKey");
        string detail="Источник: локальный манифест Steam.\nУстановленная сборка: "+(build>0?build.ToString():"не указана")+(branch.Length>0?"\nВетка: "+branch:"");
        if(target>0)detail+="\nЦелевая сборка клиента: "+target;
        if(total>0)detail+=$"\nЗагрузка клиента: {GameDiskUsage.Format((long)Math.Min(received,(ulong)long.MaxValue))} / {GameDiskUsage.Format((long)Math.Min(total,(ulong)long.MaxValue))}";
        detail+="\n\n"+(pending?"Steam сохранил сведения о требуемой загрузке. Она может быть приостановлена или отложена.":"Клиент не сообщает о подготовленной загрузке. Это не подтверждение актуальности игры.")+"\n\nОткройте Steam для проверки доступной версии и запуска обновления. Ветки, очередь и файлы магазина лаунчер не изменяет.";
        return new(game,"Steam",state,detail);
    }
    internal static IReadOnlyList<GameUpdateEntry> Read(IEnumerable<GameInfo> games,IEnumerable<string>? steamRoots=null,CancellationToken token=default,IEnumerable<string>? epicRoots=null)
    {
        var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var epic=new HashSet<string>(epicRoots??EpicRoots(),StringComparer.OrdinalIgnoreCase);
        foreach(string candidate in steamRoots??GameIconCache.SteamFolders())
        {
            string? root=LocalFolder(candidate);if(root==null)continue;
            folders.Add(Path.Combine(root,"steamapps"));string file=Path.Combine(root,"steamapps","libraryfolders.vdf");
            try{if(File.Exists(file)&&new FileInfo(file).Length<4*1024*1024)foreach(Match match in Regex.Matches(File.ReadAllText(file),"\"path\"\\s+\"([^\"]+)\"")){string? library=LocalFolder(match.Groups[1].Value.Replace("\\\\","\\"));if(library!=null)folders.Add(Path.Combine(library,"steamapps"));}}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){}
        }
        var result=new List<GameUpdateEntry>();
        foreach(var game in games)
        {
            token.ThrowIfCancellationRequested();string client=Client(game);
            if(client=="Локальная игра"&&LocalFolder(game.InstallDirectory) is string install&&epic.Contains(install))client="Epic Games";
            if(client!="Steam"){result.Add(new(game,client,client=="Локальная игра"?GameUpdateState.Unknown:GameUpdateState.CheckInClient,"Нет надёжных сведений о доступной версии. Проверка и загрузка выполняются в игровом клиенте. Для локальной игры используйте официальный источник установки."));continue;}
            var candidates=folders.Select(folder=>Path.Combine(folder,"appmanifest_"+game.SteamAppId+".acf")).Where(File.Exists).ToArray();
            string? file=candidates.FirstOrDefault(path=>ManifestMatches(game,path))??candidates.FirstOrDefault();
            if(file==null){result.Add(new(game,client,GameUpdateState.Unknown,"Манифест Steam не найден. Откройте клиент, чтобы проверить установку и обновления."));continue;}
            try{if(new FileInfo(file).Length>4*1024*1024)throw new IOException("Манифест слишком велик.");result.Add(Steam(game,File.ReadAllText(file)));}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){result.Add(new(game,client,GameUpdateState.ReadError,"Не удалось прочитать состояние установки. Откройте Steam или повторите проверку после завершения обновления клиента."));}
        }
        return result.OrderBy(e=>e.State is GameUpdateState.Pending or GameUpdateState.Partial?0:1).ThenBy(e=>e.Game.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    internal static string? PageUri(GameUpdateEntry entry)=>entry.Client=="Steam"&&SteamId(entry.Game.SteamAppId)?"steam://nav/games/details/"+entry.Game.SteamAppId:null;
    internal static void Open(GameUpdateEntry entry,bool gamePage=false)
    {
        string? uri=gamePage?PageUri(entry):entry.Client switch{"Steam"=>"steam://nav/downloads","Epic Games"=>"com.epicgames.launcher://library",_=>null};
        if(uri!=null){Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});return;}
        if(entry.Client=="Ubisoft Connect"){UbisoftLaunch.OpenClient();return;}
        if(entry.Client=="Riot Client")
        {
            string manifest=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Riot Games","RiotClientInstalls.json");
            if(File.Exists(manifest)&&new FileInfo(manifest).Length<1024*1024){var data=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));foreach(string key in new[]{"rc_default","rc_live"})if(data.Value<string>(key) is string path&&File.Exists(path)&&Path.GetFileName(path).Equals("RiotClientServices.exe",StringComparison.OrdinalIgnoreCase)){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});return;}}
            throw new IOException("Riot Client не найден. Откройте его вручную.");
        }
        string? folder=Directory.Exists(entry.Game.InstallDirectory)?entry.Game.InstallDirectory:Path.GetDirectoryName(entry.Game.ExePath);
        if(!Directory.Exists(folder))throw new IOException("Папка игры не найдена.");Process.Start(new ProcessStartInfo(folder!){UseShellExecute=true});
    }
}
