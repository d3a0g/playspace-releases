using System.Security.Cryptography;
using System.Text;
using GameLauncherGUI;
using Newtonsoft.Json;

internal static class LauncherHealthChecks
{
    internal static void Run(bool ui)
    {
        int count=0;void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        void Reject(Action action,string message){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException or JsonException){count++;return;}throw new Exception(message);}
        string root=Path.Combine(Path.GetTempPath(),"PlaySpaceHealth-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string source=Path.Combine(root,"source"),destination=Path.Combine(root,"target"),backup=Path.Combine(root,"backup.json");Directory.CreateDirectory(source);Directory.CreateDirectory(destination);
            string exe=Path.Combine(root,"Game.exe");File.WriteAllText(exe,"fixture");
            var game=new GameInfo{Name="Fixture",ExePath=exe,SteamAppId="",IsPinned=true,LaunchArguments="--token private-argument",SteamPlaytimeUser="76561190000000000",CustomIconPath="private-image",Care=new(){Notes="private-note",SaveDirectory=Path.Combine(root,"saves"),BackupBeforeLaunch=true},Performance=new(){ApplyOnLaunch=true,TargetFps=120}};
            GameLibraryStore.Save(Path.Combine(source,"games.json"),new(){game});
            new LauncherPreferences{UpdateFeedUrl="https://example.test/feed?token=private-feed",HideWhilePlaying=true}.Save(source);
            var collections=new LibraryCollections(source);collections.Create("Кооператив");collections.Set(game.LibraryId,"Кооператив",true);new RecommendationFavorites(source).Toggle("620");
            File.WriteAllText(Path.Combine(source,"credentials.json"),"private-credential");
            SettingsTransfer.Export(source,backup);string json=File.ReadAllText(backup);
            foreach(string secret in new[]{"private-argument","private-note","private-image","private-feed","private-credential","76561190000000000"})Check(!json.Contains(secret),"Private data included in settings export: "+secret);
            var b=SettingsTransfer.Read(backup);Check(b.Games.Count==1&&b.Games[0].IsPinned&&b.Games[0].Performance?.TargetFps==120,"Game settings lost in export");
            Check(b.Collections.Members[game.LibraryId].Contains("Кооператив")&&b.Favorites.Contains("620"),"Collections/favorites lost");
            Check(b.Games[0].Performance?.ApplyOnLaunch==false&&b.Games[0].Care?.BackupBeforeLaunch==false&&b.Games[0].LaunchArguments=="","Imported behavior auto-enabled");
            Reject(()=>SettingsTransfer.Export(source,Path.Combine(source,"games.json")),"Export overwrote live library");
            string damaged=Path.Combine(root,"damaged.json");File.WriteAllText(damaged,json.Replace("Fixture","Changed"));Reject(()=>SettingsTransfer.Read(damaged),"Tampered checksum accepted");
            void WriteBundle(SettingsBundle bundle,string path){string payload=JsonConvert.SerializeObject(bundle);File.WriteAllText(path,JsonConvert.SerializeObject(new SettingsEnvelope(1,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),payload)));}
            b.Games[0].ExePath="https://example.test/not-an-exe";WriteBundle(b,damaged);Reject(()=>SettingsTransfer.Read(damaged),"External launch URL accepted as local EXE");b.Games[0].ExePath=exe;
            File.WriteAllText(Path.Combine(destination,"games.json"),"[]");File.WriteAllText(Path.Combine(destination,"preferences.json"),"{\"NotificationsEnabled\":false}");File.WriteAllText(Path.Combine(destination,"credentials.json"),"keep-secret");
            int writes=0;Reject(()=>SettingsTransfer.Restore(destination,backup,(path,value)=>{if(++writes==2)throw new IOException("Injected write failure");LocalJson.Write(path,value);}),"Restore did not report failed write");
            Check(File.ReadAllText(Path.Combine(destination,"games.json"))=="[]"&&File.ReadAllText(Path.Combine(destination,"preferences.json"))=="{\"NotificationsEnabled\":false}","Partial restore failed to roll back");
            string safety=SettingsTransfer.Restore(destination,backup);Check(File.Exists(safety)&&SettingsTransfer.Read(safety).Games.Count==0,"Pre-restore safety copy missing");
            Check(GameLibraryStore.Load(Path.Combine(destination,"games.json")).Games.Count==1&&File.ReadAllText(Path.Combine(destination,"credentials.json"))=="keep-secret","Restore lost data or touched credentials");
            string cache=Path.Combine(source,"posters");Directory.CreateDirectory(cache);File.WriteAllText(Path.Combine(cache,"one.jpg"),"fixture");var preview=LauncherStorage.Scan(source,"posters");Check(preview.Count==1&&preview[0].Bytes==7,"Storage scan incorrect");
            File.WriteAllText(Path.Combine(cache,"two.jpg"),"new");Check(LauncherStorage.Delete(source,LauncherStorage.Groups[0],preview)==1&&File.Exists(Path.Combine(cache,"two.jpg")),"Cleanup removed files absent from preview");
            preview=LauncherStorage.Scan(source,"posters");File.AppendAllText(Path.Combine(cache,"two.jpg"),"changed");Reject(()=>LauncherStorage.Delete(source,LauncherStorage.Groups[0],preview),"Cleanup removed modified file");
            Reject(()=>ManagedPaths.Child(source,"../escape"),"Path escape accepted");Reject(()=>LauncherStorage.Delete(source,new("fake","posters",true),Array.Empty<StorageFile>()),"Unknown cleanup target accepted");
            Reject(()=>LauncherStorage.Delete(source,LauncherStorage.Groups[0],new[]{new StorageFile("credentials.json",11,0)}),"Cleanup accepted private root file");
            string report=LauncherSession.Redact("token=secret-value\nmail@example.com\nC:\\Users\\Private\\secret.txt\nhttps://example.test/?key=secret-query\n76561190000000000\n0123456789abcdef0123456789abcdef");
            foreach(string secret in new[]{"secret-value","mail@example.com","Private","secret-query","76561190000000000","0123456789abcdef0123456789abcdef"})Check(!report.Contains(secret),"Report did not redact: "+secret);
            LauncherSession.Record(source,"Fixture operation",new IOException("private-error-path"));report=LauncherSession.Report(source,1,"fixture comment");Check(report.Contains("IOException")&&!report.Contains("private-error-path")&&!report.Contains("keep-secret"),"Report leaks exception message");
            LauncherSession.Configure(new[]{"--safe-mode"});Check(LauncherSession.SafeMode&&Workspace.For(source).Offline,"Safe mode leaves network enabled");LauncherSession.Configure(Array.Empty<string>());Check(!LauncherSession.SafeMode,"Safe mode did not reset");
            string modRoot=Path.Combine(root,"mods");Directory.CreateDirectory(modRoot);Directory.CreateDirectory(Path.Combine(modRoot,".playspace-mods"));File.WriteAllText(Path.Combine(modRoot,".playspace-mods","pending.json"),"{}");
            Check(GameReadiness.Inspect(new(){Name="Fixture",ExePath=exe,InstallDirectory=modRoot},()=>false).Any(i=>i.Name=="Файл запуска"),"Readiness lost local EXE check");
            File.Copy(exe,Path.Combine(modRoot,"Game.exe"));Check(GameReadiness.Inspect(new(){Name="Fixture",ExePath=Path.Combine(modRoot,"Game.exe")},()=>false).Any(i=>i.Blocking&&i.Name=="Операция с модами"),"Pending mod transaction not detected");
            var adapter=ModGameAdapter.All[0];var loader=new ModPackage{Owner=adapter.Loader.Split('-')[0],Name=adapter.Loader.Split('-')[1],Versions=new(){new(){Version="1.0.0"}}};
            var mod=new ModPackage{Owner="Fixture",Name="Plugin",Versions=new(){new(){Version="1.0.0",Dependencies=new[]{"Missing-Dependency-1.0.0"}}}};
            Check(!ModCompatibility.Evaluate(mod,new[]{loader,mod},adapter,new(),game,root).CanInstall,"Missing dependency passed compatibility");
            mod.Latest!.Dependencies=Array.Empty<string>();Check(ModCompatibility.Evaluate(mod,new[]{loader,mod},adapter,new(),game,root).CanInstall,"Valid dependency plan blocked");
            Check(ModCompatibility.Evaluate(mod,new[]{loader,mod},adapter,new(),game,root).Detail.Contains("не указана автором"),"Unknown game version presented as compatible");
            mod.Latest.SupportedGameVersions=new[]{"99.99"};Check(!ModCompatibility.Evaluate(mod,new[]{loader,mod},adapter,new(),game,root).CanInstall,"Explicit incompatible version ignored");
            string sizeFolder=Path.Combine(root,"size-fixture");Directory.CreateDirectory(Path.Combine(sizeFolder,"nested"));File.WriteAllBytes(Path.Combine(sizeFolder,"Game.exe"),new byte[1024]);File.WriteAllBytes(Path.Combine(sizeFolder,"nested","asset.bin"),new byte[2048]);
            Check(GameDiskUsage.Measure(sizeFolder,null,default).Bytes==3072,"Game folder size excludes nested files");
            Check(GameDiskUsage.Measure(null,Path.Combine(sizeFolder,"Game.exe"),default).ExeFolder,"EXE-only fallback is not labelled");
            string binaries=Path.Combine(sizeFolder,"Binaries","Win64");Directory.CreateDirectory(binaries);File.WriteAllBytes(Path.Combine(binaries,"Shipping.exe"),new byte[512]);
            Check(GameDiskUsage.Measure(binaries,Path.Combine(binaries,"Shipping.exe"),default).Bytes==3584,"Technical EXE subfolder counted instead of game root");
            string wrong=Path.Combine(root,"wrong-game");Directory.CreateDirectory(wrong);File.WriteAllBytes(Path.Combine(wrong,"other.bin"),new byte[9999]);
            Check(GameDiskUsage.Measure(wrong,Path.Combine(sizeFolder,"Game.exe"),default).Bytes==3584,"Unrelated stored installation directory overrides real EXE");
            string common=Path.Combine(root,"SteamLibrary","steamapps","common");string steamGame=Path.Combine(common,"FixtureSteam");Directory.CreateDirectory(Path.Combine(steamGame,"game","bin","win64"));File.WriteAllBytes(Path.Combine(steamGame,"game","bin","win64","Game.exe"),new byte[100]);File.WriteAllBytes(Path.Combine(steamGame,"assets.bin"),new byte[200]);Directory.CreateDirectory(Path.Combine(common,"OtherGame"));File.WriteAllBytes(Path.Combine(common,"OtherGame","other.bin"),new byte[900]);
            Check(GameDiskUsage.Measure(common,Path.Combine(steamGame,"game","bin","win64","Game.exe"),default).Bytes==300,"Steam root scan includes adjacent games or misses root assets");
            Check(GameDiskUsage.Measure(common,"steam://rungameid/620",default).Bytes==null,"Missing EXE caused whole Steam library scan");
            string unreal=Path.Combine(root,"UnrealGame");Directory.CreateDirectory(Path.Combine(unreal,"Engine"));Directory.CreateDirectory(Path.Combine(unreal,"Project","Content"));Directory.CreateDirectory(Path.Combine(unreal,"Project","Binaries","Win64"));File.WriteAllBytes(Path.Combine(unreal,"Project","Binaries","Win64","Game.exe"),new byte[100]);File.WriteAllBytes(Path.Combine(unreal,"Engine","engine.bin"),new byte[200]);
            Check(GameDiskUsage.Measure(null,Path.Combine(unreal,"Project","Binaries","Win64","Game.exe"),default).Bytes==300,"Unreal installation root excludes Engine folder");
            Check(GameDiskUsage.Measure(Path.GetPathRoot(root),null,default).Bytes==null,"Whole disk scanned as a game");
            Check(GameDiskUsage.Measure(null,"steam://rungameid/620",default).Bytes==null,"Steam URI shown as zero bytes");
            using(var cancelSize=new CancellationTokenSource()){cancelSize.Cancel();try{GameDiskUsage.Measure(sizeFolder,null,cancelSize.Token);throw new Exception("Size scan ignored cancellation");}catch(OperationCanceledException){count++;}}
            string steamLabel=GameCardText.Summary(new(){Name="Portal",SteamAppId="620",SteamPlaytimeMinutes=60},"1 ГБ");
            Check(steamLabel.Split("Steam").Length==2&&steamLabel.Contains("1 ГБ"),"Steam source duplicated or size missing");
            string localLabel=GameCardText.Summary(new(){Name="Fixture",PlayedSeconds=3600});Check(!localLabel.Contains("Локально")&&localLabel.Split("Локальная игра").Length==2,"Local source duplicated");
            if(ui){
                using(var wizard=new FirstRunForm(source,1)){
                    wizard.Show();Application.DoEvents();
                    IEnumerable<Control> Children(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c)));
                    var skip=Children(wizard).OfType<ModernButton>().Single(b=>b.Text=="Далее / пропустить");
                    Check(skip.Visible,"Skip missing on first step");skip.PerformClick();Check(skip.Visible,"Skip missing on second step");skip.PerformClick();
                    Check(!skip.Visible&&Children(wizard).OfType<ModernButton>().Any(b=>b.Visible&&b.Text=="Завершить настройку"),"Last step shows skip or lacks finish");wizard.Close();
                }
                string images=Path.Combine(Directory.GetCurrentDirectory(),"artifacts/improvements-previews");Directory.CreateDirectory(images);
                void Capture(Form form,string name){using(form){form.Show();Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,form.ClientRectangle);bitmap.Save(Path.Combine(images,name+".png"));form.Close();}}
                Capture(new FirstRunForm(source,1),"first-run");Capture(new SupportReportForm(source,1),"report");Capture(new SettingsTransferForm(source,()=>false),"settings-backups");Capture(new StorageManagerForm(source,new[]{game},()=>false),"storage");
                using var main=new Form1(destination,false);main.Show();Application.DoEvents();main.ClosePrompt=()=> (CloseBehavior.Exit,false);main.Close();
                count+=5;
            }
            Console.WriteLine($"PASS: {count} launcher improvements checks: settings privacy/checksum/rollback, safe mode, report redaction, cleanup containment/preview, readiness, mod compatibility and UI. Temporary fixtures only; no real installation, game changes or network calls.");
        }finally{LauncherSession.Configure(Array.Empty<string>());Directory.Delete(root,true);}
    }
}
