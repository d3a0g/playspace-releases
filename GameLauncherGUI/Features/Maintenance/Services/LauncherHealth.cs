using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal static class LauncherSession
{
    internal static bool SafeMode { get; private set; }
    internal static bool StartupFailed { get; set; }
    internal static void Configure(IEnumerable<string> args)=>SafeMode=args.Contains("--safe-mode",StringComparer.OrdinalIgnoreCase);
    internal static string DataFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncherPRO");
    internal static string Redact(string text)
    {
        text=Regex.Replace(text,@"https?://[^\s<>]+",m=>m.Value.Split('?')[0],RegexOptions.IgnoreCase);
        text=Regex.Replace(text,@"\b(?:github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]+|\d{7,}:[A-Za-z0-9_-]{20,}|[a-f0-9]{32,})\b","[секрет скрыт]",RegexOptions.IgnoreCase);
        text=Regex.Replace(text,@"(?i)(password|token|api[_ -]?key|пароль|ключ)\s*[:=]\s*\S+","$1=[скрыто]");
        text=Regex.Replace(text,@"[A-Za-z]:[\\/][^\r\n\""<>|]*","[локальный путь скрыт]");
        text=Regex.Replace(text,@"\\\\[^\r\n\""<>|]+","[сетевой путь скрыт]");
        text=Regex.Replace(text,@"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}","[почта скрыта]");
        text=Regex.Replace(text,@"\b7656119\d{10}\b","[SteamID скрыт]");
        return text;
    }
    internal static void Record(string folder,string action,Exception? error=null)
    {
        try{
            string file=Path.Combine(folder,"launcher-health.json");var list=LocalJson.Read(file,()=>new List<string>());
            list.Add($"{DateTimeOffset.UtcNow:O} · {Redact(action)}"+(error==null?"":" · "+error.GetType().Name));
            LocalJson.Write(file,list.TakeLast(80));
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
    }
    internal static string Report(string folder,int games,string comment)
    {
        var events=LocalJson.Read(Path.Combine(folder,"launcher-health.json"),()=>new List<string>());
        return Redact($"LauncherPlaySpace {UpdateService.DisplayVersion}\nWindows: {Environment.OSVersion.Version}\nАрхитектура: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\nБезопасный режим: {SafeMode}\nИгр в библиотеке: {games}\nВремя UTC: {DateTimeOffset.UtcNow:O}\n\nОписание пользователя:\n{comment}\n\nПоследние действия (без текстов обращений и ключей):\n{string.Join("\n",events.TakeLast(40))}\n\nОтчёт не содержит дамп памяти, токены или список личных файлов. Проверьте его перед передачей.");
    }
}

internal static class ManagedPaths
{
    internal static void NoLinks(string path)
    {
        for(string? current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папки и файлы-ссылки не поддерживаются.");
    }
    internal static string Child(string root,string relative)
    {
        string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(Path.IsPathRooted(relative)||relative.Split('/','\\').Any(p=>p is "" or "." or ".."||p.Contains(':')))throw new InvalidDataException("Недопустимый путь.");
        string path=Path.GetFullPath(Path.Combine(prefix,relative));if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь вне хранилища.");NoLinks(path);return path;
    }
}

internal sealed class SettingsBundle
{
    public List<GameInfo> Games { get; set; }=new();
    public LauncherPreferences Preferences { get; set; }=new();
    public CollectionState Collections { get; set; }=new();
    public List<string> Favorites { get; set; }=new();
}
internal sealed record SettingsEnvelope(int Format,string Sha256,string Payload);
internal static class SettingsTransfer
{
    internal const int MaxBytes=16*1024*1024;
    private static GameInfo Portable(GameInfo game)
    {
        var profile=game.Performance?.Copy();if(profile!=null)profile.ApplyOnLaunch=false;
        return new(){Name=game.Name,ExePath=game.ExePath,SteamAppId=game.SteamAppId,LibraryId=game.LibraryId,IsPinned=game.IsPinned,
            InstallDirectory=game.InstallDirectory,TrackingExecutable=game.TrackingExecutable,PlayedSeconds=game.PlayedSeconds,LastLaunchUtc=game.LastLaunchUtc,
            Performance=profile,Care=game.Care==null?null:new(){Id=game.Care.Id,SaveDirectory=game.Care.SaveDirectory,BackupBeforeLaunch=false},CheckUbisoftBeforeLaunch=game.CheckUbisoftBeforeLaunch};
    }
    internal static SettingsBundle Capture(string folder)
    {
        var p=LauncherPreferences.Load(folder);p.UpdateFeedUrl="";p.LastNotifiedVersion="";
        return new(){Games=GameLibraryStore.Load(Path.Combine(folder,"games.json")).Games.Select(Portable).ToList(),Preferences=p,
            Collections=LocalJson.Read(Path.Combine(folder,"collections.json"),()=>new CollectionState()),
            Favorites=LocalJson.Read(Path.Combine(folder,"recommendation-favorites.json"),()=>new List<string>())};
    }
    internal static void Export(string folder,string destination)
    {
        ManagedPaths.NoLinks(folder);ManagedPaths.NoLinks(destination);
        string prefix=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string target=Path.GetFullPath(destination);
        if(target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&!target.StartsWith(ManagedPaths.Child(folder,"settings-backups")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Внутри хранилища сохраняйте копии только в settings-backups, чтобы не заменить рабочие настройки.");
        string payload=JsonConvert.SerializeObject(Capture(folder));if(Encoding.UTF8.GetByteCount(payload)>MaxBytes)throw new IOException("Слишком большой объём настроек.");
        LocalJson.Write(destination,new SettingsEnvelope(1,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),payload));
    }
    internal static SettingsBundle Read(string file)
    {
        ManagedPaths.NoLinks(file);if(new FileInfo(file).Length>MaxBytes*2)throw new InvalidDataException("Файл настроек слишком большой.");
        var envelope=JsonConvert.DeserializeObject<SettingsEnvelope>(File.ReadAllText(file))??throw new InvalidDataException("Пустая копия.");
        if(envelope.Format!=1||envelope.Payload==null||Encoding.UTF8.GetByteCount(envelope.Payload)>MaxBytes||envelope.Sha256!=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))))throw new InvalidDataException("Неверный формат или повреждена контрольная сумма копии.");
        var b=JsonConvert.DeserializeObject<SettingsBundle>(envelope.Payload,new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MaxDepth=32})??throw new InvalidDataException("Нет настроек.");
        if(b.Games==null||b.Preferences==null||b.Collections?.Names==null||b.Collections.Members==null||b.Favorites==null||b.Games.Count>5000||b.Collections.Names.Count>24)throw new InvalidDataException("Повреждена структура копии.");
        if(b.Games.Any(g=>g==null||string.IsNullOrWhiteSpace(g.Name)||g.Name.Length>200||string.IsNullOrWhiteSpace(g.ExePath)||g.ExePath.Length>4096||!Guid.TryParseExact(g.LibraryId,"N",out _)||!string.IsNullOrEmpty(g.SteamAppId)&&(!uint.TryParse(g.SteamAppId,out var id)||id==0))||b.Games.Select(g=>g.LibraryId).Distinct().Count()!=b.Games.Count)throw new InvalidDataException("Некорректная библиотека в копии.");
        if(!Enum.IsDefined(b.Preferences.CloseBehavior)||b.Collections.Names.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>36)||b.Collections.Members.Any(p=>!Guid.TryParseExact(p.Key,"N",out _)||p.Value==null||p.Value.Any(v=>!b.Collections.Names.Contains(v)))||b.Favorites.Count>5000||b.Favorites.Any(v=>!uint.TryParse(v,out _)))throw new InvalidDataException("Некорректные коллекции или настройки.");
        if(b.Games.Any(g=>string.IsNullOrEmpty(g.SteamAppId)&&(!Path.IsPathFullyQualified(g.ExePath)||!Path.GetExtension(g.ExePath).Equals(".exe",StringComparison.OrdinalIgnoreCase))))throw new InvalidDataException("Локальные игры должны ссылаться на полный путь EXE, а не команду или ссылку.");
        b.Games=b.Games.Select(Portable).ToList();b.Preferences.UpdateFeedUrl="";b.Preferences.LastNotifiedVersion="";b.Preferences.OnboardingCompleted=true;return b;
    }
    internal static string Restore(string folder,string source,Action<string,object>? writer=null)
    {
        var b=Read(source);ManagedPaths.NoLinks(folder);Directory.CreateDirectory(folder);
        string safety=ManagedPaths.Child(folder,"settings-backups/before-restore-"+Guid.NewGuid().ToString("N")+".json");Export(folder,safety);
        var values=new Dictionary<string,object>{{"games.json",b.Games},{"preferences.json",b.Preferences},{"collections.json",b.Collections},{"recommendation-favorites.json",b.Favorites}};
        var originals=values.Keys.ToDictionary(k=>k,k=>{string path=ManagedPaths.Child(folder,k);return File.Exists(path)?File.ReadAllBytes(path):null;});
        try{foreach(var pair in values)(writer??LocalJson.Write)(ManagedPaths.Child(folder,pair.Key),pair.Value);}
        catch{foreach(var pair in originals){string path=ManagedPaths.Child(folder,pair.Key);if(pair.Value==null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,pair.Value);}throw;}
        return safety;
    }
}

internal sealed record StorageFile(string Relative,long Bytes,long Modified);
internal sealed record StorageGroup(string Name,string Relative,bool CanDelete);
internal static class LauncherStorage
{
    internal static readonly StorageGroup[] Groups={new("Обложки и фон","posters",true),new("Описание и скриншоты Steam","store-details",true),new("Кеш загрузок","transfer-cache",true),new("Кеш каталога модов","mod-catalog",true),new("Копии настроек","settings-backups",true),new("Резервные копии игр","game-backups",true)};
    internal static IReadOnlyList<StorageFile> Scan(string root,string relative,CancellationToken ct=default)
    {
        string folder=ManagedPaths.Child(root,relative);var result=new List<StorageFile>();if(!Directory.Exists(folder))return result;
        var pending=new Stack<string>();pending.Push(folder);int count=0;
        while(pending.TryPop(out var dir))foreach(var path in Directory.EnumerateFileSystemEntries(dir)){
            ct.ThrowIfCancellationRequested();if(++count>100000)throw new IOException("Слишком много файлов: выберите меньшую папку.");ManagedPaths.NoLinks(path);
            if(Directory.Exists(path))pending.Push(path);else{var info=new FileInfo(path);result.Add(new(Path.GetRelativePath(root,path),info.Length,info.LastWriteTimeUtc.Ticks));}
        }
        return result;
    }
    internal static int Delete(string root,StorageGroup group,IReadOnlyList<StorageFile> preview)
    {
        if(!Groups.Contains(group)||!group.CanDelete)throw new IOException("Удаление доступно только для управляемого хранилища.");
        foreach(var item in preview){string path=ManagedPaths.Child(root,item.Relative);string prefix=ManagedPaths.Child(root,group.Relative)+Path.DirectorySeparatorChar;if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл вне выбранного раздела.");if(!File.Exists(path))continue;var info=new FileInfo(path);if(info.Length!=item.Bytes||info.LastWriteTimeUtc.Ticks!=item.Modified)throw new IOException("После предпросмотра файлы изменились. Обновите список.");}
        int removed=0;foreach(var item in preview){string path=ManagedPaths.Child(root,item.Relative);if(File.Exists(path)){var info=new FileInfo(path);if(info.Length!=item.Bytes||info.LastWriteTimeUtc.Ticks!=item.Modified)throw new IOException("Файл изменился во время очистки. Обновите список.");File.Delete(path);removed++;}}return removed;
    }
}
