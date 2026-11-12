using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record HardwareShare(string Name,double Share);
internal sealed record SteamHardwareSnapshot(string Month,string Captured,string Source,string HtmlSHA256,HardwareShare[] Gpus,HardwareShare[] Ram,HardwareShare[] Cores);
internal sealed record ObservedRig(string Gpu,string Cpu,int Results,string Source)
{
    internal string Id=>Gpu+" / "+Cpu;
}
internal static class HardwareEvidence
{
    private static readonly Lazy<SteamHardwareSnapshot> steam=new(()=>{
        using var stream=typeof(HardwareEvidence).Assembly.GetManifestResourceStream("LauncherPlaySpace.SteamHardware")??throw new IOException("Нет снимка Steam.");using var reader=new StreamReader(stream);var snapshot=JsonConvert.DeserializeObject<SteamHardwareSnapshot>(reader.ReadToEnd())??throw new InvalidDataException("Некорректный снимок Steam.");
        if(snapshot.Gpus.Any(g=>g.Share is <0 or >100)||snapshot.Gpus.Select(g=>g.Name).Distinct().Count()!=snapshot.Gpus.Length)throw new InvalidDataException("Некорректные доли GPU.");return snapshot;
    });
    internal static SteamHardwareSnapshot Steam=>steam.Value;
    internal static ObservedRig[] Rigs=>BenchmarkCatalog.All.Where(r=>r.Cpu is not("Not reported" or "Не указан")).GroupBy(r=>(r.Gpu,r.Cpu)).Select(g=>new ObservedRig(g.Key.Gpu,g.Key.Cpu,g.Count(),g.First().Source)).OrderByDescending(r=>SteamShare(r.Gpu)??-1).ThenBy(r=>r.Gpu).ThenBy(r=>r.Cpu).ToArray();
    internal static double? SteamShare(string gpu)=>Steam.Gpus.FirstOrDefault(s=>!s.Name.Contains("Laptop",StringComparison.OrdinalIgnoreCase)&&BenchmarkCatalog.GpuKey(s.Name)==BenchmarkCatalog.GpuKey(gpu))?.Share;
    internal static readonly (string Title,string Config,string Url)[] Videos={
        ("RTX 4060 + Ryzen 5 5600 · 20 игр","Ryzen 5 5600 / RTX 4060; RAM и условия уточняются в видео","https://www.youtube.com/watch?v=2HooXCfDXL8"),
        ("RTX 4060 + Ryzen 5 5600 · 18 игр","Ryzen 5 5600 / RTX 4060; FPS не оцифрован","https://www.youtube.com/watch?v=LVggiwI5Bps"),
        ("Hardware Unboxed · RTX 60 Class","Сравнение поколений; численные данные не дублируются","https://www.youtube.com/watch?v=lIftP0Ut2lU"),
        ("Gamers Nexus · RTX 4060","Обзор GPU; смотреть стенд и пресеты автора","https://www.youtube.com/watch?v=WS0sfOb_sVM"),
        ("Hardware Unboxed · RTX 3070 Laptop vs Desktop","Ноутбуки и настольные GPU не объединяются","https://www.youtube.com/watch?v=rCrCV71x8G0")};
}
