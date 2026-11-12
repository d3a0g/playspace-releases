using System.Diagnostics;
using System.Reflection;
using GameLauncherGUI;

internal static class DesktopLibraryChecks
{
    private static int checks;
    // Retain the runner UI context; disposing it destroys the thread-wide WinForms marshaling window.
    private static WindowsFormsSynchronizationContext? runnerContext;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    private static IEnumerable<Control> Children(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    private static void PumpUntil(Func<bool> condition){var context=SynchronizationContext.Current;var watch=Stopwatch.StartNew();while(!condition()&&watch.ElapsedMilliseconds<6000){Application.DoEvents();SynchronizationContext.SetSynchronizationContext(context);Thread.Sleep(10);}Check(condition(),"Local artwork did not finish loading");}
    internal static void Run()
    {
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceDesktop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,"wide-posters"));Directory.CreateDirectory(Path.Combine(folder,"posters"));
        new LauncherPreferences{OfflineMode=true,OnboardingCompleted=true,HomePosters=false,DesktopLayoutVersion=0}.Save(folder);
        var names=new[]{"Northwind","Echoes","Deep Forest","Solstice","Blue Horizon","Last Signal","Quiet Valley","New Dawn","Nightfall","Lost Islands","Starlight","Afterglow","Ocean Drive","Skybound","Wildlands","Crossroads","Outpost","Daybreak"};var games=new List<GameInfo>();
        for(int i=0;i<names.Length;i++)
        {
            string id=(9100000+i).ToString();var color=Color.FromArgb(30+i*5,65+i*3,95+i*4);
            using var image=new Bitmap(460,215);using(var g=Graphics.FromImage(image)){g.Clear(color);using var brush=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0,0,460,215),color,Color.FromArgb(17,25,42),30);g.FillRectangle(brush,0,0,460,215);using var pen=new Pen(Color.FromArgb(65,255,255,255),2);for(int line=0;line<6;line++)g.DrawLine(pen,60+line*70,0,180+line*70,215);using var text=new Font("Segoe UI",25,FontStyle.Bold);g.DrawString(names[i],text,Brushes.White,20,145);}image.Save(Path.Combine(folder,"wide-posters",id+".jpg"));
            using var vertical=new Bitmap(300,450);vertical.Save(Path.Combine(folder,"posters",id+".jpg"));games.Add(new(){Name=names[i],SteamAppId=id,ExePath=Path.Combine(folder,"missing-"+i+".exe"),PlayedSeconds=i*3600});
        }
        GameLibraryStore.Save(Path.Combine(folder,"games.json"),games);
        using(var wide=PosterLoader.LoadAsync(folder,games[0].SteamAppId,true,default,true).GetAwaiter().GetResult())using(var tall=PosterLoader.LoadAsync(folder,games[0].SteamAppId,true,default).GetAwaiter().GetResult()){Check(wide?.Width==460&&wide.Height==215,"Wide cache aspect lost");Check(tall?.Width==300&&tall.Height==450,"Portrait cache was replaced");}
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        string output=Path.GetFullPath("artifacts/playnite-layout-previews");Directory.CreateDirectory(output);
        var previousContext=SynchronizationContext.Current;var context=runnerContext??=new WindowsFormsSynchronizationContext();SynchronizationContext.SetSynchronizationContext(context);
        using(var form=new Form1(folder,false))
        {
            form.Show();Application.DoEvents();SynchronizationContext.SetSynchronizationContext(context);var grid=(CustomCheckListBox)typeof(Form1).GetField("listBoxGames",flags)!.GetValue(form)!;
            Check(grid.PosterView&&grid.LandscapePosters&&grid.CompactRows,"Desktop views were not configured");Check(LauncherPreferences.Load(folder).DesktopLayoutVersion==1,"Layout migration not saved");
            PumpUntil(()=>grid.Items.Count==18&&grid.Items.All(i=>i.PosterImage!=null));Check(!form.Controls.OfType<Form>().Any(),"Desktop opens a detached game card");
            var chrome=form.Controls.OfType<Panel>().Single(p=>p.Name=="WindowChrome");Check(chrome.Height==44&&Children(chrome).OfType<TextBox>().Any(t=>t.PlaceholderText.Contains("Найти игру")),"Search is not integrated into title bar");
            foreach(string action in new[]{"Свернуть","Развернуть","Закрыть"}){var label=Children(chrome).Single(c=>c.AccessibleName==action);Check(label.Visible&&chrome.RectangleToScreen(chrome.ClientRectangle).Contains(label.RectangleToScreen(label.ClientRectangle)),"Window action hidden or clipped: "+action);}
            Check(Children(form).OfType<ModernButton>().Count(b=>b.IconOnly)>=10,"Icon navigation missing");
            var source=(ModernSelect)typeof(Form1).GetField("librarySource",flags)!.GetValue(form)!;source.SelectedIndex=5;Check(grid.Items.Count==0,"Source filter doesn't filter");source.SelectedIndex=0;PumpUntil(()=>grid.Items.Count==18&&grid.Items.All(i=>i.PosterImage!=null));
            grid.SelectItem(0);using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(output,"grid.png"));}
            var toggle=Children(form).OfType<ModernButton>().Single(b=>b.AccessibleName=="Показать или скрыть фильтры");toggle.PerformClick();Check(!source.Parent!.Visible,"Filters cannot be hidden");toggle.PerformClick();Check(source.Parent.Visible,"Filters cannot be restored");
            Children(form).OfType<ModernButton>().Single(b=>b.AccessibleName=="Компактный список").PerformClick();Check(!grid.PosterView&&!LauncherPreferences.Load(folder).HomePosters,"List selection doesn't persist");Check(grid.AccessibilityObject.GetChild(0)!.Bounds.Height<=44,"List remains card-sized");
            using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(output,"list.png"));}
            form.Size=form.MinimumSize;Application.DoEvents();SynchronizationContext.SetSynchronizationContext(context);Check(grid.Width>300&&grid.Height>200,"Library collapses at minimum size");var scope=(UiTaskScope)typeof(Form1).GetProperty("Tasks",flags)!.GetValue(form)!;PumpUntil(()=>scope.ActiveCount==0);form.Dispose();
        }
        using(var form=new Form1(folder,false)){var grid=(CustomCheckListBox)typeof(Form1).GetField("listBoxGames",flags)!.GetValue(form)!;Check(!grid.PosterView,"Migration overrides saved view on every launch");var scope=(UiTaskScope)typeof(Form1).GetProperty("Tasks",flags)!.GetValue(form)!;PumpUntil(()=>scope.ActiveCount==0);}
        using(var hero=new LibraryHero{Compact=true,Size=new(500,64)}){int launched=0;hero.PlayRequested+=(s,e)=>launched++;hero.SetPresentation("Fixture","Stats",null,true);typeof(LibraryHero).GetMethod("OnMouseUp",flags)!.Invoke(hero,new object[]{new MouseEventArgs(MouseButtons.Left,1,400,30,0)});Check(launched==0&&!hero.TabStop,"Compact summary has hidden launch hit area");}
        SynchronizationContext.SetSynchronizationContext(previousContext);
        Console.WriteLine($"PASS: {checks} desktop library checks: title-bar search, icon rail, landscape cache isolation, unreadable-icon fallback, source filters, grid/list persistence, minimum size and no hidden game-card launch. Offline fixtures only.");
    }
}
