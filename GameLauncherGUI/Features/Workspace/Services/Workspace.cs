using Newtonsoft.Json;

namespace GameLauncherGUI;

internal static class LocalJson
{
    internal static T Read<T>(string file,Func<T> fallback){try{return File.Exists(file)?JsonConvert.DeserializeObject<T>(File.ReadAllText(file))??fallback():fallback();}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return fallback();}}
    internal static void Write(string file,object value){Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,JsonConvert.SerializeObject(value,Formatting.Indented));File.Move(temp,file,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
internal sealed record ActivityEntry(DateTimeOffset Time,string GameId,string GameName,string Kind,string Text);
internal sealed class ActivityHistory
{
    private readonly string file;private readonly object gate=new();
    internal ActivityHistory(string folder){file=Path.Combine(folder,"activity-history.json");}
    internal IReadOnlyList<ActivityEntry> Entries(){lock(gate)return LocalJson.Read(file,()=>new List<ActivityEntry>()).TakeLast(1000).Reverse().ToArray();}
    internal void Add(GameInfo game,string kind,string text)
    {
        try{lock(gate){var known=GameLibraryStore.Load(Path.Combine(Path.GetDirectoryName(file)!,"games.json")).Games.FirstOrDefault(g=>(!string.IsNullOrEmpty(game.SteamAppId)&&g.SteamAppId==game.SteamAppId)||g.ExePath.Equals(game.ExePath,StringComparison.OrdinalIgnoreCase));var entries=Entries().Reverse().ToList();entries.Add(new(DateTimeOffset.UtcNow,known?.LibraryId??game.LibraryId,game.Name,kind,text));LocalJson.Write(file,entries.TakeLast(1000));}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
    }
}
internal sealed class CollectionState
{
    public List<string> Names{get;set;}=new();
    public Dictionary<string,List<string>> Members{get;set;}=new();
}
internal sealed class LibraryCollections
{
    private readonly string file;internal CollectionState State{get;private set;}
    internal LibraryCollections(string folder){file=Path.Combine(folder,"collections.json");State=LocalJson.Read(file,()=>new CollectionState());}
    internal static string Name(string text){text=text.Trim();if(text.Length is <1 or >36||text.Any(char.IsControl))throw new ArgumentException("Название: от 1 до 36 символов без переводов строки.");return text;}
    private void Change(Action<CollectionState> action){var next=JsonConvert.DeserializeObject<CollectionState>(JsonConvert.SerializeObject(State))!;action(next);LocalJson.Write(file,next);State=next;}
    internal void Create(string name){name=Name(name);if(State.Names.Count>=24)throw new IOException("Можно создать до 24 коллекций.");if(State.Names.Contains(name,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("Такая коллекция уже существует.");Change(state=>state.Names.Add(name));}
    internal void Rename(string original,string name){name=Name(name);if(!State.Names.Contains(original)||State.Names.Any(n=>n!=original&&n.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Коллекция не найдена или новое имя занято.");Change(state=>{state.Names[state.Names.IndexOf(original)]=name;foreach(var list in state.Members.Values){int i=list.IndexOf(original);if(i>=0)list[i]=name;}});}
    internal void Remove(string name)=>Change(state=>{state.Names.Remove(name);foreach(var list in state.Members.Values)list.Remove(name);});
    internal void Set(string gameId,string name,bool included){if(!Guid.TryParseExact(gameId,"N",out _)||!State.Names.Contains(name))throw new ArgumentException("Игра или коллекция не найдена.");Change(state=>{if(!state.Members.TryGetValue(gameId,out var list))state.Members[gameId]=list=new();list.Remove(name);if(included)list.Add(name);});}
    internal bool Contains(GameInfo game,string name)=>State.Members.TryGetValue(game.LibraryId,out var names)&&names.Contains(name);
    internal void MergeMembership(string keep,IEnumerable<string> ids)=>Change(state=>{
        var names=ids.SelectMany(id=>state.Members.GetValueOrDefault(id)??new()).Distinct().ToList();
        state.Members[keep]=names; // Retain aliases until the library save succeeds; no membership is lost on failure.
    });
}
internal sealed class Workspace
{
    private static readonly Dictionary<string,Workspace> instances=new(StringComparer.OrdinalIgnoreCase);
    internal static string ActiveFolder{get;private set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncherPRO");
    internal static Workspace Current=>For(ActiveFolder);
    internal static Workspace Use(string folder){ActiveFolder=Path.GetFullPath(folder);PresentationOptions.Configure(LauncherPreferences.Load(folder));return For(folder);}
    internal static Workspace For(string folder){folder=Path.GetFullPath(folder);lock(instances){if(!instances.TryGetValue(folder,out var value))instances[folder]=value=new(folder);return value;}}
    internal readonly string Folder;internal readonly ActivityHistory History;internal readonly LibraryCollections Collections;internal readonly TransferCenter Transfers;
    private Workspace(string folder){Folder=folder;History=new(folder);Collections=new(folder);Transfers=new(Path.Combine(folder,"transfer-cache"));}
    internal bool Offline=>LauncherSession.SafeMode||LauncherPreferences.Load(Folder).OfflineMode;
}
