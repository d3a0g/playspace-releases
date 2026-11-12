using System.Diagnostics;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed class UniversalDlss5
{
    public const string Version="0.5.1";
    public const string ProjectUrl="https://github.com/MotionflowOffical/UniversalDLSS5";
    public const string DownloadUrl=ProjectUrl+"/releases/download/v0.5.1/UniversalDLSS5-0.5.1-Setup-x64.exe";
    internal const string InstallerHash="0ded01f5509a5cbb427806e1574d0f9ed21e200320191022677ec82e66916231";
    internal static readonly IReadOnlyDictionary<string,string> RequiredHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
        ["UniversalDLSS5.exe"]="1671a71a60492075818799856680b16aadb949df23d73c8e222ede32aab123da",
        ["UniversalDLSS5.Bridge.dll"]="f7c0ae595718aef78bf441f8fda197967a84295296a2ecf2eb4e2eb8f2968a22",
        ["UniversalDLSS5.Bridge32.dll"]="cd09f42fec5ee104c1fb9b1b6dd034d706d10baf684265aba990d19513fc918f",
        ["UniversalDLSS5.Injector.exe"]="d21f87f552f197fc86dfcb0713f5d71b45c3c7d6383ed3d9ccdbb81de8347ed0",
        ["UniversalDLSS5.Injector32.exe"]="044a42f24a1eabf496e967585e17de7453ee8170e83addf8c4f9a821e8b643d1",
        ["UniversalDLSS5.NRHost.exe"]="9150117fd3aa4ca0a40d1558c1632daf01ce475e2bba1294e788636d532b1d25",
        ["nvngx.dll_UniversalDLSS5_NRForwarder.dll"]="9589c44bb18976ad5e66b0c6c9458483e49ff9ac28e9cfbcc52be45b01e10ba7"
    };
    private readonly string folder;
    private string Config=>Path.Combine(folder,"controller.json");
    internal string ManagedFolder=>Path.Combine(folder,"v"+Version);
    public UniversalDlss5(string? folder=null){this.folder=Path.GetFullPath(folder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncherPRO","tools","UniversalDLSS5"));}
    public static void ValidateFolder(string root)
    {
        foreach(var file in RequiredHashes){string path=ModInstaller.SafePath(root,file.Key);if(!File.Exists(path)||!DlssManager.Hash(path).Equals(file.Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Файлы UniversalDLSS5 не соответствуют проверенному выпуску 0.5.1: "+file.Key);}
    }
    public string? Controller()
    {
        var candidates=new List<string>{Path.Combine(ManagedFolder,"UniversalDLSS5.exe")};
        if(File.Exists(Config)){try{string? saved=JsonConvert.DeserializeObject<string>(File.ReadAllText(Config));if(saved!=null)candidates.Insert(0,saved);}catch(Exception ex)when(ex is JsonException or IOException or UnauthorizedAccessException){}}
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs")})candidates.Add(Path.Combine(root,"UniversalDLSS5","UniversalDLSS5.exe"));
        foreach(var candidate in candidates){try{if(File.Exists(candidate)){ValidateFolder(Path.GetDirectoryName(candidate)!);return candidate;}}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException){}}
        return null;
    }
    public void RegisterController(string path){if(!Path.GetFileName(path).Equals("UniversalDLSS5.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Выбери UniversalDLSS5.exe.");ValidateFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);Directory.CreateDirectory(folder);File.WriteAllText(Config,JsonConvert.SerializeObject(Path.GetFullPath(path)));}
    public static string? SevenZip()=>new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"7-Zip","7z.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"7-Zip","7z.exe")}.FirstOrDefault(File.Exists);
    public async Task<string> DownloadInstaller(CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);string file=Path.Combine(folder,"UniversalDLSS5-0.5.1-Setup-x64.exe");if(File.Exists(file)&&DlssManager.Hash(file).Equals(InstallerHash,StringComparison.OrdinalIgnoreCase))return file;
        string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{byte[] bytes=await Workspace.Current.Transfers.Fetch("UniversalDLSS5 0.5.1",new Uri(DownloadUrl),16L*1024*1024,cancellation);await File.WriteAllBytesAsync(temp,bytes,cancellation);
            if(!DlssManager.Hash(temp).Equals(InstallerHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Контрольная сумма UniversalDLSS5 не совпала.");File.Move(temp,file,true);return file;
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal async Task<string> ExtractVerified(string installer,string sevenZip,CancellationToken cancellation)
    {
        if(!DlssManager.Hash(installer).Equals(InstallerHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Неверный установочный пакет.");
        if(Directory.Exists(ManagedFolder)){ValidateFolder(ManagedFolder);return Path.Combine(ManagedFolder,"UniversalDLSS5.exe");}
        Directory.CreateDirectory(folder);string stage=Path.Combine(folder,".stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try {
            using var sourceLock=new FileStream(installer,FileMode.Open,FileAccess.Read,FileShare.Read);
            var info=new ProcessStartInfo(sevenZip){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string argument in new[]{"x","-y","-bd","-o"+stage,"-xr!$PLUGINSDIR",installer})info.ArgumentList.Add(argument);
            using var process=Process.Start(info)??throw new IOException("Не удалось открыть архив.");var stdout=process.StandardOutput.ReadToEndAsync(cancellation);var stderr=process.StandardError.ReadToEndAsync(cancellation);
            try{await process.WaitForExitAsync(cancellation);}catch(OperationCanceledException){try{try{process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync();}finally{try{await Task.WhenAll(stdout,stderr);}catch(Exception ex)when(ex is OperationCanceledException or IOException){}}throw;}
            await Task.WhenAll(stdout,stderr);if(process.ExitCode!=0)throw new IOException("7-Zip не смог извлечь пакет.");
            if(Directory.EnumerateFileSystemEntries(stage,"*",SearchOption.AllDirectories).Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0))throw new InvalidDataException("Пакет содержит ссылки файловой системы.");
            ValidateFolder(stage);cancellation.ThrowIfCancellationRequested();string uninstall=Path.Combine(stage,"Uninstall.exe");if(File.Exists(uninstall))File.Delete(uninstall);
            Directory.Move(stage,ManagedFolder);return Path.Combine(ManagedFolder,"UniversalDLSS5.exe");
        }finally{if(Directory.Exists(stage))DeleteStaging(stage);}
    }
    private void DeleteStaging(string stage)
    {
        string full=Path.GetFullPath(stage);if(!string.Equals(Path.GetDirectoryName(full),folder,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith(".stage-",StringComparison.Ordinal))throw new IOException("Недопустимая папка очистки.");
        var stack=new Stack<(string Path,bool Visited)>();stack.Push((full,false));
        while(stack.Count>0){var item=stack.Pop();if(item.Visited){Directory.Delete(item.Path);continue;}stack.Push((item.Path,true));foreach(string entry in Directory.EnumerateFileSystemEntries(item.Path)){if(!Path.GetFullPath(entry).StartsWith(full+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь вышел за папку подготовки.");var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.Directory)!=0){if((attributes&FileAttributes.ReparsePoint)!=0)Directory.Delete(entry);else stack.Push((entry,false));}else File.Delete(entry);}}
    }
    public static string RuntimeDirectory(string? controller=null)
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\UniversalDLSS5");string? configured=key?.GetValue("RuntimePath") as string;
        if(!string.IsNullOrWhiteSpace(configured))return Path.GetFullPath(configured);
        if(controller!=null){string beside=Path.Combine(Path.GetDirectoryName(controller)!,"runtime");if(File.Exists(Path.Combine(beside,"nvngx_dlssnr.dll")))return beside;}
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UniversalDLSS5","runtime");
    }
    public static void ValidateNvidiaRuntimeFile(string path)
    {
        if(!Path.GetFileName(path).Equals("nvngx_dlssnr.dll",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Нужен nvngx_dlssnr.dll, а не DLSS Super Resolution.");
        using var source=File.OpenRead(path);using var pe=new PEReader(source);if(!pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll)||pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64||pe.PEHeaders.PEHeader?.Magic!=PEMagic.PE32Plus)throw new InvalidDataException("Нужна NVIDIA Neural Rendering DLL для Windows x64.");
    }
    public static async Task ImportRuntime(string file,string destination,CancellationToken cancellation)
    {
        ValidateNvidiaRuntimeFile(file);string verifiedHash=DlssManager.Hash(file);await DlssManager.VerifyNvidia(file,cancellation);cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);string target=Path.Combine(destination,"nvngx_dlssnr.dll");
        if(File.Exists(target)){if(DlssManager.Hash(target)==verifiedHash)return;throw new IOException("В runtime уже есть другая DLL. Для её смены используй импорт NVIDIA SDK в контроллере UniversalDLSS5.");}
        string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.Copy(file,temp,false);if(DlssManager.Hash(temp)!=verifiedHash)throw new IOException("Runtime изменился после проверки.");cancellation.ThrowIfCancellationRequested();File.Move(temp,target,false);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static void ValidateGame(GameInfo game,string executable,bool acknowledged,bool checkAntiCheat=true)
    {
        if(!acknowledged)throw new InvalidOperationException("Подтверди экспериментальный режим для одиночной игры.");
        if(!File.Exists(executable)||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("Выбери основной EXE игры.");
        string full=Path.GetFullPath(executable);string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if(full.StartsWith(windows+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Защищённые системные приложения не поддерживаются.");
        bool original=File.Exists(game.ExePath)&&full.Equals(Path.GetFullPath(game.ExePath),StringComparison.OrdinalIgnoreCase);
        bool inGame=Directory.Exists(game.InstallDirectory)&&full.StartsWith(Path.GetFullPath(game.InstallDirectory!).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(!original&&!inGame)throw new IOException("Выбранный EXE не относится к папке этой игры.");
        if(new[]{"steam","epicgameslauncher","riotclientservices","dowser","universal dlss5","universaldlss5"}.Contains(Path.GetFileNameWithoutExtension(full),StringComparer.OrdinalIgnoreCase))throw new IOException("Нужен процесс игры, а не игровой клиент/контроллер.");
        var target=new GameInfo{Name=game.Name,SteamAppId=game.SteamAppId,ExePath=full,InstallDirectory=game.InstallDirectory??Path.GetDirectoryName(full)};
        if(checkAntiCheat&&DlssManager.Blocked(target))throw new IOException("Для этой сетевой игры/обнаруженного античита экспериментальный инжектор отключён.");
    }
    public static ProcessStartInfo StartInfo(string controller,GameInfo? game=null,string? executable=null,bool acknowledged=false)
    {
        ValidateFolder(Path.GetDirectoryName(controller)!);
        var start=new ProcessStartInfo(controller){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(controller)};
        if(game!=null){ValidateGame(game,executable??"",acknowledged);DlssManager.EnsureClosed(game);start.ArgumentList.Add("--launch");start.ArgumentList.Add(Path.GetFullPath(executable!));}
        return start;
    }
}
