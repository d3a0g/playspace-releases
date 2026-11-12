namespace GameLauncherGUI;

public partial class Form1
{
    private readonly LaunchAttemptTracker launchAttempts=new();
    private LaunchIssue? lastLaunchIssue;
    private bool launchBalloonShown;
    private LaunchHelpForm? launchHelpWindow;
    private void OpenGameUpdates(){using var window=new GameUpdatesForm(myGames.ToArray(),appDataFolder,SelectedGame?.LibraryId);window.ShowDialog(this);}
    private void RecordLaunchIssue(LaunchIssue issue)
    {
        lastLaunchIssue=issue;
        try{LaunchIssues.Add(appDataFolder,issue);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){LauncherSession.Record(appDataFolder,"Сохранение помощи при запуске",ex);}
        Workspace.For(appDataFolder).History.Add(myGames.FirstOrDefault(g=>g.LibraryId==issue.GameId)??new GameInfo{Name=issue.GameName,LibraryId=issue.GameId,ExePath="",SteamAppId=""},"Запуски","Есть сведения для помощника запуска: "+issue.Kind);
        lblStatus.Text="Нужна помощь с запуском «"+issue.GameName+"»? Откройте «Помощь с запуском» в меню игры.";
        var settings=LauncherPreferences.Load(appDataFolder);
        if(!settings.LaunchHelpEnabled)return;
        if(Visible&&WindowState!=FormWindowState.Minimized)ShowLaunchHelp(issue);
        else if(settings.NotificationsEnabled&&tray!=null){launchBalloonShown=true;tray.ShowBalloonTip(6000,"PLAYSPACE · Помощь с запуском",issue.GameName+": есть сведения для проверки запуска. Нажмите, чтобы открыть помощник.",ToolTipIcon.Info);}
    }
    private void OpenLastLaunchHelp()
    {
        string? id=SelectedGame?.LibraryId;
        var issue=LaunchIssues.Load(appDataFolder).LastOrDefault(i=>id==null||i.GameId==id);
        if(issue==null){if(SelectedGame is not GameInfo game){lblStatus.Text="Сначала выберите игру.";return;}issue=new(game.LibraryId,game.Name,DateTimeOffset.UtcNow,LaunchIssueKind.NotObserved,"Автоматически обнаруженных проблем с этой игрой нет. Ниже доступны действия для проверки запуска.");}
        ShowLaunchHelp(issue);
    }
    private void ShowLaunchHelp(LaunchIssue issue)
    {
        if(IsDisposed||resourcesDisposed)return;
        var game=myGames.FirstOrDefault(g=>g.LibraryId==issue.GameId);if(game==null)return;
        if(launchHelpWindow is {IsDisposed:false}){launchHelpWindow.Activate();return;}
        launchHelpWindow=new LaunchHelpForm(game,issue,
            ()=>Tasks.Start("Запуск без подготовки",_=>LaunchGameAsync(game,true)),
            ()=>OpenPerformance(game),()=>OpenModsFor(game),()=>OpenGameCare(game),
            ()=>{using var window=new GameUpdatesForm(myGames.ToArray(),appDataFolder,game.LibraryId);window.ShowDialog(this);},
            ()=>{using var picker=new OpenFileDialog{Filter="Процесс игры|*.exe",Title="Выберите основной EXE игры"};if(picker.ShowDialog(this)==DialogResult.OK){game.TrackingExecutable=picker.FileName;SaveData();}},
            ()=>{var next=LauncherPreferences.Load(appDataFolder);next.LaunchHelpEnabled=false;next.Save(appDataFolder);preferences=next;});
        launchHelpWindow.Show(this);
    }
}
