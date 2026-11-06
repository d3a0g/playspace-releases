using System.IO.Compression;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal sealed record NativeModAdapter(string SteamId,string Name,string Layout,string Folder,string? UserBase=null,string? RequiredFile=null)
{
    public static readonly NativeModAdapter[] All={
        new("413150","Stardew Valley / SMAPI","smapi","Mods",null,"StardewModdingAPI.exe"),
        new("294100","RimWorld","rimworld","Mods"),new("268500","XCOM 2","xcom","XComGame/Mods"),
        new("427520","Factorio","factorio","Factorio/mods","appdata"),
        new("1091500","Cyberpunk 2077 / архивные моды","archive","archive/pc/mod"),
        new("292030","The Witcher 3","witcher","Mods"),
        new("1086940","Baldur's Gate 3 / PAK","pak","Larian Studios/Baldur's Gate 3/Mods","local"),
        new("435150","Divinity: Original Sin 2 / PAK","pak","Larian Studios/Divinity Original Sin 2 Definitive Edition/Mods","documents"),
        new("489830","Skyrim Special Edition / Data","data","Data"),new("72850","Skyrim / Data","data","Data"),
        new("377160","Fallout 4 / Data","data","Data"),new("22380","Fallout: New Vegas / Data","data","Data"),
        new("22370","Fallout 3 GOTY / Data","data","Data"),new("22330","Oblivion / Data","data","Data")
    };
    public static NativeModAdapter? For(GameInfo game)=>All.FirstOrDefault(a=>a.SteamId==game.SteamAppId);
    public string Root(GameInfo game,string? userOverride=null)
    {
        string root=UserBase switch{"appdata"=>userOverride??Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"local"=>userOverride??Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"documents"=>userOverride??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),_=>Directory.Exists(game.InstallDirectory)?game.InstallDirectory!:File.Exists(game.ExePath)?Path.GetDirectoryName(game.ExePath)!:""};
        if(string.IsNullOrWhiteSpace(root))throw new IOException("Не найдена папка игры/пользователя.");
        if(RequiredFile!=null&&!File.Exists(Path.Combine(root,RequiredFile)))throw new IOException("Сначала установи загрузчик: "+RequiredFile);
        return ModInstaller.SafePath(root,Folder);
    }
    public string Hint=>Layout switch{"smapi"=>"Требуется установленный SMAPI. ZIP одного мода с manifest.json; запускай через StardewModdingAPI.exe.","rimworld"=>"ZIP одного мода с About/About.xml. Активируй мод и порядок в меню RimWorld.","factorio"=>"ZIP с info.json сохраняется как штатный архив Factorio. Зависимости/активация настраиваются в игре.","xcom"=>"ZIP с .XComMod. Активируй мод в игровом менеджере XCOM 2.","archive"=>"Только архивные .archive-моды. REDmod, RED4ext и скриптовые установщики не поддерживаются этим импортом.","pak"=>"Только .pak. Активация и зависимости — в менеджере модов игры. Не каждый PAK совместим с текущей версией.","data"=>"Импорт файлов Data без перезаписи оригиналов. Плагины активируются отдельно в менеджере игры. SKSE/F4SE и DLL-загрузчики не устанавливаются.",_=>"ZIP модов в папку Mods. Порядок, зависимости и совместимость проверяются в игре."};
}
internal static class NativeModImport
{
    public static void Install(NativeModAdapter adapter,GameInfo game,string zipFile,string root,IProgress<string> progress,CancellationToken token)
    {
        string stage=Path.Combine(Path.GetTempPath(),"PlaySpaceNative-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);var created=new List<string>();
        try {
            using var zip=ZipFile.OpenRead(zipFile);var files=zip.Entries.Where(e=>e.Name.Length>0).ToArray();if(files.Length==0||files.Length>30000)throw new InvalidDataException("Архив пустой или слишком большой.");
            long total=0;foreach(var entry in files){_=ModInstaller.SafePath(stage,entry.FullName.Replace('\\','/'));if(((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Символические ссылки не поддерживаются.");if(entry.Length>512L*1024*1024||(total+=entry.Length)>4L*1024*1024*1024)throw new InvalidDataException("Архив слишком большой.");if(new[]{".exe",".bat",".cmd",".ps1",".vbs",".lnk"}.Contains(Path.GetExtension(entry.Name),StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("ZIP содержит исполняемый установщик.");}
            string unique=Guid.NewGuid().ToString("N"),prefix="",nativeFolder="",name=Path.GetFileNameWithoutExtension(zipFile);var sources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if(adapter.Layout=="factorio") {
                var info=files.SingleOrDefault(e=>e.FullName.EndsWith("/info.json",StringComparison.OrdinalIgnoreCase)||e.FullName=="info.json")??throw new InvalidDataException("Нужен info.json Factorio.");
                using var reader=new StreamReader(info.Open());if(info.Length>1024*1024)throw new InvalidDataException("info.json слишком большой.");var metadata=JObject.Parse(reader.ReadToEnd());string id=metadata.Value<string>("name")??"",version=metadata.Value<string>("version")??"";
                if(!Regex.IsMatch(id,@"^[A-Za-z0-9_-]+$")||!Regex.IsMatch(version,@"^\d+\.\d+\.\d+$"))throw new InvalidDataException("Неверные name/version Factorio.");if(!info.FullName.Equals(id+"_"+version+"/info.json",StringComparison.Ordinal))throw new InvalidDataException("Factorio ZIP должен содержать папку name_version с info.json.");name=metadata.Value<string>("title")??id;sources[id+"_"+version+".zip"]=zipFile;
            } else {
                if(adapter.Layout is "smapi" or "rimworld" or "xcom") {
                    string marker=adapter.Layout=="smapi"?"manifest.json":adapter.Layout=="rimworld"?"About/About.xml":".XComMod";
                    var matches=files.Where(e=>e.FullName.EndsWith(marker,StringComparison.OrdinalIgnoreCase)).ToArray();if(matches.Length!=1)throw new InvalidDataException("Ожидается один мод с "+marker);
                    prefix=adapter.Layout=="xcom"?matches[0].FullName[..(matches[0].FullName.LastIndexOf('/')+1)]:matches[0].FullName[..^marker.Length];
                    if(adapter.Layout=="xcom"){nativeFolder=Path.GetFileNameWithoutExtension(matches[0].Name);if(!Regex.IsMatch(nativeFolder,@"^[A-Za-z0-9_-]+$"))throw new InvalidDataException("Неверное имя XCOM-мода.");}
                    if(adapter.Layout=="smapi"){if(matches[0].Length>1024*1024)throw new InvalidDataException("manifest.json слишком большой.");using var reader=new StreamReader(matches[0].Open());var metadata=JObject.Parse(reader.ReadToEnd());if(string.IsNullOrEmpty(metadata.Value<string>("UniqueID")))throw new InvalidDataException("Нет SMAPI UniqueID.");name=metadata.Value<string>("Name")??name;}
                }
                foreach(var entry in files){token.ThrowIfCancellationRequested();string path=entry.FullName.Replace('\\','/');if(prefix.Length>0&&!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;if(prefix.Length>0)path=path[prefix.Length..];if(string.IsNullOrEmpty(path))continue;
                    if(adapter.Layout=="archive"){if(!path.EndsWith(".archive",StringComparison.OrdinalIgnoreCase))continue;path=Path.GetFileName(path);}
                    else if(adapter.Layout=="pak"){if(!path.EndsWith(".pak",StringComparison.OrdinalIgnoreCase))continue;path=Path.GetFileName(path);}
                    else if(adapter.Layout=="data"){if(path.StartsWith("Data/",StringComparison.OrdinalIgnoreCase))path=path[5..];if(!new[]{".esp",".esm",".esl",".bsa",".ba2",".dds",".nif",".hkx",".pex",".psc",".json",".ini",".txt",".wav",".fuz",".lip",".xml"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Неподдерживаемый файл Data: "+path);}
                    else if(adapter.Layout=="witcher"){var parts=path.Split('/');int start=Array.FindIndex(parts,part=>part.StartsWith("mod",StringComparison.OrdinalIgnoreCase));if(start<0)continue;path=string.Join("/",parts.Skip(start));if(!path.Contains("/content/",StringComparison.OrdinalIgnoreCase))continue;}
                    else path=(nativeFolder.Length>0?nativeFolder:"PlaySpace_"+unique)+"/"+path;
                    string staged=ModInstaller.SafePath(stage,path);Directory.CreateDirectory(Path.GetDirectoryName(staged)!);if(sources.ContainsKey(path))throw new InvalidDataException("Повторяющийся путь в архиве.");
                    using(var input=entry.Open())using(var output=new FileStream(staged,FileMode.CreateNew)){created.Add(staged);byte[] block=new byte[81920];int read;long actual=0;while((read=input.Read(block))>0){token.ThrowIfCancellationRequested();actual+=read;if(actual>entry.Length)throw new InvalidDataException("Неверная длина ZIP.");output.Write(block,0,read);}}
                    sources.Add(path,staged);
                }
            }
            if(sources.Count==0)throw new InvalidDataException("Нет поддерживаемых файлов мода.");token.ThrowIfCancellationRequested();DlssManager.EnsureClosed(game);
            var versionInfo=new ModVersion{Version="local"};var package=new ModPackage{Owner="Local",Name=Regex.Replace(name,@"[^\p{L}\p{N}_-]","_")+"_"+unique[..8],Versions=new(){versionInfo}};new ModInstaller(root).ApplyStaged(new ModSelection(package,versionInfo),sources,progress);
        } finally {
            foreach(string file in created)if(File.Exists(file))File.Delete(file);
            foreach(string dir in Directory.EnumerateDirectories(stage,"*",SearchOption.AllDirectories).OrderByDescending(d=>d.Length))if(!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
            if(!Directory.EnumerateFileSystemEntries(stage).Any())Directory.Delete(stage);
        }
    }
}
internal sealed class NativeModsPanel : UserControl
{
    private readonly Label status=new(){Dock=DockStyle.Fill};private readonly ListBox installed=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White,BorderStyle=BorderStyle.None};private readonly CancellationTokenSource cancellation=new();private bool busy;private bool disposed;public event Action<bool>? BusyChanged;
    public NativeModsPanel(NativeModAdapter adapter,GameInfo game)
    {
        Dock=DockStyle.Fill;var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,110));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65));Controls.Add(layout);
        layout.Controls.Add(new Label{Text=adapter.Name+"\n\n"+adapter.Hint,Dock=DockStyle.Fill},0,0);var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};var import=new ModernButton("＋ Импорт ZIP",Color.FromArgb(115,94,244)){Width=190,Height=40};var remove=new ModernButton("Удалить выбранный",Color.FromArgb(35,40,63)){Width=230,Height=40};actions.Controls.Add(import);actions.Controls.Add(remove);layout.Controls.Add(actions,0,1);layout.Controls.Add(installed,0,2);layout.Controls.Add(status,0,3);
        void Reload(){installed.Items.Clear();try{foreach(var package in new ModInstaller(adapter.Root(game)).ReadState().Packages.Values.Where(p=>p.Id.StartsWith("Local-")))installed.Items.Add(package.Id);status.Text="Папка модов: "+adapter.Root(game);}catch(Exception ex){status.Text=ex.Message;}}
        async Task Run(Action<string,IProgress<string>> action){if(busy)return;busy=true;BusyChanged?.Invoke(true);import.Enabled=remove.Enabled=false;try{string root=adapter.Root(game);DlssManager.EnsureClosed(game);var progress=new Progress<string>(text=>{if(!IsDisposed)status.Text=text;});await Task.Run(()=>action(root,progress));if(!IsDisposed)Reload();}catch(Exception ex){if(!IsDisposed)status.Text=ex.Message;}finally{busy=false;BusyChanged?.Invoke(false);if(!IsDisposed)import.Enabled=remove.Enabled=true;}}
        import.Click+=async(s,e)=>{using var picker=new OpenFileDialog{Filter="ZIP моды|*.zip",Title=adapter.Name};if(picker.ShowDialog(this)==DialogResult.OK)await Run((root,progress)=>NativeModImport.Install(adapter,game,picker.FileName,root,progress,cancellation.Token));};
        remove.Click+=async(s,e)=>{if(installed.SelectedItem is string id&&MessageBox.Show(this,"Удалить только файлы выбранного мода LauncherPlaySpace?","Удаление",MessageBoxButtons.OKCancel)==DialogResult.OK)await Run((root,p)=>new ModInstaller(root).Uninstall(id));};Load+=(s,e)=>Reload();
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
