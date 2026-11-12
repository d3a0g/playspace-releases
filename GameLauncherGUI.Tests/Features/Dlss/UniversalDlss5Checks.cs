using GameLauncherGUI;
using System.Reflection;

internal static class UniversalDlss5Checks
{
    public static void Run(bool ui)
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaySpaceUniversal5Checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);int checks=0;
        void Check(bool okay,string message){if(!okay)throw new Exception(message);checks++;}
        void Reject(Action work){try{work();}catch(Exception ex)when(ex is IOException or InvalidDataException or InvalidOperationException or BadImageFormatException){checks++;return;}throw new Exception("Unsafe neural-rendering setup accepted");}
        var manager=new UniversalDlss5(Path.Combine(root,"module"));Check(manager.Controller()==null,"Uninstalled controller detected");
        string exe=Path.Combine(root,"game","Game.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe)!);File.WriteAllText(exe,"fixture, never executed");
        var game=new GameInfo{Name="Offline fixture",SteamAppId="123456789",InstallDirectory=Path.GetDirectoryName(exe),ExePath=exe};
        UniversalDlss5.ValidateGame(game,exe,true);checks++;Reject(()=>UniversalDlss5.ValidateGame(game,exe,false));
        string outside=Path.Combine(root,"Outside.exe");File.WriteAllText(outside,"fixture");Reject(()=>UniversalDlss5.ValidateGame(game,outside,true));
        game.SteamAppId="730";Reject(()=>UniversalDlss5.ValidateGame(game,exe,true));game.SteamAppId="123456789";
        Directory.CreateDirectory(Path.Combine(game.InstallDirectory!,"EasyAntiCheat"));Reject(()=>UniversalDlss5.ValidateGame(game,exe,true));Directory.Delete(Path.Combine(game.InstallDirectory!,"EasyAntiCheat"));
        Reject(()=>manager.RegisterController(exe));
        Reject(()=>UniversalDlss5.ValidateNvidiaRuntimeFile(Path.Combine(root,"nvngx_dlss.dll")));
        string fakeRuntime=Path.Combine(root,"nvngx_dlssnr.dll");File.WriteAllText(fakeRuntime,"not a PE DLL");Reject(()=>UniversalDlss5.ValidateNvidiaRuntimeFile(fakeRuntime));
        string fakeInstaller=Path.Combine(root,"fake.exe");File.WriteAllText(fakeInstaller,"not the release");Reject(()=>manager.ExtractVerified(fakeInstaller,"not-executed",default).GetAwaiter().GetResult());
        if(ui){using var form=new UniversalDlss5Form(game,Path.Combine(root,"ui-module"));form.Show();Application.DoEvents();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var launch=(ModernButton)typeof(UniversalDlss5Form).GetField("launch",flags)!.GetValue(form)!;Check(!launch.Enabled,"Launch enabled without runtime/controller");var consent=(CheckBox)typeof(UniversalDlss5Form).GetField("consent",flags)!.GetValue(form)!;consent.Checked=true;Check(!launch.Enabled,"Consent bypassed missing prerequisites");Directory.CreateDirectory("artifacts");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save("artifacts/universal-dlss5.png");}}
        Console.WriteLine($"PASS: {checks} UniversalDLSS5 checks: explicit opt-in, game ownership, online/anti-cheat guard, runtime type, pinned package and disabled launch prerequisites. No external controller/game started.");
    }
    public static void VerifyRealPackage()
    {
        string source=Path.GetFullPath("artifacts/upstream-dlss5/UniversalDLSS5-0.5.1-Setup-x64.exe");string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceUniversal5Verified-"+Guid.NewGuid().ToString("N"));var manager=new UniversalDlss5(folder);string zip=UniversalDlss5.SevenZip()??throw new Exception("7-Zip missing");
        string controller=manager.ExtractVerified(source,zip,default).GetAwaiter().GetResult();UniversalDlss5.ValidateFolder(Path.GetDirectoryName(controller)!);manager.RegisterController(controller);
        if(manager.Controller()!=controller||!File.Exists(Path.Combine(Path.GetDirectoryName(controller)!,"LICENSE")))throw new Exception("Package wasn't installed with its license");
        string gameFolder=Path.Combine(folder,"game-fixture");Directory.CreateDirectory(gameFolder);string gameExe=Path.Combine(gameFolder,"Offline.exe");File.WriteAllText(gameExe,"fixture, never executed");var game=new GameInfo{Name="Offline fixture",ExePath=gameExe,InstallDirectory=gameFolder};
        var command=UniversalDlss5.StartInfo(controller,game,gameExe,true);if(command.ArgumentList.Count!=2||command.ArgumentList[0]!="--launch"||command.ArgumentList[1]!=gameExe)throw new Exception("Controller launch command wrong");
        File.AppendAllText(controller,"tampered");try{UniversalDlss5.ValidateFolder(Path.GetDirectoryName(controller)!);throw new Exception("Tampered controller accepted");}catch(InvalidDataException){}
        Console.WriteLine("PASS: real UniversalDLSS5 0.5.1 package SHA-256, extraction without running installer, all 7 critical hashes, license retention, launch arguments and tamper rejection. No controller or game started.");
    }
}
