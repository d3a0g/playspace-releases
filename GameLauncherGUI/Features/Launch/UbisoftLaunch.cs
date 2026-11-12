using System.Diagnostics;
using Microsoft.Win32;

namespace GameLauncherGUI;

internal static class UbisoftLaunch
{
    // Conservative defaults; a user can enable this for any other game in its menu.
    private static readonly HashSet<string> SteamGames = new(StringComparer.Ordinal)
    { "359550", "623990", "48190", "33230", "201870", "208480", "242050", "289650", "368500", "582160", "812140", "2208920", "3035570", "220240", "298110", "552520", "939960", "2369390", "243470", "447040", "2239550", "460930", "2231380", "365590", "2221490", "916440", "375910", "48240" };
    internal static bool Recognized(GameInfo game) => SteamGames.Contains(game.SteamAppId??"") ||
        (game.ExePath??"").StartsWith("uplay://",StringComparison.OrdinalIgnoreCase) ||
        (game.ExePath??"").Replace('\\','/').Contains("/Ubisoft Game Launcher/games/",StringComparison.OrdinalIgnoreCase);
    internal static bool NeedsCheck(GameInfo game) => game.CheckUbisoftBeforeLaunch ?? Recognized(game);

    internal static void OpenClient()
    {
        string? installed = null;
        foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})
        {
            using var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
            using var key=root.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher");
            string? directory=key?.GetValue("InstallDir") as string;
            if(!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory,"UbisoftConnect.exe")))
            {installed=Path.Combine(directory,"UbisoftConnect.exe");break;}
        }
        // The registered official protocol is the fallback; never send credentials or login flags.
        Process.Start(new ProcessStartInfo { FileName=installed??"uplay://",UseShellExecute=true });
    }
}

internal sealed class UbisoftLaunchForm : StudioDialog
{
    private readonly Label status;
    private readonly Action openClient;
    internal UbisoftLaunchForm(string game,Action? openClient=null)
    {
        this.openClient=openClient??UbisoftLaunch.OpenClient;
        Text="PLAYSPACE / Ubisoft Connect";Size=new Size(660,430);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(25),ColumnCount=1,RowCount=5};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{46,120,48,55,55})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.Controls.Add(new SmoothLabel{Text="Перед запуском: "+game,Dock=DockStyle.Fill,AutoEllipsis=true,Font=new Font("Segoe UI",16,FontStyle.Bold)});
        layout.Controls.Add(new SmoothLabel{Text="1. Открой Ubisoft Connect и проверь нужный аккаунт.\n2. Если сессия завершилась, войди и включи «Запомнить меня».\n3. Вернись сюда и запусти игру.\n\nPlaySpace не может проверить вход или восстановить сессию Ubisoft автоматически.",Dock=DockStyle.Fill});
        status=new SmoothLabel{Dock=DockStyle.Fill,ForeColor=Color.FromArgb(176,185,210),Text="Проверку можно отключить в меню игры правой кнопкой мыши."};layout.Controls.Add(status);
        var open=new ModernButton("Открыть Ubisoft Connect ↗",Color.FromArgb(45,55,80)){Dock=DockStyle.Fill,Margin=new Padding(0,3,0,6)};
        open.Click+=(s,e)=>OpenClient();layout.Controls.Add(open);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var launch=new ModernButton("Вошёл · запустить игру",VisualTheme.Accent){Size=new Size(260,42)};
        launch.Click+=(s,e)=>{DialogResult=DialogResult.OK;Close();};
        var cancel=new ModernButton("Отмена",Color.FromArgb(36,39,57)){Size=new Size(130,42)};
        cancel.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};
        buttons.Controls.Add(launch);buttons.Controls.Add(cancel);layout.Controls.Add(buttons);ContentHost.Controls.Add(layout);
        Shown+=(s,e)=>OpenClient();
    }
    private void OpenClient()
    {
        try{openClient();status.Text="Ubisoft Connect открыт. После входа нажми «Вошёл · запустить игру».";}
        catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {status.Text="Не удалось открыть Ubisoft Connect. Открой его вручную через меню «Пуск».";}
    }
}
