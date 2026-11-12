using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record UpdateJob(string Installer, string Hash, string Target, int ParentId, long ParentStarted, string Version);
internal static class LauncherUpdater
{
    internal static void Cleanup(string folder)
    {
        try
        {
            folder=Path.GetFullPath(folder);
            if(!Path.GetDirectoryName(folder)!.Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("PlaySpaceUpdate-",StringComparison.Ordinal))return;
            var job=JsonConvert.DeserializeObject<UpdateJob>(File.ReadAllText(Path.Combine(folder,"job.json")));
            if(job==null||!string.Equals(job.Target,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))return;
            // The helper is exiting; wait briefly for Windows to release its executable.
            for(int attempt=0;attempt<15;attempt++)
            {
                try{Directory.Delete(folder,true);return;}catch(IOException){Thread.Sleep(100);}
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){}
    }
    internal static bool ValidHash(string? hash)=>hash?.Length==64&&hash.All(Uri.IsHexDigit);
    internal static async Task Verify(string file,string hash,CancellationToken token)
    {
        if(!ValidHash(hash))throw new InvalidDataException("В канале обновлений отсутствует SHA-256 установщика. Дождитесь нового выпуска.");
        await using var input=File.OpenRead(file);
        var actual=Convert.ToHexString(await SHA256.HashDataAsync(input,token));
        if(!actual.Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Проверка обновления не пройдена: SHA-256 не совпадает. Попробуйте загрузить заново.");
        input.Position=0;
        if(input.ReadByte()!='M'||input.ReadByte()!='Z')throw new InvalidDataException("Загруженный файл не является установщиком Windows.");
    }
    internal static async Task Download(HttpClient client,UpdateRelease release,string destination,IProgress<(long Done,long? Total)> progress,CancellationToken token)
    {
        if(!release.HasInstaller||!ValidHash(release.InstallerSha256))throw new InvalidDataException("Этот выпуск ещё не поддерживает встроенную установку с проверкой SHA-256. Дождитесь обновления канала.");
        try
        {
            using var response=await client.GetAsync(release.DownloadUri,HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Небезопасное перенаправление загрузки.");
            long? total=response.Content.Headers.ContentLength;
            const long limit=1024L*1024*1024;
            if(total>limit)throw new InvalidDataException("Установщик превышает допустимый размер.");
            await using(var input=await response.Content.ReadAsStreamAsync(token))
            await using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                byte[] buffer=new byte[65536];long done=0;int read;
                var report=Stopwatch.StartNew();
                while((read=await input.ReadAsync(buffer,token))>0)
                {
                    done+=read;if(done>limit)throw new InvalidDataException("Установщик превышает допустимый размер.");
                    await output.WriteAsync(buffer.AsMemory(0,read),token);
                    if(report.ElapsedMilliseconds>100){progress.Report((done,total));report.Restart();}
                }
                if(total.HasValue&&done!=total.Value)throw new IOException("Загрузка оборвалась. Повторите попытку.");
                progress.Report((done,total));
            }
            await Verify(destination,release.InstallerSha256!,token);
        }
        catch {if(File.Exists(destination))File.Delete(destination);throw;}
    }
    internal static string PrepareHelper(string folder,UpdateRelease release)
    {
        string source=Environment.ProcessPath??throw new IOException("Не найден путь лаунчера.");
        if(!Path.GetFileName(source).Equals("LauncherPlaySpace.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Запустите LauncherPlaySpace.exe для установки обновления.");
        string helper=Path.Combine(folder,"helper");Directory.CreateDirectory(helper);
        File.Copy(source,Path.Combine(helper,"LauncherPlaySpace.exe"),true);
        // Published single-file builds need only the EXE; development builds also need their managed dependencies.
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"LauncherPlaySpace.runtimeconfig.json")))
            foreach(string file in Directory.EnumerateFiles(AppContext.BaseDirectory).Where(f=>Path.GetExtension(f)==".dll"||Path.GetFileName(f) is "LauncherPlaySpace.runtimeconfig.json" or "LauncherPlaySpace.deps.json"))
                File.Copy(file,Path.Combine(helper,Path.GetFileName(file)),true);
        using var parent=Process.GetCurrentProcess();
        string job=Path.Combine(folder,"job.json");
        File.WriteAllText(job,JsonConvert.SerializeObject(new UpdateJob(Path.Combine(folder,"setup.exe"),release.InstallerSha256!,source,parent.Id,parent.StartTime.ToUniversalTime().Ticks,release.Version)));
        return job;
    }
    internal static ProcessStartInfo InstallerStart(UpdateJob job,string folder)
    {
        var start=new ProcessStartInfo(job.Installer){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder};
        foreach(string arg in new[]{"/VERYSILENT","/SUPPRESSMSGBOXES","/SP-","/NORESTART","/NOCLOSEAPPLICATIONS","/NORESTARTAPPLICATIONS","/DIR="+Path.GetDirectoryName(job.Target),"/LOG="+Path.Combine(folder,"install.log"),"/PLAYSPACEPROGRESS="+Path.Combine(folder,"progress.txt")})start.ArgumentList.Add(arg);
        return start;
    }
}

internal sealed class UpdateActivity : Control
{
    private readonly System.Windows.Forms.Timer timer=new(){Interval=16};
    private float phase;
    private double displayed;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal double? Progress {get;set;}
    internal UpdateActivity(){DoubleBuffered=true;Height=70;timer.Tick+=(s,e)=>{phase=(phase+.012f)%1;if(Progress is double target)displayed=PresentationOptions.ReduceMotion?target:displayed+(target-displayed)*.18;Invalidate();};VisibleChanged+=(s,e)=>{if(Visible)timer.Start();else timer.Stop();};}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var track=new SolidBrush(Color.FromArgb(42,46,54));using var fill=new SolidBrush(Color.FromArgb(69,184,164));
        e.Graphics.FillRectangle(track,0,28,Width,6);
        if(Progress is double value)e.Graphics.FillRectangle(fill,0,28,(int)(Width*Math.Clamp(PresentationOptions.ReduceMotion?value:displayed,0,1)),6);
        else {float x=PresentationOptions.ReduceMotion?0:phase*Math.Max(1,Width-100);e.Graphics.FillRectangle(fill,x,28,Math.Min(100,Width),6);}
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}

internal sealed class LauncherUpdateForm : StudioDialog
{
    private readonly SmoothLabel stage=new(){Dock=DockStyle.Fill,Text="Подготовка обновления",Font=new Font("Segoe UI",18,FontStyle.Bold)};
    private readonly SmoothLabel detail=new(){Dock=DockStyle.Fill,Text="",ForeColor=Color.FromArgb(172,183,193)};
    private readonly UpdateActivity activity=new(){Dock=DockStyle.Fill};
    private readonly ModernButton action=new("Отменить",Color.FromArgb(43,49,57)){Dock=DockStyle.Fill};
    private readonly CancellationTokenSource cancellation=new();
    private bool installing,busy,disposed;
    private readonly UpdateRelease? release;
    private readonly string? jobFile;
    private readonly bool preview;
    internal LauncherUpdateForm(UpdateRelease release,bool preview=false):this(){this.release=release;this.preview=preview;}
    internal LauncherUpdateForm(string jobFile):this(){this.jobFile=jobFile;ShowInTaskbar=true;}
    private LauncherUpdateForm()
    {
        Text="PLAYSPACE / Обновление";Size=new Size(640,440);MinimumSize=new Size(560,400);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28),ColumnCount=1,RowCount=5};
        foreach(var height in new[]{44,64,72,44})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new SmoothLabel{Dock=DockStyle.Fill,Text="LAUNCHERPLAYSPACE",ForeColor=Color.FromArgb(69,184,164)},0,0);
        layout.Controls.Add(stage,0,1);layout.Controls.Add(detail,0,2);layout.Controls.Add(activity,0,3);layout.Controls.Add(action,0,4);ContentHost.Controls.Add(layout);
        action.Click+=(s,e)=>{if(busy)cancellation.Cancel();else Close();};
        FormClosing+=(s,e)=>{if(installing){e.Cancel=true;return;}if(busy){cancellation.Cancel();e.Cancel=true;}};
        Shown+=async(s,e)=>{if(!preview)await Run();};
    }
    private async Task Run()
    {
        busy=true;string? folder=null;
        try
        {
            if(jobFile!=null){installing=true;action.Enabled=false;await Install(jobFile);return;}
            folder=Path.Combine(Path.GetTempPath(),"PlaySpaceUpdate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            stage.Text="Загрузка обновления";
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(30)};
            var clock=Stopwatch.StartNew();
            var progress=new Progress<(long Done,long? Total)>(p=>{
                if(IsDisposed)return;
                activity.Progress=p.Total>0?(double)p.Done/p.Total.Value:null;
                detail.Text=$"Версия {release!.Version} · {p.Done/1048576d:F1}"+(p.Total>0?$" / {p.Total/1048576d:F1} Мбайт · {100d*p.Done/p.Total:F0}%":" Мбайт")+$"\n{p.Done/1048576d/Math.Max(.1,clock.Elapsed.TotalSeconds):F1} Мбайт/с";
            });
            await LauncherUpdater.Download(client,release!,Path.Combine(folder,"setup.exe"),progress,cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();stage.Text="Подготовка установки";detail.Text="Загрузка проверена. Перезапускаем лаунчер для обновления.";activity.Progress=null;
            string job=await Task.Run(()=>LauncherUpdater.PrepareHelper(folder,release!),cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var start=new ProcessStartInfo(Path.Combine(folder,"helper","LauncherPlaySpace.exe")){UseShellExecute=false,WorkingDirectory=folder};
            start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(job);
            using var helper=Process.Start(start)??throw new IOException("Не удалось запустить обновление.");
            var ready=Stopwatch.StartNew();
            while(!File.Exists(Path.Combine(folder,"ready")))
            {
                if(helper.HasExited)throw new IOException("Процесс обновления не запустился. Лаунчер останется открыт.");
                if(ready.Elapsed>TimeSpan.FromSeconds(20)||cancellation.IsCancellationRequested)
                {
                    helper.Kill();await helper.WaitForExitAsync();
                    throw new IOException("Не удалось подготовить процесс обновления. Повторите попытку.");
                }
                await Task.Delay(100);
            }
            folder=null;busy=false;DialogResult=DialogResult.OK;Close();
        }
        catch(OperationCanceledException){busy=false;Close();}
        catch(Exception ex){stage.Text="Обновление не завершено";detail.Text=ex.Message;activity.Progress=0;action.Text="Закрыть";}
        finally
        {
            busy=false;installing=false;action.Enabled=true;
            if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private async Task Install(string file)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(file))!;
        var job=JsonConvert.DeserializeObject<UpdateJob>(await File.ReadAllTextAsync(file))??throw new InvalidDataException("Нет задания обновления.");
        if(!Path.GetFullPath(job.Installer).Equals(Path.Combine(folder,"setup.exe"),StringComparison.OrdinalIgnoreCase)||!Path.IsPathFullyQualified(job.Target)||Path.GetFileName(job.Target)!="LauncherPlaySpace.exe")throw new InvalidDataException("Некорректное задание обновления.");
        await File.WriteAllTextAsync(Path.Combine(folder,"ready"),"ready");
        stage.Text="Подготовка установки";detail.Text="Сохраняем данные и закрываем предыдущую версию…";
        try
        {
            using var parent=Process.GetProcessById(job.ParentId);
            if(parent.StartTime.ToUniversalTime().Ticks==job.ParentStarted)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await parent.WaitForExitAsync(timeout.Token);
            }
        }
        catch(ArgumentException){}
        catch(OperationCanceledException){throw new IOException("Лаунчер не завершился за 60 секунд. Закройте его и повторите обновление.");}
        await LauncherUpdater.Verify(job.Installer,job.Hash,CancellationToken.None);
        stage.Text="Установка обновления";detail.Text="Обновляем файлы приложения. Библиотека и настройки сохраняются.";
        using var installer=Process.Start(LauncherUpdater.InstallerStart(job,folder))??throw new IOException("Не удалось запустить установщик.");
        string progressFile=Path.Combine(folder,"progress.txt");
        while(!installer.HasExited)
        {
            try{if(int.TryParse(await File.ReadAllTextAsync(progressFile),out int value)){activity.Progress=Math.Clamp(value/100d,0,.99);detail.Text=$"Замена файлов · {Math.Clamp(value,0,99)}%";}}catch(IOException){}catch(UnauthorizedAccessException){}
            await Task.Delay(150);
        }
        if(installer.ExitCode!=0)throw new IOException($"Установщик завершился с кодом {installer.ExitCode}. Подробности: {Path.Combine(folder,"install.log")}");
        string version=FileVersionInfo.GetVersionInfo(job.Target).FileVersion??"0.0";
        if(UpdateService.ParseVersion(version)<UpdateService.ParseVersion(job.Version))throw new IOException("Версия установленного файла не совпадает с обновлением.");
        activity.Progress=1;stage.Text="Обновление установлено";detail.Text="Запускаем новую версию…";
        await Task.Delay(650);
        var restart=new ProcessStartInfo(job.Target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(job.Target)!};
        restart.ArgumentList.Add("--cleanup-update");restart.ArgumentList.Add(folder);
        using var next=Process.Start(restart)??throw new IOException("Обновление установлено, но лаунчер не запустился. Откройте его ярлыком.");
        try{File.Delete(job.Installer);}catch(IOException){}catch(UnauthorizedAccessException){}
        busy=false;installing=false;Close();
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
