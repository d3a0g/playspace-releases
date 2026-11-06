using System.Reflection;
using System.Text;
using GameLauncherGUI;

internal static class PerformanceChecks
{
    public static void Run(bool ui)
    {
        string root = Path.Combine(Path.GetTempPath(), "PlayspacePerformance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "GameUserSettings.ini");
        string original = "; keep comment\r\n[/Script/Engine.GameUserSettings]\r\nResolutionSizeX=1920\r\nResolutionSizeY=1080\r\nFrameRateLimit=60\r\nbUseVSync=True\r\nCustomAudioValue=0.7\r\n[Other]\r\nFrameRateLimit=999\r\n[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\n";
        File.WriteAllText(path, original, Encoding.Unicode);
        byte[] originalBytes = File.ReadAllBytes(path);
        var profile = new PerformanceProfile { TargetFps = 120, RenderScale = 75, Quality = "Низкие", ResolutionWidth = 1600,
            ResolutionHeight = 900, WindowMode = "Оконный", VSync = false, ApplyOnLaunch = true, ConfigFile = path };
        var game = new GameInfo { Name = "MECCHA CHAMELEON", SteamAppId = "4704690", ExePath = Path.Combine(root, "PenguinHotel.exe"), Performance = profile };
        GamePerformanceController.ApplyConfig(path, profile, new Size(1920,1080));
        string updated = File.ReadAllText(path);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Check(updated.Contains("FrameRateLimit=120") && updated.Contains("FrameRateLimit=999"), "Wrong INI section edited");
        Check(updated.Contains("sg.ResolutionQuality=75") && updated.Contains("sg.ShadowQuality=0") && updated.Contains("bUseVSync=False"), "Graphics profile not applied");
        Check(updated.Contains("CustomAudioValue=0.7") && updated.Contains("; keep comment") && updated.Contains("FullscreenMode=2"), "Unrelated settings lost");
        Check(File.ReadAllBytes(path+".playspace-backup").SequenceEqual(originalBytes), "Backup lost original encoding");
        profile.TargetFps=144; GamePerformanceController.ApplyConfig(path,profile,new Size(1920,1080));
        Check(File.ReadAllBytes(path+".playspace-backup").SequenceEqual(originalBytes), "Original backup overwritten");
        GamePerformanceController.RestoreConfig(path);
        Check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "Restore is not byte-exact");
        var unrealArgs=GamePerformanceController.BuildArguments(game,profile,new Size(1920,1080));
        Check(unrealArgs.Contains("-ResX=1600") && !unrealArgs.Contains("-fullscreen"), "Command line overrides INI window mode");
        var unity=new GameInfo {Name="R.E.P.O.",SteamAppId="3241660",ExePath="REPO.exe"};
        var unityArgs=GamePerformanceController.BuildArguments(unity,profile,new Size(1920,1080));
        Check(unityArgs.Contains("-screen-width") && !unityArgs.Contains("fps_max"), "Unity flags include unsupported FPS command");
        var cs2=new GameInfo {Name="Counter-Strike 2",SteamAppId="730",ExePath="cs2.exe"};
        var sourceArgs=GamePerformanceController.BuildArguments(cs2,profile,new Size(1920,1080));
        Check(sourceArgs.Contains("+fps_max") && sourceArgs.Contains("144"), "CS2 FPS cap missing");
        var steam=UnrealModLayout.SteamStart("steam.exe","4704690",new[]{"--mod-dir",Path.Combine(root,"mods")}.Concat(unrealArgs));
        Check(steam.ArgumentList.Contains("--mod-dir") && steam.ArgumentList.Contains("-ResX=1600"), "Performance arguments displaced mod profile");
        game.Performance=profile.Copy();game.Performance.ApplyOnLaunch=false;
        Check(GamePerformanceController.Prepare(game).Length==0 && File.ReadAllBytes(path).SequenceEqual(originalBytes), "Disabled profile changed config");
        bool rejected=false;try{GamePerformanceController.EditIni("[Other]\nx=1",profile,new Size(1920,1080));}catch(InvalidDataException){rejected=true;}
        Check(rejected,"Non-game INI accepted");
        Check(GamePerformanceController.Backend(new GameInfo {Name="Unknown",ExePath="unknown.exe"})==PerformanceBackend.None,"Unknown game advertised as supported");
        Console.WriteLine("PASS: Unreal config application, section preservation, original backup/encoding, restore, Unity/CS2 arguments, combined mod launch and disabled/unsupported profiles. Only temporary configs changed.");
        if(ui) Ui(game,unity,cs2,root);
    }
    private static void Ui(GameInfo game,GameInfo unity,GameInfo cs2,string root)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        PerformanceProfile? saved=null;
        using var dialog=new PerformanceForm(new[]{game,unity,cs2},game,(g,p)=>{saved=p;g.Performance=p.Copy();return true;});
        dialog.SetSnapshot(PcSnapshot.Read());
        string artifacts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../artifacts"));Directory.CreateDirectory(artifacts);
        foreach(var size in new[]{new Size(1120,840),new Size(960,720)})
        {
            dialog.Size=size;dialog.PerformLayout();
            using var image=new Bitmap(dialog.Width,dialog.Height);using(var g=Graphics.FromImage(image))ModUiChecks.Render(dialog,g);
            image.Save(Path.Combine(artifacts,"performance-studio-"+size.Width+".png"));
        }
        var type=typeof(PerformanceForm);
        void Click(string field)=>typeof(ModernButton).GetMethod("OnClick",flags)!.Invoke(type.GetField(field,flags)!.GetValue(dialog),new object[]{EventArgs.Empty});
        Click("save");if(saved==null)throw new Exception("Profile save button failed");
        var picker=(ComboBox)type.GetField("gamePicker",flags)!.GetValue(dialog)!;
        picker.SelectedIndex=1;
        if(((NumericUpDown)type.GetField("target",flags)!.GetValue(dialog)!).Enabled)throw new Exception("Unsupported Unity FPS cap enabled");
        picker.SelectedIndex=2;
        if(!((NumericUpDown)type.GetField("target",flags)!.GetValue(dialog)!).Enabled)throw new Exception("Supported CS2 cap disabled");
        picker.SelectedIndex=0;
        Click("launch");if(dialog.LaunchRequested!=game || saved?.ApplyOnLaunch!=true)throw new Exception("Apply and play lost selected profile");
        Console.WriteLine("PASS: modern performance UI at two sizes, capability controls, save and apply/play request; no games started.");
    }
}
