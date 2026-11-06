namespace GameLauncherGUI;

public partial class Form1
{
    private readonly GameModeState gameMode=new();
    private void OpenGameCare(GameInfo game)
    {
        if(launchingGame)return;
        try{
            if(game.Care==null){game.Care=new();try{SaveData();}catch{game.Care=null;throw;}}
            using var form=new GameCareForm(game,appDataFolder,(options,arguments,path)=>{
                var previous=game.Care;string oldArgs=game.LaunchArguments,oldPath=game.ExePath;string? oldFolder=game.InstallDirectory;
                try{
                    game.Care=new(){Id=options.Id,SaveDirectory=options.SaveDirectory,Notes=options.Notes,BackupBeforeLaunch=options.BackupBeforeLaunch};game.LaunchArguments=arguments;
                    if(!path.Equals(game.ExePath,StringComparison.OrdinalIgnoreCase)){if(IsGameRunning(game))throw new IOException("Перед исправлением пути полностью закройте игру.");if(!File.Exists(path)||!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Выбранный EXE не найден.");game.ExePath=Path.GetFullPath(path);game.InstallDirectory=Path.GetDirectoryName(game.ExePath);}
                    SaveData();RefreshList();
                    Workspace.For(appDataFolder).History.Add(game,"Настройки","Обновлены параметры запуска, заметки или папка сохранений");
                }catch{game.Care=previous;game.LaunchArguments=oldArgs;game.ExePath=oldPath;game.InstallDirectory=oldFolder;throw;}
            },()=>IsGameRunning(game));form.ShowDialog(this);
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException){lblStatus.Text="Не удалось открыть управление игрой: "+ex.Message;}
    }
    private void SaveGameMode(bool hide,bool restore)
    {
        var next=preferences.Copy();next.HideWhilePlaying=hide;next.RestoreAfterPlaying=restore;next.Save(appDataFolder);preferences=next;
    }
    private void EnterGameMode(GameInfo game){if(!preferences.HideWhilePlaying)return;gameMode.Arm(game.ExePath);Hide();}
    private void UpdateGameMode(string[] paths)
    {
        bool finished=gameMode.Observe(myGames.Where(g=>MatchesGame(g,paths)).Select(g=>g.ExePath),sessions.Keys.Select(g=>g.ExePath));
        if(finished&&preferences.RestoreAfterPlaying&&!Visible&&!IsDisposed)RestoreWindow();
    }
}
