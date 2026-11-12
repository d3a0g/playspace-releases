using GameLauncherGUI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ModExpansionVerification
{
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        string folder=Path.Combine(Directory.GetCurrentDirectory(),"artifacts/mod-expansion");Directory.CreateDirectory(folder);
        var ecosystem=JObject.Parse(File.ReadAllText(Path.Combine(folder,"ecosystem.json")));
        var knownLoaders=ecosystem["modloaderPackages"]!.Where(p=>p.Value<string>("loader")=="bepinex").Select(p=>p.Value<string>("packageId")!).ToHashSet();
        var ids=RecommendedGame.All.Select(g=>g.Id).ToHashSet();var additional=AdditionalModAdapters.Load().Select(a=>a.SteamId).ToHashSet();var current=ModGameAdapter.All.Where(a=>!additional.Contains(a.SteamId)).Select(a=>a.SteamId).ToHashSet();
        var candidates=new List<ModGameAdapter>();
        foreach(var property in ((JObject)ecosystem["games"]!).Properties())foreach(var config in property.Value["r2modman"]??new JArray()){
            var steam=config["distributions"]?.FirstOrDefault(p=>p.Value<string>("platform")=="steam")?.Value<string>("identifier");
            string? exe=config["exeNames"]?.Values<string>().FirstOrDefault(e=>e?.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)==true);
            if(steam==null||!ids.Contains(steam)||current.Contains(steam)||config.Value<string>("packageLoader")!="bepinex"||exe==null)continue;
            candidates.Add(new(steam,config["meta"]!.Value<string>("displayName")!,property.Name,exe,"",null,config.Value<string>("dataFolderName")));
        }
        Workspace.Use(Path.Combine(folder,"verification-cache"));
        using var concurrency=new SemaphoreSlim(3);var verified=new System.Collections.Concurrent.ConcurrentBag<ModGameAdapter>();var evidence=new System.Collections.Concurrent.ConcurrentBag<object>();
        async Task Verify(ModGameAdapter candidate){await concurrency.WaitAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(110));try{
            using var service=new ModCatalog();var catalog=await service.LoadAsync(candidate,timeout.Token);
            var loaders=catalog.Where(p=>knownLoaders.Contains(p.Id)&&p.Latest!=null&&!p.Deprecated).OrderByDescending(p=>catalog.Count(m=>m.Latest?.Dependencies.Any(d=>d.StartsWith(p.Id+"-",StringComparison.Ordinal))==true)).ToArray();
            string? error=null;
            foreach(var loader in loaders){try{
                var adapter=candidate with{Loader=loader.Id};byte[] bytes=await service.DownloadAsync(loader.Latest!.DownloadUrl,timeout.Token,"Проверка загрузчика "+candidate.Name);
                var loaderFiles=ModInstaller.MapArchive(new(loader,loader.Latest),bytes,adapter);
                if(!loaderFiles.Keys.Any(k=>k.StartsWith("BepInEx/core/",StringComparison.OrdinalIgnoreCase)))throw new IOException("Loader has no core files");
                string? sample=null;
                foreach(var mod in catalog.Where(p=>p.Latest!=null&&!p.Deprecated&&!p.Nsfw&&!knownLoaders.Contains(p.Id)&&!p.Id.Contains("GaleModManager",StringComparison.OrdinalIgnoreCase)&&!p.Categories.Contains("Modpacks",StringComparer.OrdinalIgnoreCase)).OrderByDescending(p=>p.Latest!.Downloads).Take(20)){
                    try{var plan=ModInstaller.Resolve(mod,catalog,adapter,new());if(plan.Count>8)continue;
                        bool plugin=false;foreach(var selection in plan){byte[] package=selection.Package.Id==loader.Id?bytes:await service.DownloadAsync(selection.Version.DownloadUrl,timeout.Token,"Проверка мода "+mod.Name);var mapped=ModInstaller.MapArchive(selection,package,adapter);if(selection.Package.Id!=loader.Id&&mapped.Keys.Any(p=>p.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)))plugin=true;}
                        if(!plugin)continue;
                        sample=mod.Id;break;
                    }catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){error=ex.Message;}
                }
                if(sample==null)throw new IOException("No tested plugin plan: "+error);
                verified.Add(adapter);evidence.Add(new{adapter.SteamId,adapter.Name,adapter.Community,adapter.Loader,LoaderVersion=loader.Latest.Version,Packages=catalog.Count,Sample=sample,VerifiedUtc=DateTimeOffset.UtcNow});Console.WriteLine("VERIFIED "+adapter.Name+" | "+catalog.Count+" packages | "+sample);return;
            }catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){error=ex.Message;}}
            Console.WriteLine("SKIP "+candidate.Name+": "+error);
        }catch(Exception ex)when(ex is IOException or OperationCanceledException or System.Net.Http.HttpRequestException or JsonException){Console.WriteLine("SKIP "+candidate.Name+": "+ex.GetType().Name);}finally{concurrency.Release();}}
        await Task.WhenAll(candidates.DistinctBy(a=>a.SteamId).Select(Verify));
        string output=Path.Combine(folder,"verified-adapters.json");File.WriteAllText(output,JsonConvert.SerializeObject(verified.OrderBy(a=>a.Name),Formatting.Indented));File.WriteAllText(Path.Combine(folder,"verification.json"),JsonConvert.SerializeObject(evidence.OrderBy(e=>JsonConvert.SerializeObject(e)),Formatting.Indented));
        Console.WriteLine($"Verified {verified.Count} new adapters against live catalogs and loader/plugin ZIP mappings. No game was modified or launched. Output: {output}");
    }
}
