namespace GameLauncherGUI;

public partial class Form1
{
    private void OpenDuplicates()
    {
        using var form=new DuplicateGamesForm(()=>LibraryDuplicates.Find(myGames),(group,keep)=>
        {
            var combined=LibraryDuplicates.Merge(keep,group.Games);
            var next=myGames.Where(g=>!group.Games.Contains(g)||ReferenceEquals(g,keep)).Select(g=>ReferenceEquals(g,keep)?combined:g).ToList();
            string backup=Path.Combine(appDataFolder,"library-backups","before-merge-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json");
            GameLibraryStore.Save(backup,myGames);
            Workspace.For(appDataFolder).Collections.MergeMembership(keep.LibraryId,group.Games.Select(g=>g.LibraryId));
            GameLibraryStore.Save(dataFile,next);myGames=next;RefreshList();
        });form.ShowDialog(this);
    }
    private void OpenGameTransfer()
    {
        if(SelectedGame is not GameInfo game){lblStatus.Text="Сначала выберите игру для переноса.";return;}
        if(LauncherSession.SafeMode){lblStatus.Text="Перенос недоступен в безопасном режиме.";return;}
        using var dialog=new GameTransferForm(game,appDataFolder,(journal,moved)=>
        {
            string from=moved?journal.Source:journal.Target,to=moved?journal.Target:journal.Source;
            string Change(string path)
            {
                if(string.IsNullOrWhiteSpace(path))return path;
                string full=Path.GetFullPath(path);
                if(full.Equals(from,StringComparison.OrdinalIgnoreCase))return to;
                return full.StartsWith(from+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?Path.Combine(to,Path.GetRelativePath(from,full)):path;
            }
            foreach(var entry in myGames){entry.ExePath=Change(entry.ExePath);entry.TrackingExecutable=Change(entry.TrackingExecutable);if(entry.InstallDirectory!=null)entry.InstallDirectory=Change(entry.InstallDirectory);}
            SaveData();
        });dialog.ShowDialog(this);diskSizes.Clear();RefreshList();
    }
}
