using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharpCompress.Archives.SevenZip;

namespace GameLauncherGUI;

internal enum RtxFrameMode { Disabled, Streamline, Fsr31, Fsr30, Nukem, Upscaler }
internal enum RtxPreset { GameDefault, K, L, M }

internal sealed class OptiScalerManager
{
    internal const string Version="0.9.4";
    internal const string PackageId="PlaySpace-OptiScaler";
    internal const string ProjectUrl="https://github.com/optiscaler/OptiScaler";
    internal const string DownloadUrl=ProjectUrl+"/releases/download/v0.9.4/Optiscaler_0.9.4-final.20260718._MM.7z";
    internal const string ArchiveHash="575cb4df866116093df75af607e37fd70e10f5163e0f23fd5c804142e80ef0ad";
    internal static readonly string[] Payload={"OptiScaler.dll","OptiScaler.ini","amd_fidelityfx_dx12.dll","amd_fidelityfx_framegeneration_dx12.dll","amd_fidelityfx_upscaler_dx12.dll","amd_fidelityfx_vk.dll","dlssg_to_fsr3_amd_is_better.dll","fakenvapi.dll","fakenvapi.ini","libxell.dll","libxess.dll","libxess_dx11.dll","libxess_fg.dll","D3D12_Optiscaler/D3D12Core.dll","Licenses/DirectX_LICENSE.txt","Licenses/FidelityFX_v1_LICENSE.md","Licenses/FidelityFX_v2_LICENSE.md","Licenses/XeSS_LICENSE.txt"};
    private readonly string cache;
    internal OptiScalerManager(string? cache=null)=>this.cache=Path.GetFullPath(cache??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncherPRO","tools","OptiScaler"));

    internal static string ValidateGame(GameInfo game,string executable,bool acknowledged,string adapter,bool dx12Confirmed)
    {
        if(LauncherSession.SafeMode)throw new IOException("Установка модулей отключена в безопасном режиме.");
        if(!RtxCompatibility.Detect(adapter).SuperResolution)throw new IOException("Совместимая GeForce RTX не определена.");
        if(!dx12Confirmed)throw new IOException("Подтверди запуск игры в DirectX 12 и наличие DLSS/FSR2+/XeSS.");
        UniversalDlss5.ValidateGame(game,executable,acknowledged);
        ManagedPaths.NoLinks(executable);
        using(var stream=File.OpenRead(executable))using(var pe=new PEReader(stream))
            if(pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64||pe.PEHeaders.PEHeader?.Magic!=PEMagic.PE32Plus||pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll))throw new IOException("Нужен основной Windows x64 EXE игры.");
        string root=Path.GetDirectoryName(Path.GetFullPath(executable))!;
        // Check the complete game tree as well as the selected shipping executable directory.
        var probe=new GameInfo{Name=game.Name,SteamAppId=game.SteamAppId,ExePath=executable,InstallDirectory=root};
        if(DlssManager.Blocked(game)||DlssManager.Blocked(probe))throw new IOException("Изменения отключены: обнаружена сетевая игра или античит.");
        return root;
    }
    internal async Task<string> Download(CancellationToken token)
    {
        ManagedPaths.NoLinks(cache);Directory.CreateDirectory(cache);string target=ModInstaller.SafePath(cache,"OptiScaler-0.9.4.7z");
        if(File.Exists(target)&&DlssManager.Hash(target).Equals(ArchiveHash,StringComparison.OrdinalIgnoreCase))return target;
        string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{var bytes=await Workspace.Current.Transfers.Fetch("OptiScaler 0.9.4 · RTX / FSR FG",new Uri(DownloadUrl),64L*1024*1024,token);await File.WriteAllBytesAsync(temp,bytes,token);if(!DlssManager.Hash(temp).Equals(ArchiveHash,StringComparison.OrdinalIgnoreCase))throw new IOException("Контрольная сумма OptiScaler не совпала.");token.ThrowIfCancellationRequested();File.Move(temp,target,true);return target;}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static string Configure(string ini,RtxFrameMode mode,RtxPreset preset)
    {
        if(!Enum.IsDefined(mode)||!Enum.IsDefined(preset))throw new ArgumentException("Неизвестный режим графики.");
        string input=mode switch{RtxFrameMode.Streamline=>"dlssg",RtxFrameMode.Fsr31=>"fsrfg",RtxFrameMode.Fsr30=>"fsrfg30",RtxFrameMode.Nukem=>"nukems",RtxFrameMode.Upscaler=>"upscaler",_=>"nofg"};
        ini=Set(ini,"Upscalers","Dx12Upscaler","dlss");
        ini=Set(ini,"FrameGen","Enabled",mode==RtxFrameMode.Disabled?"false":"true");ini=Set(ini,"FrameGen","FGInput",input);ini=Set(ini,"FrameGen","FGOutput",mode==RtxFrameMode.Disabled?"nofg":mode==RtxFrameMode.Nukem?"nukems":"fsrfg");
        ini=Set(ini,"DLSS","RenderPresetOverride",preset==RtxPreset.GameDefault?"false":"true");
        return Set(ini,"DLSS","RenderPresetForAll",preset switch{RtxPreset.K=>"11",RtxPreset.L=>"12",RtxPreset.M=>"13",_=>"0"});
    }
    private static string Set(string ini,string section,string key,string value)
    {
        bool active=false,found=false;var lines=ini.Replace("\r\n","\n").Split('\n');
        for(int i=0;i<lines.Length;i++){var text=lines[i].Trim();if(text.StartsWith('['))active=text.Equals("["+section+"]",StringComparison.OrdinalIgnoreCase);else if(active&&Regex.IsMatch(text,"^"+Regex.Escape(key)+@"\s*=",RegexOptions.IgnoreCase)){if(found)throw new IOException("Повторяющийся параметр OptiScaler.");lines[i]=key+"="+value;found=true;}}
        if(!found)throw new IOException("В пакете OptiScaler отсутствует параметр "+section+"/"+key);return string.Join("\r\n",lines);
    }
    internal static Dictionary<string,string> Extract(string archivePath,string stage,CancellationToken token)
    {
        Directory.CreateDirectory(stage);ManagedPaths.NoLinks(stage);
        using var input=new FileStream(archivePath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(!Convert.ToHexString(SHA256.HashData(input)).Equals(ArchiveHash,StringComparison.OrdinalIgnoreCase))throw new IOException("Неизвестный пакет OptiScaler.");input.Position=0;
        using var archive=SevenZipArchive.OpenArchive(input);
        var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var entry in archive.Entries){token.ThrowIfCancellationRequested();if(entry.IsDirectory)continue;string key=entry.Key?.Replace('\\','/')??"";ModInstaller.SafePath(stage,key);
            if(!Payload.Contains(key,StringComparer.OrdinalIgnoreCase))continue;
            if(entry.IsEncrypted||entry.Size<0||entry.Size>100L*1024*1024||(total+=entry.Size)>256L*1024*1024)throw new IOException("Недопустимое содержимое OptiScaler.");
            string file=ModInstaller.SafePath(stage,key);Directory.CreateDirectory(Path.GetDirectoryName(file)!);using var source=entry.OpenEntryStream();using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var buffer=new byte[65536];long written=0;int count;while((count=source.Read(buffer))>0){token.ThrowIfCancellationRequested();if((written+=count)>entry.Size)throw new IOException("Превышен размер файла.");output.Write(buffer,0,count);}if(written!=entry.Size)throw new IOException("Неполный файл OptiScaler.");}
            if(!files.TryAdd(key,file))throw new IOException("Повторяющийся файл OptiScaler.");
        }
        if(Payload.Any(key=>!files.ContainsKey(key)))throw new IOException("Пакет OptiScaler неполон.");return files;
    }
    internal async Task Install(GameInfo game,string executable,string adapter,bool consent,bool dx12,RtxFrameMode mode,RtxPreset preset,IProgress<string> progress,CancellationToken token)
    {
        string root=ValidateGame(game,executable,consent,adapter,dx12);DlssManager.EnsureClosed(game);
        if(File.Exists(ModInstaller.SafePath(root,"dxgi.dll"))||File.Exists(ModInstaller.SafePath(root,"OptiScaler.ini")))throw new IOException("В папке уже есть dxgi.dll или OptiScaler.ini. Сначала удали прежний мод через его установщик.");
        progress.Report("Загружаем проверенный OptiScaler 0.9.4…");string archive=await Download(token);
        progress.Report("Загружаем DLSS 4.5 и проверяем подпись NVIDIA…");string runtime=await DlssManager.Download(DlssLibrary.All.Single(l=>l.Tag=="v310.5.3"),cache,token);string runtimeHash=DlssManager.Hash(runtime);
        string stage=ModInstaller.SafePath(cache,"stage-"+Guid.NewGuid().ToString("N"));
        try{await Task.Run(()=>{var files=Extract(archive,stage,token);string ini=files["OptiScaler.ini"];string runtimeRelative="PlaySpace.OptiScaler/nvngx_dlss.dll";string runtimeCopy=ModInstaller.SafePath(stage,runtimeRelative);Directory.CreateDirectory(Path.GetDirectoryName(runtimeCopy)!);File.Copy(runtime,runtimeCopy,false);if(DlssManager.Hash(runtimeCopy)!=runtimeHash)throw new IOException("Библиотека NVIDIA изменилась после проверки.");files.Add(runtimeRelative,runtimeCopy);
                File.WriteAllText(ini,Set(Configure(File.ReadAllText(ini),mode,preset),"Libraries","NvngxDlssPath",ModInstaller.SafePath(root,runtimeRelative)));files.Remove("OptiScaler.dll",out var dll);files.Add("dxgi.dll",dll!);
                using(var license=typeof(OptiScalerManager).Assembly.GetManifestResourceStream("LauncherPlaySpace.OptiScalerLicense")??throw new IOException("Отсутствует лицензия OptiScaler.")){
                    string licensePath=ModInstaller.SafePath(stage,"PlaySpace.OptiScaler/OptiScaler-LICENSE.txt");using(var output=File.Create(licensePath))license.CopyTo(output);files.Add("PlaySpace.OptiScaler/OptiScaler-LICENSE.txt",licensePath);
                }
                using(var license=typeof(OptiScalerManager).Assembly.GetManifestResourceStream("LauncherPlaySpace.NvidiaDlssLicense")??throw new IOException("Отсутствует лицензия NVIDIA.")){
                    string licensePath=ModInstaller.SafePath(stage,"PlaySpace.OptiScaler/NVIDIA-DLSS-LICENSE.txt");using(var output=File.Create(licensePath))license.CopyTo(output);files.Add("PlaySpace.OptiScaler/NVIDIA-DLSS-LICENSE.txt",licensePath);
                }
                string notice=ModInstaller.SafePath(stage,"PlaySpace.OptiScaler/SOURCES.txt");File.WriteAllText(notice,"OptiScaler 0.9.4, GPL-3.0. Source: "+ProjectUrl+"/tree/v0.9.4\r\nDownloaded separately; not part of PLAYSPACE's MIT license. Bundled runtime libraries retain their Licenses/*.txt and *.md terms.\r\nNVIDIA DLSS SR SDK: https://github.com/NVIDIA/DLSS/tree/v310.5.3\r\nNVIDIA license: https://github.com/NVIDIA/DLSS/blob/v310.5.3/LICENSE.txt\r\n");files.Add("PlaySpace.OptiScaler/SOURCES.txt",notice);
                ValidateGame(game,executable,consent,adapter,dx12);DlssManager.EnsureClosed(game);token.ThrowIfCancellationRequested();
                var package=new ModPackage{Owner="PlaySpace",Name="OptiScaler"};new ModInstaller(root).ApplyStaged(new(package,new(){Version=Version}),files,progress);
            },token);
        }finally{if(Directory.Exists(stage)){if(!Path.GetDirectoryName(stage)!.Equals(cache,StringComparison.OrdinalIgnoreCase))throw new IOException("Неверная папка подготовки.");ManagedPaths.NoLinks(stage);foreach(var path in Directory.EnumerateFileSystemEntries(stage,"*",SearchOption.AllDirectories))ManagedPaths.NoLinks(path);Directory.Delete(stage,true);}}
    }
    internal static bool Installed(string executable)=>File.Exists(executable)&&new ModInstaller(Path.GetDirectoryName(Path.GetFullPath(executable))!).ReadState().Packages.ContainsKey(PackageId);
    internal static void Remove(GameInfo game,string executable)
    {
        // Removal remains available after a GPU change or switching the game's graphics API.
        UniversalDlss5.ValidateGame(game,executable,true,checkAntiCheat:false);DlssManager.EnsureClosed(game);ManagedPaths.NoLinks(executable);
        new ModInstaller(Path.GetDirectoryName(Path.GetFullPath(executable))!).Uninstall(PackageId);
    }
}
