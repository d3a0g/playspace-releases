namespace GameLauncherGUI;

internal sealed class StartupSplash : Form
{
    private readonly System.Windows.Forms.Timer timer=new(){Interval=16};private float angle;private string action="Запускаем LauncherPlaySpace…";
    private readonly string heading;
    public StartupSplash(string heading="LauncherPlaySpace"){this.heading=heading;FormBorderStyle=FormBorderStyle.None;Size=new Size(540,340);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(14,17,27);DoubleBuffered=true;ShowInTaskbar=true;Text="LauncherPlaySpace";timer.Tick+=(s,e)=>{angle=(angle+5)%360;Invalidate();};timer.Start();}
    public void Report(string value){action=value;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using var track=new Pen(Color.FromArgb(37,42,64),5);using var arc=new Pen(Color.FromArgb(157,130,255),5){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};g.DrawEllipse(track,Width/2-31,Height/2-45,62,62);g.DrawArc(arc,new Rectangle(Width/2-31,Height/2-45,62,62),angle,105);using var title=new Font("Segoe UI",20,FontStyle.Bold);using var sub=new Font("Segoe UI",11);SmoothText.Draw(g,heading,title,new Rectangle(20,40,Width-40,45),Color.White,TextFormatFlags.HorizontalCenter);SmoothText.Draw(g,action,sub,new Rectangle(20,Height-95,Width-40,48),Color.FromArgb(175,185,212),TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
internal sealed class LauncherApplicationContext : ApplicationContext
{
    public LauncherApplicationContext()
    {
        var splash=new StartupSplash();var lifetime=new CancellationTokenSource();var token=lifetime.Token;splash.Disposed+=(s,e)=>{lifetime.Cancel();lifetime.Dispose();};MainForm=splash;splash.Shown+=async(s,e)=> {
            try {
                splash.Report("Подготавливаем хранилище…");
                var library=await Task.Run(()=>{string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncherPRO");Directory.CreateDirectory(folder);return GameLibraryStore.Load(Path.Combine(folder,"games.json"));});
                if(token.IsCancellationRequested||splash.IsDisposed)return;splash.Report("Создаём библиотеку и интерфейс…");await Task.Delay(60,token);
                var launcher=new Form1(null,!LauncherSession.SafeMode,library);splash.Report("Готово — открываем библиотеку");await Task.Delay(80);if(token.IsCancellationRequested||splash.IsDisposed){launcher.Dispose();return;}MainForm=launcher;launcher.Show();splash.Close();
            }catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception ex){if(splash.IsDisposed)return;LauncherSession.StartupFailed=true;LauncherSession.Record(LauncherSession.DataFolder,"Ошибка подготовки библиотеки",ex);MessageBox.Show("Не удалось подготовить библиотеку. Запустите лаунчер с параметром --safe-mode и создайте отчёт для поддержки.","Запуск",MessageBoxButtons.OK,MessageBoxIcon.Error);splash.Close();ExitThread();}
        };splash.Show();
    }
}
