using System.Diagnostics;
namespace GameLauncherGUI;

internal sealed class UniversalDlss5Form : StudioDialog
{
    private readonly UniversalDlss5 service;
    private readonly GameInfo game;
    private string? executable;
    private readonly Label status=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(180,192,215)};
    private readonly CheckBox consent=new(){Text="Это одиночная игра без античита. Понимаю экспериментальный характер инжектора.",Dock=DockStyle.Fill};
    private readonly ModernButton install=new("Установить UniversalDLSS5",Color.FromArgb(115,94,244)){Dock=DockStyle.Fill};
    private readonly ModernButton import=new("Импорт NVIDIA runtime…",Color.FromArgb(35,45,63)){Dock=DockStyle.Fill};
    private readonly ModernButton open=new("Открыть контроллер",Color.FromArgb(35,45,63)){Dock=DockStyle.Fill};
    private readonly ModernButton launch=new("Запустить с DLSS 5 · эксперимент",Color.FromArgb(92,74,180)){Dock=DockStyle.Fill};
    private readonly CancellationTokenSource cancellation=new();private bool busy,disposed;
    public UniversalDlss5Form(GameInfo game,string? storage=null)
    {
        this.game=game;service=new(storage);executable=File.Exists(game.ExePath)?game.ExePath:StrategyGameAdapter.For(game)?.FindExecutable(game);
        Text="PLAYSPACE / UniversalDLSS5 · "+game.Name;Size=new Size(940,760);MinimumSize=new Size(840,700);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(28)};
        foreach(float height in new[]{60f,145,42,50,50,50,48})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new Label{Text="DLSS 5 · Neural Rendering",Dock=DockStyle.Fill,Font=new Font("Segoe UI",23,FontStyle.Bold)},0,0);
        layout.Controls.Add(new Label{Text="Экспериментальная интеграция UniversalDLSS5 0.5.1 (MotionflowOffical, Apache 2.0), не продукт NVIDIA. Модуль перехватывает рендер игры; совместимость и качество не гарантируются.\n\nПервый раз установи контроллер и импортируй официальный nvngx_dlssnr.dll из доступного тебе NVIDIA SDK. Затем можно запускать выбранную игру отсюда. Файлы игры не заменяются. Нужны совместимые GPU, драйвер и runtime. Для известных сетевых игр и обнаруженного античита запуск отключён.",Dock=DockStyle.Fill},0,1);
        var choose=new ModernButton("Выбрать основной EXE игры…",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};layout.Controls.Add(choose,0,2);
        var installRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};installRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));installRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));installRow.Controls.Add(install,0,0);
        var locate=new ModernButton("Указать установленный…",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};installRow.Controls.Add(locate,1,0);layout.Controls.Add(installRow,0,3);
        var runtimeRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};runtimeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));runtimeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));runtimeRow.Controls.Add(import,0,0);
        var source=new ModernButton("NVIDIA SDK ↗",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};runtimeRow.Controls.Add(source,1,0);layout.Controls.Add(runtimeRow,0,4);
        layout.Controls.Add(open,0,5);layout.Controls.Add(consent,0,6);layout.Controls.Add(status,0,7);layout.Controls.Add(new Label{Text="Запуск через контроллер не передаёт параметры запуска PLAYSPACE. Отключение — Detach в контроллере или завершение игры.",Dock=DockStyle.Fill,Font=new Font("Segoe UI",8),ForeColor=Color.FromArgb(142,155,181)},0,8);
        // Keep the launch action next to the controller action, independent of the information panel.
        layout.Controls.Remove(open);var runRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));runRow.Controls.Add(open,0,0);runRow.Controls.Add(launch,1,0);layout.Controls.Add(runRow,0,5);
        consent.CheckedChanged+=(s,e)=>RefreshStatus();
        choose.Click+=(s,e)=>{using var picker=new OpenFileDialog{Filter="EXE игры|*.exe",Title="Основной EXE — "+game.Name,InitialDirectory=Directory.Exists(game.InstallDirectory)?game.InstallDirectory:""};if(picker.ShowDialog(this)==DialogResult.OK){try{UniversalDlss5.ValidateGame(game,picker.FileName,true);executable=picker.FileName;RefreshStatus();}catch(Exception ex){status.Text=ex.Message;}}};
        locate.Click+=(s,e)=>{using var picker=new OpenFileDialog{Filter="Контроллер|UniversalDLSS5.exe",Title="Проверенный UniversalDLSS5 0.5.1"};if(picker.ShowDialog(this)==DialogResult.OK){try{service.RegisterController(picker.FileName);RefreshStatus();}catch(Exception ex){status.Text=ex.Message;}}};
        source.Click+=(s,e)=>OpenUrl("https://developer.nvidia.com/rtx/streamline/get-started");
        install.Click+=async(s,e)=>await Run(async()=>{
            status.Text="Загружаем UniversalDLSS5 0.5.1 и проверяем SHA-256…";string installer=await service.DownloadInstaller(cancellation.Token);string? sevenZip=UniversalDlss5.SevenZip();
            if(sevenZip!=null){status.Text="Распаковываем проверенный модуль…";string controller=await service.ExtractVerified(installer,sevenZip,cancellation.Token);service.RegisterController(controller);}
            else {status.Text="Открыт штатный мастер установки UniversalDLSS5. Заверши его, затем укажи установленный EXE, если он не найден автоматически.";using var process=Process.Start(new ProcessStartInfo(installer){UseShellExecute=true})??throw new IOException("Не удалось открыть установщик.");await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException("Установка отменена или не завершена.");}
            RefreshStatus();
        });
        import.Click+=async(s,e)=>{using var picker=new OpenFileDialog{Filter="NVIDIA DLSS Neural Rendering|nvngx_dlssnr.dll",Title="Выбери nvngx_dlssnr.dll из официального NVIDIA SDK"};if(picker.ShowDialog(this)==DialogResult.OK)await Run(async()=>{status.Text="Проверяем x64 и цифровую подпись NVIDIA…";await UniversalDlss5.ImportRuntime(picker.FileName,UniversalDlss5.RuntimeDirectory(service.Controller()),cancellation.Token);RefreshStatus();});};
        open.Click+=async(s,e)=>await Run(async()=>{string controller=service.Controller()??throw new IOException("Сначала установи проверенный модуль.");if(ControllerRunning())throw new IOException("Контроллер уже открыт. Используй его окно.");Process.Start(UniversalDlss5.StartInfo(controller));await Task.CompletedTask;RefreshStatus();});
        launch.Click+=async(s,e)=>await Run(async()=>{
            string controller=service.Controller()??throw new IOException("Сначала установи модуль.");UniversalDlss5.ValidateGame(game,executable??"",consent.Checked);
            string runtime=Path.Combine(UniversalDlss5.RuntimeDirectory(service.Controller()),"nvngx_dlssnr.dll");UniversalDlss5.ValidateNvidiaRuntimeFile(runtime);status.Text="Проверяем подпись NVIDIA перед запуском…";await DlssManager.VerifyNvidia(runtime);
            if(ControllerRunning())throw new IOException("Контроллер уже запущен. Используй Launch and attach в его окне или сначала закрой его.");
            if(!string.IsNullOrEmpty(game.SteamAppId)){var processes=Process.GetProcessesByName("steam");bool started=processes.Length>0;foreach(var process in processes)process.Dispose();if(!started)throw new IOException("Перед запуском Steam-игры открой клиент Steam.");}
            cancellation.Token.ThrowIfCancellationRequested();Process.Start(UniversalDlss5.StartInfo(controller,game,executable,consent.Checked));status.Text="Запуск передан контроллеру. Дождись Neural Active; без этого статус включения DLSS 5 не подтверждён.";
        });
        FormClosing+=(s,e)=>{if(busy){cancellation.Cancel();e.Cancel=true;status.Text="Завершаем операцию. Затем окно можно закрыть.";}};
        Shown+=(s,e)=>RefreshStatus();
    }
    private static bool ControllerRunning(){var processes=Process.GetProcessesByName("UniversalDLSS5");bool running=processes.Length>0;foreach(var process in processes)process.Dispose();return running;}
    private void RefreshStatus()
    {
        if(IsDisposed)return;string? controller=service.Controller();bool runtime=File.Exists(Path.Combine(UniversalDlss5.RuntimeDirectory(service.Controller()),"nvngx_dlssnr.dll"));bool blocked=DlssManager.Blocked(game);
        open.Enabled=!busy&&controller!=null;launch.Enabled=!busy&&controller!=null&&runtime&&consent.Checked&&executable!=null&&!blocked;
        status.Text="Модуль: "+(controller??"не установлен")+"\nRuntime NVIDIA: "+(runtime?"найден (подпись проверяется при запуске)":"нужен nvngx_dlssnr.dll")+"\nИгра: "+(executable??"выбери настоящий EXE, Steam URI не подходит")+(blocked?"\nЗапуск заблокирован для сетевой игры/обнаруженного античита.":"");
    }
    private async Task Run(Func<Task> work){if(busy)return;busy=true;install.Enabled=import.Enabled=open.Enabled=launch.Enabled=false;try{await work();}catch(Exception ex){if(!IsDisposed)status.Text="Операция не выполнена: "+ex.Message;}finally{busy=false;if(!IsDisposed){install.Enabled=import.Enabled=true;string? controller=service.Controller();open.Enabled=controller!=null;launch.Enabled=controller!=null&&consent.Checked&&File.Exists(Path.Combine(UniversalDlss5.RuntimeDirectory(service.Controller()),"nvngx_dlssnr.dll"))&&executable!=null&&!DlssManager.Blocked(game);}}}
    private void OpenUrl(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){status.Text="Не удалось открыть браузер.";}}
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
