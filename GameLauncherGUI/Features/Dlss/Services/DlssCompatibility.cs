using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record DlssDetectedFile(string Path,string Relative,string Kind,string Version,long Bytes,long ModifiedUtc)
{
    internal string Description=>$"{Kind} · {Version}\n{Relative}";
}
internal sealed record DlssSnapshot(string Adapter,string GameVersion,DlssDetectedFile[] Files,bool Restricted,bool Incomplete,string ConfigHash)
{
    internal string Context=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Adapter.Trim().ToUpperInvariant()+"|"+GameVersion+"|"+ConfigHash+"|"+Restricted+"|"+Incomplete+"|"+string.Join("|",Files.OrderBy(f=>f.Relative,StringComparer.OrdinalIgnoreCase).Select(f=>$"{f.Relative.ToUpperInvariant()}:{f.Version}:{f.Bytes}:{f.ModifiedUtc}")))));
    internal string Libraries=>Files.Length==0?(Incomplete?"Сканирование неполное; библиотеки не обнаружены.":"Библиотеки DLSS не найдены."):string.Join("\n\n",Files.Select(f=>f.Description));
    internal string Features
    {
        get{
            var gpu=RtxCompatibility.Detect(Adapter);bool sr=Files.Any(f=>f.Kind=="Super Resolution"),fg=Files.Any(f=>f.Kind=="Frame Generation");
            return "SUPER RESOLUTION\n"+(!gpu.SuperResolution?"Совместимая GeForce RTX не определена.":sr?"GPU поддерживается, библиотека найдена. Работа и выбранная модель проверяются в самой игре.":"GPU поддерживается; библиотека в просканированной папке не найдена.")+"\n\nNVIDIA FRAME GENERATION\n"+(!gpu.NativeFrameGeneration?"Для определённой видеокарты недоступна или поддержка не определена.":fg?"GPU поддерживается, библиотека найдена. Нужны поддержка игры и подходящий драйвер.":"GPU поддерживается, но библиотека Frame Generation не найдена.")+"\n\nMULTI FRAME GENERATION\n"+(gpu.Generation==50?"GPU поддерживается. Нужны совместимые игра, драйвер и настройки; наличие DLL не подтверждает активацию.":"Для определённой видеокарты недоступна или поддержка не определена.")+"\n\nFSR ЧЕРЕЗ OPTISCALER\n"+(gpu.SuperResolution?"Возможен отдельный экспериментальный режим DX12. Требуется проверка игры; это альтернативная генерация кадров.":"Режим RTX лаунчера отключён: совместимая GeForce RTX не определена.")+"\n\nDLSS 5\nОтдельный экспериментальный модуль. Эта проверка не определяет его работоспособность в игре.\n\n"+(Restricted?"УСТАНОВКА ОГРАНИЧЕНА\nИгра относится к известным сетевым или обнаружен античит; также блокировка возможна, если папку не удалось полностью проверить.\n\n":"")+(Incomplete?"СКАНИРОВАНИЕ НЕПОЛНОЕ\nЧасть файлов недоступна или достигнут лимит обхода. Отсутствие DLL не доказывает отсутствие функции.\n\n":"")+"Версии читаются из метаданных файлов. Подпись, активный GPU, API рендера и драйвер этой карточкой не проверяются. При установке подпись NVIDIA проверяется отдельно.";
        }
    }
    internal static DlssSnapshot Read(GameInfo game,string adapter,CancellationToken token)
    {
        var kinds=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"nvngx_dlss.dll","Super Resolution"},{"nvngx_dlssg.dll","Frame Generation"},{"nvngx_dlssd.dll","Ray Reconstruction"},{"nvngx_dlssnr.dll","Neural Rendering"}};
        var files=new List<DlssDetectedFile>();bool incomplete=false;string version="Не определена",config="";
        if(File.Exists(game.ExePath)){try{version=FileVersionInfo.GetVersionInfo(game.ExePath).FileVersion??version;string ini=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(game.ExePath))!,"OptiScaler.ini");if(File.Exists(ini)){ManagedPaths.NoLinks(ini);if(new FileInfo(ini).Length<=128*1024)config=DlssManager.Hash(ini);else incomplete=true;}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){incomplete=true;}}
        string? root=GameDiskUsage.ResolveRoot(game.InstallDirectory,game.ExePath).Root;
        if(root==null)return new(adapter,version,Array.Empty<DlssDetectedFile>(),DlssManager.Blocked(game),true,config);
        root=Path.GetFullPath(root);
        if(new[]{Path.GetPathRoot(root),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Where(p=>!string.IsNullOrEmpty(p)).Any(p=>Path.TrimEndingDirectorySeparator(p!).Equals(Path.TrimEndingDirectorySeparator(root),StringComparison.OrdinalIgnoreCase)))return new(adapter,version,Array.Empty<DlssDetectedFile>(),true,true,config);
        ManagedPaths.NoLinks(root);var pending=new Queue<(string Directory,int Depth)>();pending.Enqueue((root,0));int visited=0;
        while(pending.TryDequeue(out var item)){
            token.ThrowIfCancellationRequested();
            try{foreach(string path in Directory.EnumerateFileSystemEntries(item.Directory)){
                token.ThrowIfCancellationRequested();if(++visited>50000){incomplete=true;pending.Clear();break;}var attributes=File.GetAttributes(path);
                if((attributes&FileAttributes.ReparsePoint)!=0){incomplete=true;continue;}
                if((attributes&FileAttributes.Directory)!=0){if(item.Depth<8&&Path.GetFileName(path)!=".playspace-mods")pending.Enqueue((path,item.Depth+1));else if(item.Depth>=8)incomplete=true;continue;}
                if(kinds.TryGetValue(Path.GetFileName(path),out string? kind)){if(files.Count>=64){incomplete=true;pending.Clear();break;}var info=new FileInfo(path);string detected=FileVersionInfo.GetVersionInfo(path).FileVersion??"Версия не определена";files.Add(new(path,Path.GetRelativePath(root,path),kind,detected,info.Length,info.LastWriteTimeUtc.Ticks));}
            }}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){incomplete=true;}
        }
        return new(adapter,version,files.OrderBy(f=>f.Kind).ThenBy(f=>f.Relative).ToArray(),DlssManager.Blocked(game),incomplete,config);
    }
}
internal enum DlssTestOutcome { Works, Issues, Fails }
internal sealed record DlssUserCheck(string Context,string Adapter,string GameVersion,DlssTestOutcome Outcome,string Note,DateTimeOffset CheckedUtc)
{
    internal string Result=>Outcome switch{DlssTestOutcome.Works=>"Работает",DlssTestOutcome.Issues=>"Есть проблемы",_=>"Не работает"};
}
internal sealed class DlssCheckStore
{
    private readonly string file;
    internal DlssCheckStore(string folder,GameInfo game)
    {
        string id=!string.IsNullOrEmpty(game.SteamAppId)?"steam:"+game.SteamAppId:"exe:"+game.ExePath.Trim().ToUpperInvariant();string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        file=ManagedPaths.Child(folder,"dlss-checks/"+key+".json");
    }
    internal DlssUserCheck[] Read()
    {
        ManagedPaths.NoLinks(file);if(!File.Exists(file))return Array.Empty<DlssUserCheck>();if(new FileInfo(file).Length>256*1024)throw new InvalidDataException("Файл проверок слишком большой.");
        var checks=JsonConvert.DeserializeObject<DlssUserCheck[]>(File.ReadAllText(file))??throw new InvalidDataException("Повреждён файл проверок.");
        if(checks.Length>100||checks.Any(c=>c==null||c.Context?.Length!=64||!c.Context.All(Uri.IsHexDigit)||!Enum.IsDefined(c.Outcome)||c.Note==null||c.Note.Length>600||c.Adapter==null||c.Adapter.Length>1024||c.GameVersion==null||c.GameVersion.Length>200))throw new InvalidDataException("Некорректные сохранённые проверки.");
        return checks.OrderByDescending(c=>c.CheckedUtc).ToArray();
    }
    internal void Save(DlssSnapshot snapshot,DlssTestOutcome outcome,string note)
    {
        if(!Enum.IsDefined(outcome)||note.Length>600||snapshot.Adapter.Length>1024||snapshot.GameVersion.Length>200)throw new ArgumentException("Некорректный результат проверки.");
        note=new string(note.Where(c=>!char.IsControl(c)||c is '\r' or '\n' or '\t').ToArray()).Trim();
        Write(checks=>checks.Where(c=>c.Context!=snapshot.Context).Prepend(new(snapshot.Context,snapshot.Adapter,snapshot.GameVersion,outcome,note,DateTimeOffset.UtcNow)).Take(100).ToArray());
    }
    internal void Remove(string context)=>Write(checks=>checks.Where(c=>c.Context!=context).ToArray());
    private void Write(Func<DlssUserCheck[],DlssUserCheck[]> change)
    {
        ManagedPaths.NoLinks(file);ManagedPaths.NoLinks(file+".lock");Directory.CreateDirectory(Path.GetDirectoryName(file)!);using var gate=new FileStream(file+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);LocalJson.Write(file,change(Read()));
    }
}
