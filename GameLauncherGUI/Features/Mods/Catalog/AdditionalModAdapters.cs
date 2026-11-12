using Newtonsoft.Json;

namespace GameLauncherGUI;

internal static class AdditionalModAdapters
{
    internal static IReadOnlyList<ModGameAdapter> Load()
    {
        using var input=typeof(AdditionalModAdapters).Assembly.GetManifestResourceStream("LauncherPlaySpace.ModAdapters");
        if(input==null)return Array.Empty<ModGameAdapter>();using var reader=new StreamReader(input);
        var adapters=JsonConvert.DeserializeObject<List<ModGameAdapter>>(reader.ReadToEnd())??new();
        if(adapters.Any(a=>!uint.TryParse(a.SteamId,out var id)||id==0||string.IsNullOrWhiteSpace(a.Name)||!System.Text.RegularExpressions.Regex.IsMatch(a.Community,@"^[a-z0-9-]+$")||Path.GetFileName(a.Executable)!=a.Executable||!a.Executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(a.Loader,@"^[A-Za-z0-9_]+-[A-Za-z0-9_]+$")||string.IsNullOrWhiteSpace(a.DataFolder)||Path.GetFileName(a.DataFolder)!=a.DataFolder))throw new InvalidDataException("Некорректный встроенный каталог адаптеров.");
        return adapters;
    }
}
