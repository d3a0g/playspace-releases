using System.IO.Compression;
using System.Xml;
using GameLauncherGUI;

internal static class ModExpansionChecks
{
    internal static void Run()
    {
        int count=0;void Check(bool condition,string text){if(!condition)throw new Exception(text);count++;}
        void Reject(Action action,string text){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or XmlException or ArgumentException){count++;return;}throw new Exception(text);}
        var extra=AdditionalModAdapters.Load();Check(extra.Count>=20,"Expected at least 20 additional verified catalog adapters");
        var recommended=RecommendedGame.All.Select(g=>g.Id).ToHashSet();Check(extra.All(a=>recommended.Contains(a.SteamId))&&ExtraNativeMods.All.All(a=>recommended.Contains(a.SteamId)),"New adapter game is absent from recommendations");
        var ids=ModGameAdapter.All.Select(a=>a.SteamId).Concat(NativeModAdapter.All.Select(a=>a.SteamId)).Concat(StrategyGameAdapter.All.Select(a=>a.SteamId)).ToArray();Check(ids.Distinct().Count()==ids.Length,"Conflicting adapter IDs");
        string root=Path.Combine(Path.GetTempPath(),"PlaySpaceExpandedMods-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            foreach(var adapter in extra){string game=Path.Combine(root,adapter.Community);Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,adapter.Executable),"fixture");var info=new GameInfo{Name=adapter.Name,SteamAppId=adapter.SteamId,ExePath=Path.Combine(game,adapter.Executable),InstallDirectory=game};
                Check(ModGameAdapter.For(info)?.SteamId==adapter.SteamId,"Adapter Steam match failed");Check(adapter.FindRoot(info)==null,"Missing data folder accepted");Directory.CreateDirectory(Path.Combine(game,adapter.DataFolder!));Check(adapter.FindRoot(info)==game,"Valid root not detected");}
            byte[] Package(params (string Name,byte[] Data)[] entries){using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))foreach(var pair in entries){using var output=zip.CreateEntry(pair.Name).Open();output.Write(pair.Data);}return stream.ToArray();}
            byte[] Text(string value)=>System.Text.Encoding.UTF8.GetBytes(value);
            byte[] vpk=new byte[12];BitConverter.GetBytes(0x55AA1234u).CopyTo(vpk,0);BitConverter.GetBytes(2u).CopyTo(vpk,4);
            foreach(var adapter in ExtraNativeMods.All){
                string gameDir=Path.Combine(root,"native",adapter.SteamId);Directory.CreateDirectory(gameDir);string exe=Path.Combine(gameDir,"Fixture-"+Guid.NewGuid().ToString("N")+".exe");File.WriteAllText(exe,"fixture");var game=new GameInfo{Name=adapter.Name,SteamAppId=adapter.SteamId,ExePath=exe,InstallDirectory=gameDir};string modRoot=adapter.Root(game,Path.Combine(root,"user"));
                var archive=adapter.Layout switch{
                    "vpk"=>Package(("Wrapper/fixture.vpk",vpk)),"pack"=>Package(("Wrapper/fixture.pack",Text("PFH5fixture"))),
                    "bannerlord"=>Package(("Wrapper/SubModule.xml",Text("<Module><Id value=\"FixtureModule\"/></Module>")),("Wrapper/data.txt",Text("fixture"))),
                    "gmod"=>Package(("Wrapper/addon.json",Text("{\"title\":\"Fixture\"}")),("Wrapper/lua/fixture.lua",Text("-- fixture"))),
                    "dontstarve"=>Package(("Wrapper/modinfo.lua",Text("name='Fixture'")),("Wrapper/modmain.lua",Text("-- fixture"))),
                    "7days"=>Package(("Wrapper/ModInfo.xml",Text("<xml><Name value=\"Fixture\"/></xml>")),("Wrapper/Config/fixture.xml",Text("<configs/>"))),
                    "civ"=>Package(("Wrapper/Fixture.modinfo",Text("<Mod id=\"fixture\"/>")),("Wrapper/data.sql",Text("-- fixture"))),
                    "darkest"=>Package(("Wrapper/project.xml",Text("<project/>")),("Wrapper/data.json",Text("{}"))),_=>throw new Exception("Untested layout")};
                string file=Path.Combine(root,adapter.SteamId+".zip");File.WriteAllBytes(file,archive);NativeModImport.Install(adapter,game,file,modRoot,new Progress<string>(),default);
                var installer=new ModInstaller(modRoot);var state=installer.ReadState();Check(state.Packages.Count==1,"Native ZIP did not produce managed package");
                if(adapter.Layout=="bannerlord")Check(File.Exists(Path.Combine(modRoot,"FixtureModule","SubModule.xml")),"Bannerlord Id folder not preserved");
                string unrelated=Path.Combine(modRoot,"do-not-delete.txt");File.WriteAllText(unrelated,"original");installer.Uninstall(state.Packages.Single().Key);Check(File.Exists(unrelated)&&installer.ReadState().Packages.Count==0,"Native removal changed unrelated files");
                File.WriteAllBytes(file,Package(("../escape.vpk",vpk)));Reject(()=>NativeModImport.Install(adapter,game,file,modRoot,new Progress<string>(),default),"ZIP path escape accepted");
            }
            var banner=ExtraNativeMods.All.Single(a=>a.Layout=="bannerlord");var bad=new GameInfo{Name="Fixture",ExePath=Path.Combine(root,"FixtureGame.exe"),InstallDirectory=root};File.WriteAllText(bad.ExePath,"fixture");string badFile=Path.Combine(root,"bad.zip");
            File.WriteAllBytes(badFile,Package(("SubModule.xml",Text("<Module><Id value=\"../Native\"/></Module>"))));Reject(()=>NativeModImport.Install(banner,bad,badFile,Path.Combine(root,"bad-mods"),new Progress<string>(),default),"Unsafe module Id accepted");
            File.WriteAllBytes(badFile,Package(("SubModule.xml",Text("<!DOCTYPE Module [<!ENTITY xxe SYSTEM 'file:///C:/secret'>]><Module><Id value='&xxe;'/></Module>"))));Reject(()=>NativeModImport.Install(banner,bad,badFile,Path.Combine(root,"bad-mods"),new Progress<string>(),default),"XML external entity accepted");
            using var loaderZip=new MemoryStream(Package(("dotnet/run.cmd",Text("fixture"))));
            var adapter0=ModGameAdapter.All[0];var loader=new ModPackage{Owner=adapter0.Loader.Split('-')[0],Name=adapter0.Loader.Split('-')[1],Versions=new(){new(){Version="1.0"}}};Reject(()=>ModInstaller.MapArchive(new(loader,loader.Latest!),loaderZip.ToArray(),adapter0),"Loader runtime directory allowed command script");
            Console.WriteLine($"PASS: {count} expanded mod checks: {ModGameAdapter.All.Length+NativeModAdapter.All.Length+StrategyGameAdapter.All.Length} game adapters, recommendation matching, strict roots, new native layouts/install/uninstall, ZIP/XML containment and loader guards. Fixtures only; no games or downloads.");
        }finally{Directory.Delete(root,true);}
    }
}
