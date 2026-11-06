using System.IO.Compression;
using System.Text;
using GameLauncherGUI;
using Newtonsoft.Json.Linq;

internal static class StrategyChecks
{
    public static void Run(bool ui)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        void Reject(Action action,string message){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){checks++;return;}throw new Exception(message);}
        string root=Path.Combine(Path.GetTempPath(),"PlayspaceStrategyChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string Zip(string name,params(string Path,string Content)[] entries){string path=Path.Combine(root,name+".zip");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);foreach(var entry in entries){using var writer=new StreamWriter(zip.CreateEntry(entry.Path).Open());writer.Write(entry.Content);}return path;}
        foreach(var supported in StrategyGameAdapter.All)
        {
            string install=Path.Combine(root,"games",supported.SteamId);string exe=Path.Combine(install,supported.Executable);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);File.WriteAllText(exe,"fixture, never executed");
            var candidate=new GameInfo{Name=supported.Name,SteamAppId=supported.SteamId,ExePath=exe,InstallDirectory=install};
            Check(StrategyGameAdapter.For(candidate)==supported&&supported.FindExecutable(candidate)==Path.GetFullPath(exe),"Strategy adapter/path mismatch: "+supported.Name);
        }
        var adapter=StrategyGameAdapter.All[0];
        string steamApps=Path.Combine(root,"steamapps");string gameRoot=Path.Combine(steamApps,"common","Hearts of Iron IV");Directory.CreateDirectory(gameRoot);
        string executable=Path.Combine(gameRoot,"hoi4.exe");File.WriteAllText(executable,"original fixture");
        var game=new GameInfo{Name=adapter.Name,SteamAppId=adapter.SteamId,ExePath=executable,InstallDirectory=gameRoot};
        string data=Path.Combine(root,"user-documents","HOI4"),modRoot=Path.Combine(data,"mod"),baseRoot=Path.Combine(root,"original-base-mod");
        Directory.CreateDirectory(modRoot);Directory.CreateDirectory(baseRoot);
        string baseMetadata="name=\"Base Fixture\"\nsupported_version=\"1.*\"\npath=\""+baseRoot.Replace('\\','/')+"\"\n";
        File.WriteAllText(Path.Combine(modRoot,"base.mod"),baseMetadata);
        string load=Path.Combine(data,"dlc_load.json");File.WriteAllText(load,"{\"enabled_mods\":[\"mod/base.mod\"],\"disabled_dlcs\":[\"keep-dlc\"],\"other\":123}");
        var manager=new StrategyModsManager(adapter,game,data,()=>{});
        string metadata="# test fixture\nname=\"Fixture Overhaul\"\nsupported_version=\"1.*\"\nremote_file_id=\"12345\"\npath=\"C:/creator/path\"\ndependencies={\"Base Fixture\"}\ntags={\"Alternative History\"}\nreplace_path=\"history/states\"\n";
        string archive=Zip("valid",("Wrapper/descriptor.mod",metadata),("Wrapper/common/test.txt","fixture data"));
        manager.ImportZip(archive,new Progress<string>(),CancellationToken.None);
        var imported=manager.List().Single(mod=>mod.Name=="Fixture Overhaul");
        Check(imported.ManagedId!=null&&!imported.Enabled,"Imported mod wasn't registered disabled");
        Check(Directory.Exists(imported.Content)&&File.ReadAllText(Path.Combine(imported.Content,"common/test.txt"))=="fixture data","Content not copied");
        string registration=File.ReadAllText(Path.Combine(data,imported.Descriptor!));
        Check(ParadoxDescriptor.Value(registration,"path")==imported.Content.Replace('\\','/')&&!registration.Contains("creator/path")&&!registration.Contains("remote_file_id"),"Creator path/Workshop identity retained");
        Check(registration.Contains("replace_path")&&registration.Contains("Base Fixture"),"Descriptor metadata lost");
        Check(!File.ReadAllText(Path.Combine(imported.Content,"descriptor.mod")).Contains("remote_file_id"),"Local content still claims Workshop identity");
        Check(!File.ReadAllBytes(Path.Combine(data,imported.Descriptor!)).Take(3).SequenceEqual(new byte[]{239,187,191}),"Registration has UTF8 BOM");
        manager.SetEnabled(imported,true);imported=manager.List().Single(mod=>mod.Name=="Fixture Overhaul");Check(imported.Enabled,"Enable failed");
        var json=JObject.Parse(File.ReadAllText(load));Check((int)json["other"]! ==123&&json["disabled_dlcs"]![0]!.ToString()=="keep-dlc","Unrelated DLC/settings modified");
        Reject(()=>manager.Move(imported,-1),"Dependency order violation allowed");
        var baseEntry=manager.List().Single(mod=>mod.Descriptor=="mod/base.mod");Reject(()=>manager.SetEnabled(baseEntry,false),"Required base mod disabled");
        // The Paradox launcher can rewrite dlc_load.json; PLAYSPACE restores its own desired list only at launch.
        json["enabled_mods"]=new JArray();json["disabled_dlcs"]=new JArray("later-dlc");File.WriteAllText(load,json.ToString());
        Check(manager.List().Single(mod=>mod.Name=="Fixture Overhaul").Enabled,"Desired profile lost after external launcher change");
        Check(manager.PrepareDirectExecutable()==executable,"Direct launch target not resolved");
        json=JObject.Parse(File.ReadAllText(load));Check(json["enabled_mods"]!.Count()==2&&json["disabled_dlcs"]![0]!.ToString()=="later-dlc","Launch didn't restore profile or lost DLC choice");
        manager.SetEnabled(imported,false);manager.Remove(imported);Check(File.Exists(Path.Combine(modRoot,"base.mod")),"Unmanaged descriptor removed");
        Check(!File.Exists(Path.Combine(imported.Content,"common/test.txt")),"Managed content not removed");
        string workshop=Path.Combine(steamApps,"workshop","content",adapter.SteamId,"9876543210987");Directory.CreateDirectory(workshop);
        File.WriteAllText(Path.Combine(workshop,"descriptor.mod"),"name=\"Workshop Fixture\"\nsupported_version=\"1.*\"\n");File.WriteAllText(Path.Combine(workshop,"data.txt"),"Steam owns this");
        var downloaded=manager.List().Single(mod=>mod.Name=="Workshop Fixture");Check(downloaded.Workshop&&downloaded.Descriptor==null,"Downloaded Workshop mod not discovered");
        manager.SetEnabled(downloaded,true);downloaded=manager.List().Single(mod=>mod.Name=="Workshop Fixture");Check(downloaded.Enabled&&downloaded.ManagedId!=null,"Workshop registration/enable failed");
        manager.Remove(downloaded);Check(File.ReadAllText(Path.Combine(workshop,"data.txt"))=="Steam owns this","Workshop source deleted");
        string bad=Zip("traversal",("descriptor.mod","name=\"Bad\""),("../escape.txt","bad"));Reject(()=>manager.ImportZip(bad,new Progress<string>(),CancellationToken.None),"Zip traversal allowed");
        bad=Zip("installer",("descriptor.mod","name=\"Bad\""),("install.exe","bad"));Reject(()=>manager.ImportZip(bad,new Progress<string>(),CancellationToken.None),"Native installer accepted");
        bad=Zip("no-descriptor",("common/a.txt","data"));Reject(()=>manager.ImportZip(bad,new Progress<string>(),CancellationToken.None),"No-descriptor archive accepted");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool wasCancelled=false;try{manager.ImportZip(archive,new Progress<string>(),cancelled.Token);}catch(OperationCanceledException){wasCancelled=true;}Check(wasCancelled,"Cancelled import committed");}
        manager.RestoreControl();Check(manager.PrepareDirectExecutable()==null,"Direct profile active after restore");
        json=JObject.Parse(File.ReadAllText(load));Check(json["enabled_mods"]!.Count()==1&&json["enabled_mods"]![0]!.ToString()=="mod/base.mod"&&json["disabled_dlcs"]![0]!.ToString()=="later-dlc","Restore lost original mods or later DLC change");
        Check(File.ReadAllText(executable)=="original fixture","Original game binary modified");
        Console.WriteLine($"PASS: {checks} strategy adapter, ZIP import, metadata, Workshop discovery, dependency/order, profile/restore and ownership checks. Only temporary fixtures changed.");
        if(ui) Ui(adapter,game);
    }
    private static void Ui(StrategyGameAdapter adapter,GameInfo game)
    {
        using var panel=new StrategyModsPanel(adapter,game);panel.Size=new Size(1000,480);panel.PerformLayout();
        using var image=new Bitmap(panel.Width,panel.Height);using(var graphics=Graphics.FromImage(image))ModUiChecks.Render(panel,graphics);
        string artifacts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../artifacts"));Directory.CreateDirectory(artifacts);image.Save(Path.Combine(artifacts,"strategy-mods.png"));
        Console.WriteLine("PASS: strategy control panel renders without reading or writing actual game documents.");
    }
}
