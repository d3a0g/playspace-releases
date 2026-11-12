using GameLauncherGUI;

internal static class UbisoftLaunchChecks
{
    internal static void Run(bool ui)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var siege=new GameInfo{Name="Rainbow Six Siege",SteamAppId="359550",ExePath="steam://rungameid/359550"};
        Check(UbisoftLaunch.NeedsCheck(siege),"Siege login preparation missing");
        siege.CheckUbisoftBeforeLaunch=false;Check(!UbisoftLaunch.NeedsCheck(siege),"Explicit Ubisoft opt-out ignored");
        var other=new GameInfo{Name="Ubisoft random unrelated file",SteamAppId="620",ExePath="C:\\Games\\Portal2.exe"};
        Check(!UbisoftLaunch.NeedsCheck(other),"Game name alone triggers Ubisoft login");
        other.CheckUbisoftBeforeLaunch=true;Check(UbisoftLaunch.NeedsCheck(other),"Manual opt-in ignored");
        Check(UbisoftLaunch.Recognized(new(){ExePath="uplay://launch/1/0"}),"Official Ubisoft launch URI not recognized");
        if(ui)
        {
            int opened=0;
            using var dialog=new UbisoftLaunchForm("Fixture game",()=>opened++);
            dialog.Show();Application.DoEvents();Check(opened==1,"Client not opened before game confirmation");
            Check(dialog.DialogResult!=DialogResult.OK,"Game launched without login confirmation");
            dialog.Close();Check(dialog.DialogResult!=DialogResult.OK,"Closing login dialog accepts launch");
            using var progress=new StartupSplash("Подготавливаем запуск");progress.Report("Ожидание загрузки Steam…");progress.Show();Application.DoEvents();
            using var bitmap=new Bitmap(progress.Width,progress.Height);progress.DrawToBitmap(bitmap,progress.ClientRectangle);progress.Close();
        }
        Console.WriteLine("PASS: Ubisoft recognition, opt-in/out, confirmation/cancel flow and Steam progress rendering. No real client opened or credentials accessed.");
    }
}
