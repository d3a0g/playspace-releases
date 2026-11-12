using System.Security.Cryptography;
using Newtonsoft.Json;

namespace GameLauncherGUI;

public sealed class GameCareOptions
{
    public string Id{get;set;}=Guid.NewGuid().ToString("N");
    public string SaveDirectory{get;set;}="";
    public string Notes{get;set;}="";
    public bool BackupBeforeLaunch{get;set;}
}
internal sealed record BackupFile(string Path,long Size,string Sha256);
internal sealed record GameSnapshot(string Id,string Owner,DateTimeOffset Created,string Kind,List<BackupFile> Files)
{
    public override string ToString()=>$"{Created.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {Kind} · {Files.Count} файлов · {Files.Sum(f=>f.Size)/1048576.0:N1} МБ";
}
internal sealed record RestoreEntry(string Path,string Action);
internal sealed class GameBackupStore
{
    private readonly GameCareOptions options;private readonly string root;private readonly Func<bool> running;
    internal const long MaxBytes=2L*1024*1024*1024;
    internal string Folder=>root;
    internal GameBackupStore(GameCareOptions options,string dataFolder,Func<bool>? running=null)
    {
        if(!Guid.TryParseExact(options.Id,"N",out _))throw new InvalidDataException("Некорректный идентификатор игры.");
        this.options=options;this.running=running??(()=>false);root=Path.GetFullPath(Path.Combine(dataFolder,"game-backups",options.Id));
    }
    private static string Full(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Inside(string path,string folder)=>Full(path).StartsWith(Full(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static void NoLinks(string path)
    {
        for(string? cursor=Path.GetFullPath(path);cursor!=null;cursor=Path.GetDirectoryName(cursor)){
            if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папки и файлы-ссылки не поддерживаются. Выберите обычную папку сохранений.");
        }
    }
    internal string Source(bool allowMissing=false)
    {
        if(string.IsNullOrWhiteSpace(options.SaveDirectory)||!Path.IsPathFullyQualified(options.SaveDirectory))throw new IOException("Сначала выберите отдельную папку сохранений или настроек игры.");
        string source=Full(options.SaveDirectory);
        var protectedFolders=new[]{Environment.SpecialFolder.UserProfile,Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.MyDocuments,Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData,Environment.SpecialFolder.CommonApplicationData,Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86,Environment.SpecialFolder.Windows}.Select(Environment.GetFolderPath).Where(p=>!string.IsNullOrEmpty(p));
        string launcherData=Path.GetDirectoryName(Path.GetDirectoryName(root))!;
        if(source.StartsWith("\\\\",StringComparison.Ordinal)||Full(Path.GetPathRoot(source)!)==source||protectedFolders.Any(p=>Full(p).Equals(source,StringComparison.OrdinalIgnoreCase))||source.Equals(launcherData,StringComparison.OrdinalIgnoreCase)||Inside(source,launcherData)||Inside(root,source))throw new IOException("Выберите папку конкретной игры: корень диска, общие и служебные папки использовать нельзя.");
        string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);if(!string.IsNullOrEmpty(windows)&&Inside(source,windows))throw new IOException("Системные папки Windows нельзя использовать для копий игры.");
        NoLinks(source);NoLinks(root);if(!allowMissing&&!Directory.Exists(source))throw new DirectoryNotFoundException("Папка сохранений не найдена.");return source;
    }
    private void NotRunning(){if(running())throw new IOException("Сначала полностью закройте игру, чтобы не повредить сохранения.");}
    private string Point(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Некорректная точка восстановления.");string path=Path.Combine(root,id);if(!Inside(path,root))throw new IOException("Неверный каталог копии.");NoLinks(path);return path;
    }
    private static string Destination(string folder,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(':')||relative.Split('/','\\').Any(s=>s is "" or "." or ".."||s.EndsWith('.')||s.EndsWith(' ')))throw new InvalidDataException("Недопустимый путь в копии.");
        string path=Path.GetFullPath(Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar)));if(!Inside(path,folder))throw new InvalidDataException("Файл копии выходит за пределы папки.");NoLinks(path);return path;
    }
    private static void DataOnly(string path)
    {
        if(new[]{".exe",".dll",".sys",".msi",".bat",".cmd",".ps1",".vbs",".lnk",".scr"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))throw new IOException("В папке есть исполняемые файлы. Выберите папку только с сохранениями или настройками.");
    }
    private static string Hash(string path){using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return Convert.ToHexString(SHA256.HashData(stream));}
    private static List<string> Files(string folder,CancellationToken ct)
    {
        var files=new List<string>();if(!Directory.Exists(folder))return files;var pending=new Stack<string>();pending.Push(folder);long total=0;int entries=0;
        while(pending.TryPop(out string? current))foreach(string path in Directory.EnumerateFileSystemEntries(current)){
            ct.ThrowIfCancellationRequested();if(++entries>20000)throw new IOException("В папке слишком много файлов и каталогов. Выберите меньшую папку.");NoLinks(path);
            if(Directory.Exists(path)){pending.Push(path);continue;}DataOnly(path);total+=new FileInfo(path).Length;
            if(total>MaxBytes||files.Count>=10000)throw new IOException("Лимит одной копии — 2 ГБ и 10 000 файлов. Выберите отдельную папку сохранений.");files.Add(path);
        }
        return files;
    }
    private static void Copy(string from,string to,CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);using var source=new FileStream(from,FileMode.Open,FileAccess.Read,FileShare.Read);using var target=new FileStream(to,FileMode.CreateNew,FileAccess.Write,FileShare.None);byte[] buffer=new byte[81920];int read;while((read=source.Read(buffer))>0){ct.ThrowIfCancellationRequested();target.Write(buffer,0,read);}target.Flush(true);
    }
    // Only our newly created staging tree is cleaned up; existing snapshots are never rotated or deleted.
    private void CleanStage(string stage)
    {
        if(!Inside(stage,root)||!Path.GetFileName(stage).StartsWith(".stage-",StringComparison.Ordinal))return;NoLinks(stage);if(!Directory.Exists(stage))return;
        foreach(string file in Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories)){NoLinks(file);File.Delete(file);}foreach(string folder in Directory.EnumerateDirectories(stage,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length)){NoLinks(folder);Directory.Delete(folder);}Directory.Delete(stage);
    }
    internal IReadOnlyList<GameSnapshot> List()
    {
        if(!Directory.Exists(root))return Array.Empty<GameSnapshot>();NoLinks(root);var points=new List<GameSnapshot>();
        foreach(string path in Directory.EnumerateDirectories(root)){
            string id=Path.GetFileName(path);if(!Guid.TryParseExact(id,"N",out _))continue;
            try{var snapshot=Read(id);points.Add(snapshot);}catch(Exception ex)when(ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){}
        }
        return points.OrderByDescending(p=>p.Created).ToArray();
    }
    private GameSnapshot Read(string id)
    {
        string path=Point(id);string manifest=Path.Combine(path,"snapshot.json");NoLinks(manifest);if(new FileInfo(manifest).Length>4*1024*1024)throw new InvalidDataException("Слишком большой список файлов копии.");
        var snapshot=JsonConvert.DeserializeObject<GameSnapshot>(File.ReadAllText(manifest))??throw new InvalidDataException("Повреждена информация о копии.");
        if(snapshot.Id!=id||snapshot.Owner!=options.Id||snapshot.Files==null||snapshot.Files.Count>10000)throw new InvalidDataException("Копия не принадлежит этой игре.");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var file in snapshot.Files){if(file==null||file.Size<0||file.Size>MaxBytes||file.Sha256==null||file.Sha256.Length!=64||!file.Sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Повреждён список файлов.");string resolved=Destination(Path.Combine(path,"files"),file.Path);if(!paths.Add(resolved))throw new InvalidDataException("В копии повторяется путь файла.");DataOnly(file.Path);total+=file.Size;if(total>MaxBytes)throw new InvalidDataException("Копия превышает допустимый размер.");}
        return snapshot;
    }
    internal GameSnapshot Create(string kind="Вручную",CancellationToken cancellation=default,bool allowMissing=false)
    {
        using var guard=Lock();return CreateCore(kind,cancellation,allowMissing);
    }
    private FileStream Lock(){Directory.CreateDirectory(root);NoLinks(root);return new FileStream(Path.Combine(root,"backup.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    private GameSnapshot CreateCore(string kind,CancellationToken cancellation,bool allowMissing)
    {
        NotRunning();string source=Source(allowMissing);var files=Files(source,cancellation);Directory.CreateDirectory(root);NoLinks(root);
        if(List().Count>=50)throw new IOException("Для игры уже сохранено 50 копий. Перенесите ненужные копии из папки архива вручную.");
        long bytes=files.Sum(p=>new FileInfo(p).Length);if(new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace<bytes+64L*1024*1024)throw new IOException("На диске недостаточно места для резервной копии.");
        string id=Guid.NewGuid().ToString("N"),stage=Path.Combine(root,".stage-"+id);Directory.CreateDirectory(stage);
        try{
            var entries=new List<BackupFile>();foreach(string file in files){cancellation.ThrowIfCancellationRequested();NoLinks(file);string relative=Path.GetRelativePath(source,file).Replace('\\','/'),copy=Destination(Path.Combine(stage,"files"),relative);Copy(file,copy,cancellation);entries.Add(new(relative,new FileInfo(copy).Length,Hash(copy)));}
            var snapshot=new GameSnapshot(id,options.Id,DateTimeOffset.UtcNow,kind,entries);File.WriteAllText(Path.Combine(stage,"snapshot.json"),JsonConvert.SerializeObject(snapshot,Formatting.Indented));cancellation.ThrowIfCancellationRequested();Directory.Move(stage,Point(id));return snapshot;
        }finally{if(Directory.Exists(stage))CleanStage(stage);}
    }
    internal GameSnapshot Verify(string id,CancellationToken cancellation=default)
    {
        var snapshot=Read(id);string folder=Path.Combine(Point(id),"files");foreach(var entry in snapshot.Files){cancellation.ThrowIfCancellationRequested();string file=Destination(folder,entry.Path);if(!File.Exists(file)||new FileInfo(file).Length!=entry.Size||!Hash(file).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Целостность копии нарушена. Восстановление отменено.");}return snapshot;
    }
    internal IReadOnlyList<RestoreEntry> Preview(string id,CancellationToken cancellation=default)
    {
        string source=Source(true);var snapshot=Verify(id,cancellation);return snapshot.Files.Select(entry=>{string target=Destination(source,entry.Path);if(Directory.Exists(target))throw new IOException("На месте восстанавливаемого файла находится папка. Разрешите конфликт вручную.");return new RestoreEntry(entry.Path,!File.Exists(target)?"Создать":Hash(target).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase)?"Без изменений":"Заменить");}).ToArray();
    }
    internal int Restore(string id,CancellationToken cancellation=default,Action<int>? afterWrite=null)
    {
        using var guard=Lock();
        NotRunning();string source=Source(true);var snapshot=Verify(id,cancellation);var plan=Preview(id,cancellation);var changed=snapshot.Files.Where(f=>plan.Any(p=>p.Path==f.Path&&p.Action!="Без изменений")).ToArray();if(changed.Length==0)return 0;
        var safety=CreateCore("Перед восстановлением",cancellation,true);NotRunning();cancellation.ThrowIfCancellationRequested();var touched=new List<(BackupFile File,bool Existed)>();
        try{
            foreach(var entry in changed){cancellation.ThrowIfCancellationRequested();string target=Destination(source,entry.Path),temporary=target+".playspace-"+Guid.NewGuid().ToString("N")+".tmp";bool existed=File.Exists(target);
                try{Copy(Destination(Path.Combine(Point(id),"files"),entry.Path),temporary,cancellation);NoLinks(target);if(!Hash(temporary).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Данные копии изменились. Восстановление остановлено.");File.Move(temporary,target,true);touched.Add((entry,existed));afterWrite?.Invoke(touched.Count);}finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            return touched.Count;
        }catch{
            bool rollbackFailed=false;foreach(var touchedFile in touched.AsEnumerable().Reverse())try{
                string target=Destination(source,touchedFile.File.Path);if(!File.Exists(target)||!Hash(target).Equals(touchedFile.File.Sha256,StringComparison.OrdinalIgnoreCase)){rollbackFailed=true;continue;}
                var original=safety.Files.SingleOrDefault(f=>f.Path==touchedFile.File.Path);if(original==null){if(!touchedFile.Existed)File.Delete(target);else rollbackFailed=true;continue;}
                string backup=Destination(Path.Combine(Point(safety.Id),"files"),original.Path);if(!Hash(backup).Equals(original.Sha256,StringComparison.OrdinalIgnoreCase)){rollbackFailed=true;continue;}File.Copy(backup,target,true);
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){rollbackFailed=true;}
            if(rollbackFailed)throw new IOException("Восстановление прервано; автоматический откат выполнен не полностью. Исходные файлы сохранены в копии «Перед восстановлением». Проверьте их вручную.");throw;
        }
    }
}
