namespace GameLauncherGUI;

internal sealed record BrowsePosition(string? SelectedId,int Offset);
internal sealed class BrowseState
{
    private readonly Dictionary<string,BrowsePosition> positions=new();
    private readonly LinkedList<string> recent=new();
    internal void Save(string key,string? id,int offset)
    {
        recent.Remove(key);recent.AddLast(key);positions[key]=new(id,Math.Max(0,offset));
        if(positions.Count>64&&recent.First is { } oldest){positions.Remove(oldest.Value);recent.RemoveFirst();}
    }
    internal BrowsePosition? Read(string key)=>positions.GetValueOrDefault(key);
}
