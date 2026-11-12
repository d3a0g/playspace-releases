using GameLauncherGUI;

internal static class Rtx30Checks
{
    private static int checks;
    private static void Check(bool result,string message){if(!result)throw new Exception(message);checks++;}
    private static void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidOperationException or ArgumentException or BadImageFormatException){checks++;return;}throw new Exception("Unsafe RTX operation accepted");}
    internal static void Run(bool ui)
    {
        foreach(string adapter in new[]{"NVIDIA GeForce RTX 3060","NVIDIA GeForce RTX 3060 Ti","NVIDIA GeForce RTX 3080 Laptop GPU","Intel UHD / NVIDIA GeForce RTX 3090"}){var gpu=RtxCompatibility.Detect(adapter);Check(gpu.IsAmpere&&gpu.SuperResolution&&!gpu.NativeFrameGeneration,"RTX 30 classification");}
        Check(RtxCompatibility.Detect("NVIDIA GeForce RTX 2080").SuperResolution,"RTX 20 SR");Check(RtxCompatibility.Detect("RTX 4060").NativeFrameGeneration,"RTX 40 FG");
        foreach(var adapter in new[]{"Radeon RX 6700 XT","GTX 1080","Unknown","RTX 30600"})Check(!RtxCompatibility.Detect(adapter).SuperResolution,"Unknown GPU classified as RTX");
        string ini="[Upscalers]\nDx12Upscaler=auto\n[FrameGen]\nEnabled=auto\nFGInput=auto\nFGOutput=auto\n[DLSS]\nRenderPresetOverride=auto\nRenderPresetForAll=auto\n[DLSSD]\nRenderPresetForAll=auto\n[Other]\nEnabled=auto\n";
        foreach(var mode in Enum.GetValues<RtxFrameMode>())foreach(var preset in Enum.GetValues<RtxPreset>()){
            string configured=OptiScalerManager.Configure(ini,mode,preset);
            Check(configured.Contains("[DLSSD]\r\nRenderPresetForAll=auto")&&configured.Contains("[Other]\r\nEnabled=auto"),"Other sections changed");
            Check(configured.Contains("FGOutput="+(mode==RtxFrameMode.Disabled?"nofg":mode==RtxFrameMode.Nukem?"nukems":"fsrfg")),"Wrong FG backend");
            Check(configured.Contains("RenderPresetForAll="+(preset==RtxPreset.K?11:preset==RtxPreset.L?12:preset==RtxPreset.M?13:0)),"Wrong DLSS model");
        }
        Reject(()=>OptiScalerManager.Configure(ini,(RtxFrameMode)999,RtxPreset.K));Reject(()=>OptiScalerManager.Configure("[DLSS]\n",RtxFrameMode.Disabled,RtxPreset.K));
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceRtx30-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            string exe=Path.Combine(folder,"Fixture.exe");File.Copy(Environment.ProcessPath!,exe);var game=new GameInfo{Name="Fixture",ExePath=exe,InstallDirectory=folder};
            Check(OptiScalerManager.ValidateGame(game,exe,true,"RTX 3060",true)==folder,"Valid x64 game rejected");
            Reject(()=>OptiScalerManager.ValidateGame(game,exe,false,"RTX 3060",true));Reject(()=>OptiScalerManager.ValidateGame(game,exe,true,"RX 6700",true));Reject(()=>OptiScalerManager.ValidateGame(game,exe,true,"RTX 3060",false));
            string outside=Path.Combine(Path.GetTempPath(),"unrelated.exe");Reject(()=>OptiScalerManager.ValidateGame(game,outside,true,"RTX 3060",true));
            string anti=Path.Combine(folder,"Project","Binaries","Win64","EasyAntiCheat_EOS.dll");Directory.CreateDirectory(Path.GetDirectoryName(anti)!);File.WriteAllText(anti,"fixture");Check(DlssManager.Blocked(game),"Nested anti-cheat missed");Reject(()=>OptiScalerManager.ValidateGame(game,exe,true,"RTX 3060",true));File.Delete(anti);
            Check(DlssManager.Blocked(new(){Name="VALORANT",ExePath=exe}),"Non-Steam online game missed");
            var installer=new ModInstaller(folder);var selection=new ModSelection(new(){Owner="PlaySpace",Name="OptiScaler"},new(){Version=OptiScalerManager.Version});
            var files=new Dictionary<string,byte[]>{{"dxgi.dll",new byte[]{1,2,3}},{"OptiScaler.ini",System.Text.Encoding.UTF8.GetBytes(ini)}};
            installer.Apply(new(){(selection,files)},new Progress<string>());Check(OptiScalerManager.Installed(exe),"Managed install missing");File.WriteAllText(Path.Combine(folder,"OptiScaler.ini"),"user config");OptiScalerManager.Remove(game,exe);
            Check(!File.Exists(Path.Combine(folder,"dxgi.dll"))&&!File.Exists(Path.Combine(folder,"OptiScaler.ini")),"Owned mod not removed");Check(Directory.GetFiles(Path.Combine(folder,".playspace-mods","preserved")).Any(p=>File.ReadAllText(p)=="user config"),"Modified INI not preserved");
            installer.Apply(new(){(selection,files)},new Progress<string>());File.WriteAllText(Path.Combine(folder,"dxgi.dll"),"another mod");Reject(()=>OptiScalerManager.Remove(game,exe));Check(File.ReadAllText(Path.Combine(folder,"dxgi.dll"))=="another mod","Foreign DLL changed");
            string archive=Path.Combine(folder,"fake.7z");File.WriteAllText(archive,"not upstream");Reject(()=>OptiScalerManager.Extract(archive,Path.Combine(folder,"stage"),default));
            if(ui){using var form=new RtxOptionsForm(game,"NVIDIA GeForce RTX 3060 Ti");form.Show();Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,form.ClientRectangle);Directory.CreateDirectory("artifacts/optiscaler");bitmap.Save("artifacts/optiscaler/rtx30-panel.png");}
        }finally{Directory.Delete(folder,true);}
        Console.WriteLine($"PASS: {checks} RTX checks: GPU generations, six FG modes/four presets, section-safe INI, x64/consent/API guards, nested anti-cheat, journaled install/remove, config preservation and foreign-file protection. Fixtures only.");
    }
    internal static void VerifyPackage()
    {
        string root=Path.GetFullPath("artifacts/optiscaler/managed-"+Guid.NewGuid().ToString("N"));var files=OptiScalerManager.Extract(Path.GetFullPath("artifacts/optiscaler/OptiScaler.7z"),root,default);
        foreach(var file in files){string reference=Path.Combine("artifacts/optiscaler/inspect",file.Key);Check(DlssManager.Hash(file.Value)==DlssManager.Hash(reference),"Managed extractor changed upstream file: "+file.Key);}
        foreach(var mode in Enum.GetValues<RtxFrameMode>())foreach(var preset in Enum.GetValues<RtxPreset>())Check(OptiScalerManager.Configure(File.ReadAllText(files["OptiScaler.ini"]),mode,preset).Contains("[FrameGen]"),"Upstream INI incompatible");
        Check(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Any(p=>p.EndsWith(".bat")||p.EndsWith(".exe")||p.EndsWith(".sh")),"Installer script extracted");
        Console.WriteLine($"PASS: pinned upstream OptiScaler SHA-256; {files.Count} files match independent 7-Zip extraction; all 24 profiles accepted by upstream INI schema. No game code executed. {root}");
    }
    internal static async Task VerifyInstall()
    {
        string root=Path.GetFullPath("artifacts/optiscaler/install-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string library=Path.Combine(root,"library");new LauncherPreferences{OfflineMode=true,OnboardingCompleted=true}.Save(library);Workspace.Use(library);
        string cache=Path.Combine(root,"tool-cache");Directory.CreateDirectory(cache);File.Copy("artifacts/optiscaler/OptiScaler.7z",Path.Combine(cache,"OptiScaler-0.9.4.7z"));
        var sr=DlssLibrary.All.Single(l=>l.Tag=="v310.5.3");string runtime=Path.GetFullPath("artifacts/optiscaler/v310.5.3-nvngx_dlss.dll");
        string transfer=Path.Combine(library,"transfer-cache");Directory.CreateDirectory(transfer);string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sr.Url)));File.Copy(runtime,Path.Combine(transfer,key+".bin"));
        File.WriteAllText(Path.Combine(transfer,"index.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string,object>{{key,new{Hash=DlssManager.Hash(runtime),Size=new FileInfo(runtime).Length}}}));
        string gameRoot=Path.Combine(root,"game");Directory.CreateDirectory(gameRoot);string exe=Path.Combine(gameRoot,"Fixture.exe");File.Copy(Environment.ProcessPath!,exe);string original=Path.Combine(gameRoot,"nvngx_dlss.dll");File.WriteAllText(original,"original library fixture");
        var game=new GameInfo{Name="Fixture",ExePath=exe,InstallDirectory=gameRoot};
        await new OptiScalerManager(cache).Install(game,exe,"NVIDIA GeForce RTX 3060",true,true,RtxFrameMode.Upscaler,RtxPreset.M,new Progress<string>(),default);
        Check(OptiScalerManager.Installed(exe),"Real package not tracked");string config=File.ReadAllText(Path.Combine(gameRoot,"OptiScaler.ini"));
        Check(config.Contains("FGInput=upscaler")&&config.Contains("FGOutput=fsrfg")&&config.Contains("RenderPresetForAll=13"),"Wrong real config");
        string managed=Path.Combine(gameRoot,"PlaySpace.OptiScaler","nvngx_dlss.dll");Check(config.Contains("NvngxDlssPath="+managed)&&DlssManager.Hash(managed)==DlssManager.Hash(runtime),"Signed SR runtime not mapped");
        Check(File.ReadAllText(original)=="original library fixture","Game SR library overwritten");
        Check(File.Exists(Path.Combine(gameRoot,"PlaySpace.OptiScaler","OptiScaler-LICENSE.txt"))&&File.Exists(Path.Combine(gameRoot,"PlaySpace.OptiScaler","NVIDIA-DLSS-LICENSE.txt")),"Missing licenses");
        File.AppendAllText(Path.Combine(gameRoot,"OptiScaler.ini"),"\r\n; user change");File.WriteAllText(Path.Combine(gameRoot,"EasyAntiCheat.dll"),"fixture");OptiScalerManager.Remove(game,exe);
        Check(!OptiScalerManager.Installed(exe)&&!File.Exists(managed)&&!File.Exists(Path.Combine(gameRoot,"dxgi.dll")),"Real files not removed");Check(File.ReadAllText(original)=="original library fixture","Original modified during remove");
        Check(Directory.GetFiles(Path.Combine(gameRoot,".playspace-mods","preserved")).Any(p=>File.ReadAllText(p).Contains("user change")),"Real config not preserved");
        Console.WriteLine("PASS: full real-package installation to a temporary fixture, NVIDIA signature verification, SR runtime/config mapping, licenses, removal after anti-cheat discovery, modified INI preservation and original file retention. No game or GPU code executed. "+root);
    }
}
