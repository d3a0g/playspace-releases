using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using GameLauncherGUI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class HardwareEvidenceImport
{
    private static string Plain(string value)=>Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value,"<[^>]+>"," ")),@"\s+"," ").Trim();
    internal static void Run()
    {
        string input=Path.GetFullPath("artifacts/hardware-catalog");var pages=JArray.Parse(File.ReadAllText(Path.Combine(input,"pages.json")));
        pages.Add(JObject.FromObject(new{Key="3060",Gpu="RTX 3060",Url="https://www.notebookcheck.net/NVIDIA-GeForce-RTX-3060-Desktop-GPU-Benchmarks-and-Specs.579000.0.html"}));
        var allowed=new Dictionary<string,(string Id,string Name)>{{"Cyberpunk 2077",("1091500","Cyberpunk 2077")},{"Counter-Strike 2",("730","Counter-Strike 2")},{"Red Dead Redemption 2",("1174180","Red Dead Redemption 2")},{"Forza Horizon 5",("1551360","Forza Horizon 5")},{"Baldur's Gate 3",("1086940","Baldur's Gate 3")},{"Hogwarts Legacy",("990080","Hogwarts Legacy")},{"Ready or Not",("1144200","Ready or Not")},{"Palworld",("1623730","Palworld")},{"Shadow of the Tomb Raider",("750920","Shadow of the Tomb Raider")},{"The Witcher 3 v4",("292030","The Witcher 3")}};
        var result=new List<BenchmarkObservation>();var evidence=new List<object>();
        foreach(var page in pages.OfType<JObject>()){
            string key=page.Value<string>("Key")!,gpu=page.Value<string>("Gpu")!,url=page.Value<string>("Url")!,file=Path.Combine(input,"nbc-"+key+".html");if(!File.Exists(file))continue;string html=File.ReadAllText(file);
            var headers=Regex.Matches(html,"<h3 class=\"gpugame_header\">(.*?)</h3>",RegexOptions.Singleline);int added=0;
            for(int h=0;h<headers.Count;h++){
                string title=Plain(headers[h].Groups[1].Value);var match=allowed.FirstOrDefault(p=>title==p.Key||p.Key=="Cyberpunk 2077"&&title.StartsWith("Cyberpunk 2077 ",StringComparison.Ordinal));if(match.Value.Id==null)continue;
                int end=h+1<headers.Count?headers[h+1].Index:Math.Min(html.Length,headers[h].Index+200000);string section=html[headers[h].Index..end];var entries=Regex.Matches(section,"<a[^>]+class=\"gpugame_resulta\"[^>]*>([0-9.]+)</a>\\s*<div id=\"(gamebench_[^\"]+)\" class=\"gpugame_benchdiv\"[^>]*>(.*?)</div>",RegexOptions.Singleline);
                var chosen=new List<BenchmarkObservation>();
                foreach(Match entry in entries){
                    int before=section.LastIndexOf("<div class=\"gpugame_details\"",entry.Index,StringComparison.Ordinal);if(before<0)continue;string detail=section[before..entry.Index];var preset=Regex.Match(detail,"<abbr[^>]*title=\"([^\"]+)\"[^>]*>.*?([0-9]{3,5})x([0-9]{3,5})",RegexOptions.Singleline);if(!preset.Success)continue;
                    string description=WebUtility.HtmlDecode(preset.Groups[1].Value);int width=int.Parse(preset.Groups[2].Value),height=int.Parse(preset.Groups[3].Value);if(width is not(1920 or 2560)||height is not(1080 or 1440))continue;
                    string metadata=entry.Groups[3].Value;var lines=Regex.Split(metadata,"<br\\s*/?>",RegexOptions.IgnoreCase).Select(Plain).Where(s=>s.Length>0).ToArray();string cpu=lines.FirstOrDefault(s=>Regex.IsMatch(s,@"^(AMD (Ryzen|FX)|Intel (Core|Xeon|Pentium|Celeron)|Apple M)"))??"Не указан";
                    string? review=Regex.Matches(metadata,"href=\"(https://www.notebookcheck\\.(?:net|com)/[^\"]+)\"").Cast<Match>().Select(m=>WebUtility.HtmlDecode(m.Groups[1].Value)).FirstOrDefault();
                    string version=title=="Cyberpunk 2077 1.6"?"1.6":title=="Cyberpunk 2077 1.0"?"1.0":title.EndsWith(" v4")?"4.x (точный патч не указан)":"Патч не указан";
                    var explicitVersion=lines.FirstOrDefault(s=>Regex.IsMatch(s,@"^\d+\.\d+(?:\.\d+)?$"));if(explicitVersion!=null)version=explicitVersion;
                    string trace=description.Contains("RT",StringComparison.OrdinalIgnoreCase)||description.Contains("Ray",StringComparison.OrdinalIgnoreCase)?"указано в пресете":"не указано";
                    chosen.Add(new(match.Value.Id,match.Value.Name,gpu,cpu,version,description,width,height,double.Parse(entry.Groups[1].Value,CultureInfo.InvariantCulture),trace,Regex.IsMatch(description,@"\b(DLSS|FSR|XeSS|Upscaling)\s*off\b",RegexOptions.IgnoreCase)?"off (см. пресет)":"не указано","не указано","NBC:"+title+":"+description,"Notebookcheck",review??url,entry.Groups[2].Value));
                }
                // Keep individual observations, not the rounded overview average; balance presets and resolutions.
                foreach(var row in chosen.GroupBy(r=>(r.Preset,r.Width,r.Height)).Take(3).SelectMany(g=>g.Take(2))){result.Add(row);added++;}
            }
            evidence.Add(new{Gpu=gpu,Page=url,SHA256=DlssManager.Hash(file),Imported=added});
        }
        var original=JsonConvert.DeserializeObject<BenchmarkObservation[]>(File.ReadAllText("GameLauncherGUI/Assets/benchmarks.json"))!;result.AddRange(original.Where(r=>r.Publisher!="Notebookcheck"));
        result=result.DistinctBy(r=>(r.Publisher,r.Observation)).ToList();if(result.Count<150||result.Select(r=>r.Gpu).Distinct().Count()<8)throw new Exception("Insufficient verified coverage; source parser may need updating.");
        File.WriteAllText("GameLauncherGUI/Assets/benchmarks-expanded.json",JsonConvert.SerializeObject(result,Formatting.Indented));File.WriteAllText(Path.Combine(input,"provenance.json"),JsonConvert.SerializeObject(evidence,Formatting.Indented));
        Console.WriteLine($"Imported {result.Count} individual FPS observations, {result.Select(r=>r.Gpu).Distinct().Count()} GPUs, {result.Select(r=>r.GameId).Distinct().Count()} games, {result.Select(r=>(r.Gpu,r.Cpu)).Distinct().Count()} GPU/CPU rigs. RAM not inferred.");
    }
}
