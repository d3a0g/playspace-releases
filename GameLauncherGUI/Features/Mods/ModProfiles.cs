using System.Security.Cryptography;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record ManagedProfile(string Id,string Name,string RootHash,ModState State)
{
    public override string ToString()=>Name+" · "+State.Packages.Count+" пакетов";
}
internal sealed class ModProfileStore
{
    private sealed record Change(string Path,string? Before,string? After);
    private sealed record SwitchJournal(string Id,ModState Before,List<Change> Changes);
    private readonly string root,area;private readonly Action ensureClosed;
    private string RootHash=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
    private string StateFile=>ModInstaller.SafePath(root,".playspace-mods/installed.json");
    private string Pending=>ModInstaller.SafePath(root,".playspace-mods/profile-switch.json");
    private static string Fingerprint(ModState state)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",state.Files.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>p.Key.ToLowerInvariant()+"="+p.Value.Hash.ToLowerInvariant()))+"\n"+string.Join("\n",state.Packages.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>p.Key+"="+p.Value.Version)))));
    internal string ActiveName()
    {
        try{string file=ModInstaller.SafePath(root,".playspace-mods/active-named-profile.json");var active=LocalJson.Read(file,()=>new Dictionary<string,string>());var state=Installed();if(state.Packages.Count==0)return "Без управляемых модов";return active.GetValueOrDefault("hash")==Fingerprint(state)?active.GetValueOrDefault("name")??"Текущий набор":"Текущий набор изменён";}catch(Exception ex)when(ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){return "Набор не определён";}
    }
    private void MarkActive(string name,ModState state)=>LocalJson.Write(ModInstaller.SafePath(root,".playspace-mods/active-named-profile.json"),new Dictionary<string,string>{{"name",name},{"hash",Fingerprint(state)}});
    internal ModProfileStore(string root,Action ensureClosed){this.root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);area=ModInstaller.SafePath(this.root,".playspace-mods/profiles");this.ensureClosed=ensureClosed;}
    private static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
    private string Point(string id){if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Неверный профиль модов.");return ModInstaller.SafePath(root,".playspace-mods/profiles/"+id);}
    private FileStream Lock(){Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);return new FileStream(ModInstaller.SafePath(root,".playspace-mods/manager.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    private ModState Installed()=>File.Exists(StateFile)?JsonConvert.DeserializeObject<ModState>(File.ReadAllText(StateFile))??throw new InvalidDataException("Повреждён список модов."):new();
    internal string Preview(string? id)
    {
        var before = Installed(); var after = id == null ? new ModState() : Read(id).State;
        return "Текущий набор: " + ActiveName() + "\n\nБудут включены: " +
            string.Join(", ", after.Packages.Keys.Except(before.Packages.Keys).DefaultIfEmpty("нет")) +
            "\nБудут отключены: " + string.Join(", ", before.Packages.Keys.Except(after.Packages.Keys).DefaultIfEmpty("нет")) +
            "\nСмена версии: " + string.Join(", ", after.Packages.Where(p => before.Packages.TryGetValue(p.Key, out var previous) && previous.Version != p.Value.Version).Select(p => p.Key + " → " + p.Value.Version).DefaultIfEmpty("нет")) +
            "\n\nПереключаются только управляемые файлы PLAYSPACE. Посторонние моды не отключаются. Игра должна быть закрыта.";
    }
    internal IReadOnlyList<ManagedProfile> List()
    {
        if(!Directory.Exists(area))return Array.Empty<ManagedProfile>();var result=new List<ManagedProfile>();
        foreach(string directory in Directory.EnumerateDirectories(area)){string id=Path.GetFileName(directory);if(!Guid.TryParseExact(id,"N",out _))continue;try{var profile=Read(id);result.Add(profile);}catch(Exception ex)when(ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){}}
        return result.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    private ManagedProfile Read(string id)
    {
        string file=ModInstaller.SafePath(Point(id),"profile.json");if(new FileInfo(file).Length>8*1024*1024)throw new InvalidDataException("Профиль слишком велик.");var profile=JsonConvert.DeserializeObject<ManagedProfile>(File.ReadAllText(file))??throw new InvalidDataException("Профиль повреждён.");
        if(profile.Id!=id||profile.RootHash!=RootHash||profile.State==null||profile.State.Packages==null||profile.State.Files==null||profile.State.Files.Count>10000)throw new InvalidDataException("Профиль не принадлежит этой папке игры.");Validate(profile.State);return profile;
    }
    private void Validate(ModState state)
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in state.Files){if(file.Key.Replace('\\','/').StartsWith(".playspace-mods/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Профиль содержит служебный путь.");string path=ModInstaller.SafePath(root,file.Key);if(!paths.Add(path)||file.Value.Hash.Length!=64||!file.Value.Hash.All(Uri.IsHexDigit))throw new InvalidDataException("Повреждены пути или хеши модов.");}
        foreach(var package in state.Packages.Values){
            if(package==null||package.Files==null||package.Dependencies==null||package.Files.Any(p=>!state.Files.ContainsKey(p)))throw new InvalidDataException("В профиле отсутствуют файлы пакета.");
            foreach(string dependency in package.Dependencies){int split=dependency.LastIndexOf('-');if(split<=0||!state.Packages.TryGetValue(dependency[..split],out var found)||found.Version!=dependency[(split+1)..])throw new IOException("Не хватает зависимости профиля: "+dependency);}
        }
    }
    internal ManagedProfile Save(string name)
    {
        name=LibraryCollections.Name(name);ensureClosed();_ = new ModInstaller(root).ReadState();using var guard=Lock();var state=Installed();Validate(state);
        if(List().Count>=20||List().Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new IOException("Имя занято или достигнут лимит 20 профилей.");
        string id=Guid.NewGuid().ToString("N"),point=Point(id);long size=0;Directory.CreateDirectory(point);
        try{
            foreach(var entry in state.Files){string source=ModInstaller.SafePath(root,entry.Key);if(!File.Exists(source)||!Hash(source).Equals(entry.Value.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Файлы модов изменены вне лаунчера. Профиль не сохранён.");if(!entry.Value.Created)continue;size+=new FileInfo(source).Length;if(size>512L*1024*1024)throw new IOException("Профиль превышает 512 МБ.");string to=ModInstaller.SafePath(point,"files/"+entry.Key);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(source,to);if(!Hash(to).Equals(entry.Value.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл мода изменился во время копирования. Набор не сохранён.");}
            var profile=new ManagedProfile(id,name,RootHash,state);LocalJson.Write(Path.Combine(point,"profile.json"),profile);MarkActive(name,state);return profile;
        }catch{Cleanup(root,".playspace-mods/profiles/"+id);throw;}
    }
    internal void Activate(string? id,Action<int>? afterWrite=null)
    {
        ensureClosed();_ = new ModInstaller(root).ReadState();using var guard=Lock();var before=Installed();Validate(before);var target=id==null?new ModState():Read(id).State;
        long size=0;foreach(var entry in before.Files){string path=ModInstaller.SafePath(root,entry.Key);if(!File.Exists(path)||!Hash(path).Equals(entry.Value.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Текущие файлы изменены. Переключение отменено.");}
        foreach(var entry in target.Files){
            string destination=ModInstaller.SafePath(root,entry.Key);
            if(!entry.Value.Created){if(!File.Exists(destination)||!Hash(destination).Equals(entry.Value.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Отсутствует внешняя общая библиотека профиля.");continue;}
            string payload=ModInstaller.SafePath(Point(id!),"files/"+entry.Key);if(!File.Exists(payload)||!Hash(payload).Equals(entry.Value.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Кеш профиля повреждён. Переключение отменено.");size+=new FileInfo(payload).Length;if(size>512L*1024*1024)throw new InvalidDataException("Профиль слишком велик.");
            if(File.Exists(destination)&&(!before.Files.TryGetValue(entry.Key,out var owned)||!owned.Created))throw new IOException("Профиль конфликтует с посторонним файлом: "+entry.Key);
        }
        var paths=before.Files.Where(p=>p.Value.Created&&!target.Files.ContainsKey(p.Key)).Select(p=>p.Key).Concat(target.Files.Where(p=>p.Value.Created).Select(p=>p.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string transaction=Guid.NewGuid().ToString("N");string stage=ModInstaller.SafePath(root,".playspace-mods/profile-transactions/"+transaction);Directory.CreateDirectory(stage);var changes=new List<Change>();
        foreach(string relative in paths){string path=ModInstaller.SafePath(root,relative);string? original=File.Exists(path)?Hash(path):null;if(original!=null){string backup=ModInstaller.SafePath(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(path,backup);if(!Hash(backup).Equals(original,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл мода изменился при подготовке. Переключение не началось.");}changes.Add(new(relative,original,target.Files.TryGetValue(relative,out var value)?value.Hash:null));}
        LocalJson.Write(Pending,new SwitchJournal(transaction,before,changes));
        try{
            int written=0;foreach(var change in changes){string path=ModInstaller.SafePath(root,change.Path);if(change.After==null){if(File.Exists(path))File.Delete(path);}else{Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+".profile-"+Guid.NewGuid().ToString("N")+".tmp";try{File.Copy(ModInstaller.SafePath(Point(id!),"files/"+change.Path),temp);if(!Hash(temp).Equals(change.After,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл профиля изменился.");File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}afterWrite?.Invoke(++written);}
            target.Transaction=transaction;LocalJson.Write(StateFile,target);File.Delete(Pending);Cleanup(root,".playspace-mods/profile-transactions/"+transaction);try{MarkActive(id==null?"Без управляемых модов":Read(id).Name,target);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
        }catch{Recover(root);throw;}
    }
    internal static void Recover(string root)
    {
        string pending=ModInstaller.SafePath(root,".playspace-mods/profile-switch.json");if(!File.Exists(pending))return;
        if(new FileInfo(pending).Length>8*1024*1024)throw new InvalidDataException("Слишком большой журнал переключения профиля.");var journal=JsonConvert.DeserializeObject<SwitchJournal>(File.ReadAllText(pending))??throw new InvalidDataException("Повреждён журнал переключения профиля.");if(!Guid.TryParseExact(journal.Id,"N",out _)||journal.Before==null||journal.Changes==null||journal.Changes.Count>10000)throw new InvalidDataException("Повреждён журнал переключения профиля.");
        string stateFile=ModInstaller.SafePath(root,".playspace-mods/installed.json");var state=File.Exists(stateFile)?JsonConvert.DeserializeObject<ModState>(File.ReadAllText(stateFile)):null;
        if(state?.Transaction==journal.Id){File.Delete(pending);Cleanup(root,".playspace-mods/profile-transactions/"+journal.Id);return;}
        foreach(var change in journal.Changes.AsEnumerable().Reverse()){
            string path=ModInstaller.SafePath(root,change.Path);string? hash=File.Exists(path)?Hash(path):null;if(hash==change.Before)continue;
            if(hash!=null&&!hash.Equals(change.After,StringComparison.OrdinalIgnoreCase))throw new IOException("Файлы изменены сторонней программой. Автоматический откат профиля остановлен; страховочные файлы находятся в .playspace-mods/profile-transactions.");
            if(change.Before==null){if(File.Exists(path))File.Delete(path);}else{string backup=ModInstaller.SafePath(root,".playspace-mods/profile-transactions/"+journal.Id+"/"+change.Path);if(!File.Exists(backup)||!Hash(backup).Equals(change.Before,StringComparison.OrdinalIgnoreCase))throw new IOException("Страховочная копия профиля повреждена.");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(backup,path,true);}
        }
        LocalJson.Write(stateFile,journal.Before);File.Delete(pending);Cleanup(root,".playspace-mods/profile-transactions/"+journal.Id);
    }
    private static void Cleanup(string root,string relative)
    {
        try{string directory=ModInstaller.SafePath(root,relative);if(!Directory.Exists(directory))return;foreach(string file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories)){string safe=ModInstaller.SafePath(directory,Path.GetRelativePath(directory,file));File.Delete(safe);}foreach(string child in Directory.EnumerateDirectories(directory,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length))Directory.Delete(ModInstaller.SafePath(directory,Path.GetRelativePath(directory,child)));Directory.Delete(directory);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
    }
}
internal sealed class ModProfilesForm : StudioDialog
{
    private readonly ModernSelect profiles=new(){Dock=DockStyle.Fill};private readonly TextBox name=new(){Dock=DockStyle.Fill,PlaceholderText="Например: Кооператив"};private readonly FormattedDescription detail=new(){Dock=DockStyle.Fill};private readonly SmoothLabel status=new(){Dock=DockStyle.Fill};private bool busy;
    internal ModProfilesForm(GameInfo game,string root)
    {
        var store=new ModProfileStore(root,()=>DlssManager.EnsureClosed(game));Text="PLAYSPACE / Профили модов · "+game.Name;Size=new Size(860,620);MinimumSize=new Size(760,560);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(28)};foreach(float h in new[]{44F,44F})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));ContentHost.Controls.Add(layout);layout.Controls.Add(profiles,0,0);layout.Controls.Add(name,0,1);layout.Controls.Add(detail,0,2);layout.Controls.Add(status,0,4);
        var buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};for(int i=0;i<3;i++)buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));var save=new ModernButton("Сохранить текущий набор",VisualTheme.Accent){Dock=DockStyle.Fill};var apply=new ModernButton("Активировать набор",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};var empty=new ModernButton("Без управляемых модов",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};buttons.Controls.Add(save,0,0);buttons.Controls.Add(apply,1,0);buttons.Controls.Add(empty,2,0);layout.Controls.Add(buttons,0,3);
        void Refresh(){profiles.SelectedIndex=-1;profiles.Items.Clear();foreach(var p in store.List())profiles.Items.Add(p);profiles.SetPlaceholder("Сохранённых наборов пока нет");if(profiles.Items.Count>0)profiles.SelectedIndex=0;}
        profiles.SelectedIndexChanged+=(s,e)=>detail.Text=profiles.SelectedItem is ManagedProfile p?string.Join("\n",p.State.Packages.Values.Select(m=>m.Id+" · "+m.Version))+"\n\nЗависимости и SHA-256 проверяются при активации. Переключаются только файлы, установленные PlaySpace. Посторонние моды и общие библиотеки не отключаются.":"Установите нужные моды, закройте игру и сохраните текущий набор с названием. Затем можно переключать сохранённые наборы без повторного скачивания. Для больших модпаков используйте специализированный менеджер.";
        async Task Run(Action work,string history){if(busy)return;busy=true;buttons.Enabled=profiles.Enabled=name.Enabled=false;try{await Task.Run(work);if(IsDisposed)return;Workspace.Current.History.Add(game,"Моды",history);Refresh();status.Text="Готово. Набор изменён при закрытой игре.";}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException){if(!IsDisposed)status.Text=ex.Message;}finally{busy=false;if(!IsDisposed)buttons.Enabled=profiles.Enabled=name.Enabled=true;}}
        save.Click+=async(s,e)=>{string title=name.Text;await Run(()=>store.Save(title),"Сохранён набор управляемых модов");};apply.Click+=async(s,e)=>{if(profiles.SelectedItem is ManagedProfile p && MessageBox.Show(this,store.Preview(p.Id),"Переключение набора",MessageBoxButtons.OKCancel)==DialogResult.OK)await Run(()=>store.Activate(p.Id),"Активирован набор: "+p.Name);};empty.Click+=async(s,e)=>{if(MessageBox.Show(this,store.Preview(null),"Оригинальная игра · управляемые моды",MessageBoxButtons.OKCancel)==DialogResult.OK)await Run(()=>store.Activate(null),"Отключены управляемые моды");};
        Shown+=(s,e)=>{try{_ = new ModInstaller(root).ReadState();Refresh();if(profiles.Items.Count==0)detail.Text="Сохраните текущий установленный набор, чтобы переключать его позже. «Без управляемых модов» отключает только файлы PlaySpace.";}catch(Exception ex)when(ex is IOException or InvalidDataException or JsonException){status.Text=ex.Message;}};FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
    }
}
