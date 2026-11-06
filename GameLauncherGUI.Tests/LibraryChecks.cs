using System.Reflection;
using GameLauncherGUI;

internal static class LibraryChecks
{
    public static void Run(bool ui)
    {
        string root=Path.Combine(Path.GetTempPath(),"PlayspaceLibraryChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string process=Environment.ProcessPath!;
        var game=new GameInfo { Name="Fixture", ExePath=process, SteamAppId="", PlayedSeconds=54000, CustomIconPath="fixture.png",TrackingExecutable=process,AchievementSummary="5/10 · 50%" };
                string iconPath=Path.Combine(root,"icon.png");using(var icon=new Bitmap(128,128)){using(var graphics=Graphics.FromImage(icon))graphics.Clear(Color.Lime);icon.Save(iconPath);}
        game.CustomIconPath=iconPath;using(var cache=new GameIconCache()){var icon=cache.Get(game);if(icon==null||icon.GetPixel(20,20).ToArgb()!=Color.Lime.ToArgb())throw new Exception("Custom icon image wasn't used");}
        string data=Path.Combine(root,"games.json"); GameLibraryStore.Save(data,new(){game});
        var read=GameLibraryStore.Load(data).Games.Single();
        if(read.PlayedSeconds!=54000 || read.CustomIconPath!=game.CustomIconPath || read.TrackingExecutable!=process || read.AchievementSummary!=game.AchievementSummary)throw new Exception("Library personalization/statistics persistence failed");
        var running=typeof(Form1).GetMethod("IsGameRunning",BindingFlags.Static|BindingFlags.NonPublic)!;
        if(!(bool)running.Invoke(null,new object[]{game})!)throw new Exception("Actual game process path detection failed");
        game.TrackingExecutable=Path.Combine(root,"missing.exe");
        if((bool)running.Invoke(null,new object[]{game})!)throw new Exception("Missing process counted as playing");
        if(ui) {
            using var grid=new CustomCheckListBox { Size=new Size(650,330),PosterView=true,ShowPinButtons=true,ShowProfileButtons=true };
            using var poster=new Bitmap(300,450);using(var g=Graphics.FromImage(poster))g.Clear(Color.MediumPurple);
            for(int i=0;i<25;i++)grid.Items.Add(new CustomListItem("Fixture "+i,false,"15 ч · 50%") { PosterImage=poster });
            typeof(CustomCheckListBox).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(grid,new object[]{new MouseEventArgs(MouseButtons.Left,1,250,70,0)});if(grid.SelectedIndex!=1)throw new Exception("Poster second-column hit test failed");
            typeof(CustomCheckListBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(grid,new object[]{new KeyEventArgs(Keys.Down)});if(grid.SelectedIndex!=4)throw new Exception("Poster row keyboard navigation failed");
            grid.SelectItem(24);
            using var image=new Bitmap(grid.Width,grid.Height);grid.DrawToBitmap(image,grid.ClientRectangle);
            grid.Size=new Size(340,330);grid.SelectItem(24);using var narrow=new Bitmap(grid.Width,grid.Height);grid.DrawToBitmap(narrow,grid.ClientRectangle);
            grid.PosterView=false;grid.SelectItem(20);grid.DrawToBitmap(narrow,grid.ClientRectangle);
            using var form=new Form1(Path.Combine(root,"ui"));form.ClosePrompt=()=>(CloseBehavior.Tray,false);form.Show();form.Close();
            if(form.IsDisposed || form.Visible)throw new Exception("Closing launcher didn't hide to tray");
            Directory.CreateDirectory("artifacts");image.Save("artifacts/poster-grid.png");
        }
        Console.WriteLine("PASS: library stats/icon persistence, real process path detection, poster hit testing/navigation/resizing and close-to-tray. No games launched.");
    }
}
