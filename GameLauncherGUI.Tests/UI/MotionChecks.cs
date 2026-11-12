using System.Reflection;
using System.Diagnostics;
using GameLauncherGUI;

internal static class MotionChecks
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Pump(int milliseconds){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}}
    private static Bitmap Render(Control control){var image=new Bitmap(control.Width,control.Height);control.DrawToBitmap(image,control.ClientRectangle);return image;}
    internal static void Run()
    {
        Check(UiMotion.Ease(0)==0&&UiMotion.Ease(1)==1,"Animation endpoints wrong");Check(UiMotion.Ease(-1)==0&&UiMotion.Ease(2)==1,"Animation progress not clamped");float previous=0;for(int i=0;i<=100;i++){float value=UiMotion.Ease(i/100F);Check(value>=previous&&value<=1,"Animation overshoots or reverses");previous=value;}
        PresentationOptions.Configure(new());
        using(var hero=new LibraryHero{Size=new(800,150)})
        {
            using var icon=new Bitmap(32,32);hero.SetPresentation("Fixture game","Steam · 15 ч",icon,true);
            var art=new Bitmap(800,150);using(var graphics=Graphics.FromImage(art))graphics.Clear(Color.Coral);hero.SetArtwork(art);
            using var before=Render(hero);Pump(420);using var after=Render(hero);
            Check(before.GetPixel(500,70).ToArgb()!=after.GetPixel(500,70).ToArgb(),"Artwork does not fade in");
            var timer=(System.Windows.Forms.Timer)typeof(LibraryHero).GetField("animation",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(hero)!;Check(!timer.Enabled,"Hero animation never becomes idle");
            hero.Dispose();Check(icon.Width==32,"Borrowed icon disposed by animation");
            bool disposed=false;try{art.GetPixel(0,0);}catch(ArgumentException){disposed=true;}Check(disposed,"Owned artwork leaked after close");
        }
        PresentationOptions.Configure(new(){ReduceMotion=true});
        using(var hero=new LibraryHero{Size=new(800,150)})
        {
            var art=new Bitmap(800,150);using(var g=Graphics.FromImage(art))g.Clear(Color.Coral);hero.SetArtwork(art);using var first=Render(hero);Pump(60);using var second=Render(hero);Check(first.GetPixel(500,70).ToArgb()==second.GetPixel(500,70).ToArgb(),"Reduced motion still fades artwork");
        }
        using(var form=new StudioDialog()){form.Show();UiMotion.Enter(form);Check(form.Opacity==1,"Reduced motion still fades windows");form.Close();}
        PresentationOptions.Configure(new());
        using(var button=new ModernButton("Fixture",Color.FromArgb(31,36,53)){Size=new(180,44),IsNavigation=true})
        {
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;button.IsActive=true;Pump(450);var timer=(System.Windows.Forms.Timer)typeof(ModernButton).GetField("animation",flags)!.GetValue(button)!;Check(!timer.Enabled,"Button animation does not stop when idle");button.IsActive=false;Pump(450);Check(!timer.Enabled,"Selection fade-out does not settle");
        }
        using(var form=new StudioDialog()){form.Show();Pump(350);Check(form.Opacity==1,"Window entrance never settles to full opacity");form.Close();}
        using(var form=new StudioDialog()){form.Show();form.Dispose();Pump(50);checks++;}
        using(var grid=new CustomCheckListBox{Size=new(1000,620),PosterView=true,ShowPinButtons=true})
        {
            using var art=new Bitmap(320,480);using(var graphics=Graphics.FromImage(art)){graphics.Clear(Color.FromArgb(42,95,125));using var pen=new Pen(Color.FromArgb(74,143,158),30);graphics.DrawEllipse(pen,60,80,280,280);}
            for(int i=0;i<20;i++)grid.Items.Add(new("Game "+i,false,"Steam · 12 ч"){PosterImage=art});grid.SelectItem(2);int selected=grid.SelectedIndex,offset=grid.ViewportOffset;
            typeof(CustomCheckListBox).GetMethod("OnMouseMove",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(grid,new object[]{new MouseEventArgs(MouseButtons.None,0,250,70,0)});Pump(350);using var image=Render(grid);Check(grid.SelectedIndex==selected&&grid.ViewportOffset==offset,"Poster animation changes selection or scroll");
            string output=Path.GetFullPath("artifacts/motion-refresh-previews");Directory.CreateDirectory(output);image.Save(Path.Combine(output,"posters.png"));
        }
        PresentationOptions.Configure(new());Console.WriteLine($"PASS: {checks} motion checks: easing, artwork appearance, resource ownership, reduced motion, idle timers, window close and stable poster interaction. Offline fixtures only.");
    }
}
