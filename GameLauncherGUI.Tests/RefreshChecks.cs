using GameLauncherGUI;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Reflection;

internal static class RefreshChecks
{
    public static void Run(bool ui)
    {
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceRefresh-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){checks++;return;}throw new Exception("Invalid operation accepted");}
        Check(RecommendedGame.All.Length==500&&RecommendedGame.All.Select(g=>g.Id).Distinct().Count()==500,"Expected exactly 500 unique games");
        Check(UpdateService.BundledFeed(Path.Combine(folder,"missing.json"))==UpdateService.DefaultFeed,"Portable EXE lost update feed");
        string empty=Path.Combine(folder,"empty.json");File.WriteAllText(empty,"{\"feedUrl\":\"\"}");Check(UpdateService.BundledFeed(empty)==UpdateService.DefaultFeed,"Empty config disabled embedded updates");
        string formatted=GameDescription.Format(JObject.Parse("{developers:['Fixture Studio'],genres:[{description:'Puzzle'}],detailed_description:'<h2>About</h2><p>Text &amp; detail</p><ul><li>One</li><li>Two</li></ul><script>bad()</script>'}"));
        Check(formatted.Contains("Fixture Studio")&&formatted.Contains("ABOUT")&&formatted.Contains("• One")&&!formatted.Contains("bad()")&&!formatted.Contains("<p>"),"Description layout/sanitization failed");
        string dllFolder=Path.Combine(folder,"dlss");Directory.CreateDirectory(dllFolder);string target=Path.Combine(dllFolder,"nvngx_dlss.dll"),source=Path.Combine(folder,"signed-fixture-not-executable.dll");File.WriteAllText(target,"original");File.WriteAllText(source,"replacement");
        var dlss=new DlssManager(target);dlss.ApplyVerified(source);Check(File.ReadAllText(target)=="replacement"&&File.ReadAllText(target+".playspace-original")=="original","DLSS backup/apply failed");
        File.WriteAllText(target,"game update");Reject(dlss.Restore);File.WriteAllText(target,"replacement");dlss.Restore();Check(File.ReadAllText(target)=="original","DLSS restore failed");
        Reject(()=>dlss.ApplyVerified(source,"bad-hash"));Check(DlssManager.Blocked(new GameInfo{Name="CS2",SteamAppId="730",ExePath="none"}),"Online game DLSS guard failed");
        string Zip(string name,params(string Path,string Text)[] entries){string file=Path.Combine(folder,name+".zip");using var zip=ZipFile.Open(file,ZipArchiveMode.Create);foreach(var entry in entries){using var writer=new StreamWriter(zip.CreateEntry(entry.Path).Open());writer.Write(entry.Text);}return file;}
        string gameRoot=Path.Combine(folder,"game");Directory.CreateDirectory(gameRoot);File.WriteAllText(Path.Combine(gameRoot,"StardewModdingAPI.exe"),"fixture");
        var game=new GameInfo{Name="Stardew",SteamAppId="413150",InstallDirectory=gameRoot,ExePath=Path.Combine(gameRoot,"Stardew Valley.exe")};var adapter=NativeModAdapter.For(game)!;string root=adapter.Root(game);
        string valid=Zip("smapi",("Wrapper/manifest.json","{\"Name\":\"Fixture\",\"UniqueID\":\"Test.Mod\"}"),("Wrapper/test.dll","fixture not code"));
        NativeModImport.Install(adapter,game,valid,root,new Progress<string>(),default);var state=new ModInstaller(root).ReadState();Check(state.Packages.Count==1&&Directory.GetFiles(root,"test.dll",SearchOption.AllDirectories).Length==1,"SMAPI ZIP import failed");
        Reject(()=>NativeModImport.Install(adapter,game,Zip("bad",("../escape.dll","x")),root,new Progress<string>(),default));
        Reject(()=>NativeModImport.Install(adapter,game,Zip("installer",("setup.exe","x")),root,new Progress<string>(),default));
        new ModInstaller(root).Uninstall(state.Packages.Keys.Single());Check(Directory.GetFiles(root,"test.dll",SearchOption.AllDirectories).Length==0,"Local mod uninstall failed");
        var factorio=NativeModAdapter.All.Single(a=>a.SteamId=="427520");string factorioRoot=Path.Combine(folder,"factorio");
        NativeModImport.Install(factorio,game,Zip("factorio",("fixture_1.0.0/info.json","{\"name\":\"fixture\",\"version\":\"1.0.0\"}")),factorioRoot,new Progress<string>(),default);
        Check(File.Exists(Path.Combine(factorioRoot,"fixture_1.0.0.zip")),"Factorio didn't preserve native ZIP format");
        if(ui){
            Directory.CreateDirectory("artifacts");using var main=new Form1(Path.Combine(folder,"library"));main.Show();Application.DoEvents();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var editor=(TextBox)typeof(Form1).GetField("searchGames",flags)!.GetValue(main)!;
            Check(editor.Parent is ModernSearchField&&editor.Top>=0&&editor.Bottom<=editor.Parent.Height,"Library search clipped");
            var close=(Label)typeof(Form1).GetField("btnClose",flags)!.GetValue(main)!;var actions=close.Parent!.Controls.Cast<Control>().ToArray();Check(actions.Length==3&&actions.Select(c=>c.Top).Distinct().Count()==1,"Window buttons misaligned");
            using(var image=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(image,main.ClientRectangle);image.Save("artifacts/launcher-refresh.png");}
            using var splash=new StartupSplash();splash.Report("Загружаем библиотеку…");splash.Show();using(var image=new Bitmap(splash.Width,splash.Height)){splash.DrawToBitmap(image,splash.ClientRectangle);image.Save("artifacts/startup-splash.png");}
            using var dialog=new DlssForm(game);dialog.Show();Application.DoEvents();using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,dialog.ClientRectangle);image.Save("artifacts/dlss-panel.png");}
            using var modal=new StudioDialog();modal.Show();typeof(StudioDialog).GetMethod("ProcessCmdKey",flags)!.Invoke(modal,new object[]{Message.Create(IntPtr.Zero,0,IntPtr.Zero,IntPtr.Zero),Keys.Escape});Check(modal.IsDisposed,"Escape didn't close section");
        }
        Console.WriteLine($"PASS: {checks} refresh checks: 500 games, portable updates, description formatting, native ZIP import/removal, DLSS backup/restore/guards, search, window alignment, Escape and splash. Fixtures only.");
    }
}
