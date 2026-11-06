using Newtonsoft.Json;

namespace GameLauncherGUI;

internal enum CloseBehavior { Ask, Tray, Exit }
internal sealed class LauncherPreferences
{
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Ask;
    public bool NotificationsEnabled { get; set; } = true;
    public string UpdateFeedUrl { get; set; } = "";
    public string LastNotifiedVersion { get; set; } = "";
    public bool HideWhilePlaying { get; set; }
    public bool RestoreAfterPlaying { get; set; } = true;
    public bool OfflineMode { get; set; }
    public string AccentName { get; set; } = "Фиолетовый";
    public int TextScalePercent { get; set; } = 100;
    public int PosterWidth { get; set; } = 160;
    public bool ReduceMotion { get; set; }
    public LauncherPreferences Copy() => (LauncherPreferences)MemberwiseClone();
    public static LauncherPreferences Load(string folder)
    {
        try {string file=Path.Combine(folder,"preferences.json");var value=File.Exists(file)?JsonConvert.DeserializeObject<LauncherPreferences>(File.ReadAllText(file)):null;return value!=null&&Enum.IsDefined(value.CloseBehavior)?value:new();}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);string file=Path.Combine(folder,"preferences.json"),temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,JsonConvert.SerializeObject(this));File.Move(temp,file,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
internal sealed class CloseChoiceDialog : StudioDialog
{
    public CloseBehavior Choice { get; private set; }=CloseBehavior.Ask;
    private readonly CheckBox remember=new(){Text="Запомнить выбор",AutoSize=true};
    public bool Remember=>remember.Checked;
    public CloseChoiceDialog()
    {
        Text="PLAYSPACE / Закрытие";Size=new Size(580,310);MinimumSize=Size;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28),RowCount=4,ColumnCount=1};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new Label{Text="Как закрыть PLAYSPACE?",Dock=DockStyle.Fill,Font=new Font("Segoe UI",17,FontStyle.Bold)},0,0);
        layout.Controls.Add(new Label{Text="В трее лаунчер продолжит работать в фоне. Полный выход завершит работу приложения.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(165,176,202)},0,1);layout.Controls.Add(remember,0,2);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));layout.Controls.Add(actions,0,3);
        var tray=new ModernButton("Свернуть в трей",Color.FromArgb(115,94,244)){Dock=DockStyle.Fill};var exit=new ModernButton("Выйти полностью",Color.FromArgb(40,45,65)){Dock=DockStyle.Fill,Margin=new Padding(10,3,3,3)};actions.Controls.Add(tray,0,0);actions.Controls.Add(exit,1,0);
        tray.Click+=(s,e)=>{Choice=CloseBehavior.Tray;DialogResult=DialogResult.OK;};exit.Click+=(s,e)=>{Choice=CloseBehavior.Exit;DialogResult=DialogResult.OK;};
    }
}
public partial class Form1
{
    private LauncherPreferences preferences=new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<(CloseBehavior Choice,bool Remember)?>? ClosePrompt { get; set; }
    private void HandleCloseChoice(object? sender,FormClosingEventArgs e)
    {
        if(exiting || e.CloseReason!=CloseReason.UserClosing)return;
        var behavior=preferences.CloseBehavior;
        if(behavior==CloseBehavior.Ask) {
            (CloseBehavior Choice,bool Remember)? result;
            if(ClosePrompt!=null)result=ClosePrompt();
            else {using var dialog=new CloseChoiceDialog();result=dialog.ShowDialog(this)==DialogResult.OK?(dialog.Choice,dialog.Remember):null;}
            if(result==null||result.Value.Choice==CloseBehavior.Ask){e.Cancel=true;return;}
            behavior=result.Value.Choice;
            if(result.Value.Remember) {
                try{var next=preferences.Copy();next.CloseBehavior=behavior;next.Save(appDataFolder);preferences=next;}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){lblStatus.Text="Не удалось запомнить выбор закрытия.";}
            }
        }
        if(behavior==CloseBehavior.Tray){e.Cancel=true;Hide();}else exiting=true;
    }
}
