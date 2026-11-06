using GameLauncherGUI;
using Newtonsoft.Json.Linq;

internal static class PreferencesChecks
{
    public static void Run(bool ui)
    {
        string folder=Path.Combine(Path.GetTempPath(),"PlayspacePreferences-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        if(LauncherPreferences.Load(folder).CloseBehavior!=CloseBehavior.Ask)throw new Exception("Default close must ask");
        new LauncherPreferences{CloseBehavior=CloseBehavior.Exit}.Save(folder);
        if(LauncherPreferences.Load(folder).CloseBehavior!=CloseBehavior.Exit)throw new Exception("Close preference not persisted");
        var steam=new GameInfo{Name="Steam fixture",SteamAppId="620",ExePath="fixture.exe",PlayedSeconds=900};var local=new GameInfo{Name="Local fixture",SteamAppId="",ExePath="local.exe",PlayedSeconds=100};
        var games=new[]{steam,local};
        int count=SteamPlaytime.Apply(games,JObject.Parse("{response:{games:[{appid:620,playtime_forever:180000}]}}"),"user-a",DateTimeOffset.UtcNow);
        if(count!=1||steam.DisplayPlayedSeconds!=3000*3600||steam.PlayedSeconds!=900||local.DisplayPlayedSeconds!=100)throw new Exception("Steam total did not replace displayed local hours");
        string library=Path.Combine(folder,"games.json");GameLibraryStore.Save(library,games.ToList());
        if(GameLibraryStore.Load(library).Games[0].SteamPlaytimeMinutes!=180000)throw new Exception("Steam hours cache didn't persist");
        SteamPlaytime.Apply(games,JObject.Parse("{response:{}}"),"user-a",DateTimeOffset.UtcNow);
        if(steam.SteamPlaytimeMinutes!=180000)throw new Exception("Private data reset cached hours");
        SteamPlaytime.Apply(games,JObject.Parse("{response:{games:[{appid:620,playtime_forever:-1}]}}"),"user-a",DateTimeOffset.UtcNow);
        if(steam.SteamPlaytimeMinutes!=180000)throw new Exception("Negative hours accepted");
        SteamPlaytime.Apply(games,JObject.Parse("{response:{games:[{appid:620,playtime_forever:0}]}}"),"user-a",DateTimeOffset.UtcNow);
        if(steam.DisplayPlayedSeconds!=0)throw new Exception("Actual Steam zero fell back to local time");
        SteamPlaytime.Clear(steam);if(steam.SteamPlaytimeMinutes!=null||steam.SteamPlaytimeUser!=null)throw new Exception("Account data not cleared");
        if(ui){
            IEnumerable<Control> Descendants(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
            using(var settings=new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{},CloseBehavior.Exit,behavior=>new LauncherPreferences{CloseBehavior=behavior}.Save(folder))){
                settings.Show();Application.DoEvents();Descendants(settings).OfType<ModernButton>().Single(button=>button.Text=="Приложение").PerformClick();
                var choice=Descendants(settings).OfType<ModernSelect>().Single();choice.SelectedIndex=0;
                if(LauncherPreferences.Load(folder).CloseBehavior!=CloseBehavior.Ask)throw new Exception("Settings couldn't reset saved close choice");
                Directory.CreateDirectory("artifacts");using var preview=new Bitmap(settings.Width,settings.Height);settings.DrawToBitmap(preview,settings.ClientRectangle);preview.Save("artifacts/application-settings.png");
            }
            string uiFolder=Path.Combine(folder,"ui");int prompts=0;
            using(var form=new Form1(uiFolder)) {
                form.ClosePrompt=()=>{prompts++;return null;};form.Show();Application.DoEvents();form.Close();
                if(form.IsDisposed||!form.Visible||prompts!=1)throw new Exception("Cancel close prompt failed");
                form.ClosePrompt=()=>{prompts++;return(CloseBehavior.Tray,true);};form.Close();
                if(form.IsDisposed||form.Visible||LauncherPreferences.Load(uiFolder).CloseBehavior!=CloseBehavior.Tray)throw new Exception("Remember tray failed");
                form.Show();form.ClosePrompt=()=>throw new Exception("Saved tray choice asked again");form.Close();
                if(form.IsDisposed||form.Visible)throw new Exception("Saved tray choice didn't hide");
            }
            new LauncherPreferences{CloseBehavior=CloseBehavior.Exit}.Save(uiFolder);
            using(var form=new Form1(uiFolder)){form.ClosePrompt=()=>throw new Exception("Saved exit asked again");form.Show();form.Close();if(!form.IsDisposed)throw new Exception("Saved exit didn't close");}
            using(var dialog=new CloseChoiceDialog()){dialog.Show();Directory.CreateDirectory("artifacts");using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,dialog.ClientRectangle);image.Save("artifacts/close-choice.png");}
        }
        Console.WriteLine("PASS: Steam total hours, zero/private/invalid responses, local fallback, account clearing; ask/cancel/remember tray/full exit preferences. Temporary data only.");
    }
}
