using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal sealed class NetworkForm : StudioDialog
{
    private readonly IWarpClient warp;
    private readonly Func<IProgress<string>,CancellationToken,Task<bool>> installComponent;
    private readonly Func<CancellationToken,Task<bool>> probe;
    private readonly string consentFile;
    private readonly CancellationTokenSource cancellation=new();
    private readonly System.Windows.Forms.Timer timer=new(){Interval=6000};
    private readonly ModernButton install=new("Установить компонент",VisualTheme.Accent){Dock=DockStyle.Fill};
    private readonly ModernButton connect=new("Подключить",VisualTheme.Accent){Dock=DockStyle.Fill};
    private readonly ModernButton disconnect=new("Отключить",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};
    private readonly ModernButton check=new("Проверить туннель",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};
    private readonly ModernButton diagnostics=new("Диагностика и отчёт",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};
    private readonly ModernToggle consent=new(){Dock=DockStyle.Fill,Text="Принимаю условия Cloudflare и подключение через WARP",AccessibleName="Согласие на установку и использование Cloudflare WARP"};
    private readonly SmoothLabel state=new(){Dock=DockStyle.Fill,Font=new Font("Segoe UI",18,FontStyle.Bold),Text="Проверяем компонент…"};
    private readonly SmoothLabel detail=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(165,177,202)};
    private readonly SmoothLabel activity=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(188,174,239),AutoEllipsis=true};
    private WarpState current=WarpState.Unknown;private bool busy,disposed,reboot;
    internal NetworkForm(string dataFolder,IWarpClient? client=null,Func<IProgress<string>,CancellationToken,Task<bool>>? installer=null,Func<CancellationToken,Task<bool>>? trace=null)
    {
        warp=client??new WarpClient();installComponent=installer??((progress,ct)=>WarpInstaller.Install(Path.Combine(dataFolder,"downloads","warp"),progress,ct));probe=trace??WarpInstaller.VerifyTunnel;
        consentFile=Path.Combine(dataFolder,"network-consent.json");
        try{if(File.Exists(consentFile))consent.Checked=JObject.Parse(File.ReadAllText(consentFile)).Value<bool>("cloudflareTermsAccepted");}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException){}
        Text="PLAYSPACE / Сеть";Size=new Size(900,760);MinimumSize=new Size(760,680);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(28)};
        foreach(float height in new[]{84F,112F,52F,48F,44F,36F,38F})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new StudioHeading("Одно подключение — больше возможностей","Управление официальным Cloudflare WARP из PlaySpace","СЕТЬ / WARP"){Dock=DockStyle.Fill},0,0);
        var statusCard=new RoundedPanel{Dock=DockStyle.Fill,Margin=new Padding(0,6,0,10),Padding=new Padding(18,10,18,10)};
        var statusLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=Color.FromArgb(23,28,42)};statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));statusLayout.Controls.Add(state,0,0);statusLayout.Controls.Add(detail,0,1);statusCard.Controls.Add(statusLayout);layout.Controls.Add(statusCard,0,1);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};foreach(int _ in Enumerable.Range(0,3))actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.Controls.Add(install,0,0);actions.Controls.Add(connect,1,0);actions.Controls.Add(disconnect,2,0);layout.Controls.Add(actions,0,2);
        var tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};for(int i=0;i<3;i++)tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));tools.Controls.Add(check,0,0);tools.Controls.Add(diagnostics,1,0);var downloads=new ModernButton("Центр загрузок",Color.FromArgb(28,44,55)){Dock=DockStyle.Fill};downloads.Click+=(s,e)=>{using var dialog=new DownloadsForm(Workspace.Current.Transfers);dialog.ShowDialog(this);};tools.Controls.Add(downloads,2,0);layout.Controls.Add(tools,0,3);layout.Controls.Add(consent,0,4);
        diagnostics.Click+=(s,e)=>{timer.Stop();using var dialog=new NetworkDiagnosticsForm();dialog.ShowDialog(this);if(!disposed)timer.Start();};
        var links=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        foreach(var entry in new[]{("Условия Cloudflare","https://www.cloudflare.com/application/terms/"),("Приватность","https://www.cloudflare.com/application/privacypolicy/"),("Помощь","https://developers.cloudflare.com/warp-client/get-started/windows/")}){
            var link=new LinkLabel{Text=entry.Item1,AutoSize=true,LinkColor=Color.FromArgb(175,165,240),Margin=new Padding(4,8,18,0)};link.LinkClicked+=(s,e)=>OpenLink(entry.Item2);links.Controls.Add(link);
        }
        layout.Controls.Add(links,0,5);layout.Controls.Add(activity,0,6);
        layout.Controls.Add(new FormattedDescription{Dock=DockStyle.Fill,Padding=new Padding(14),Text="КАК ЭТО РАБОТАЕТ\n\nПри первой настройке PlaySpace скачает и проверит официальный пакет WARP. Windows запросит разрешение на установку службы и сетевых компонентов. Пакет также содержит интерфейс Cloudflare; дальнейшее подключение доступно здесь.\n\nWARP влияет на подключение всего ПК: браузер, Telegram и игры. Доступность сайтов и игровых серверов, а также снижение пинга не гарантируются. Одновременная работа с другим VPN может мешать подключению.\n\nЗакрытие этого окна и выход из PlaySpace не отключают службу WARP. Для возврата к обычной сети нажмите «Отключить». Удаление компонента доступно в «Параметры Windows → Приложения»."},0,7);
        consent.CheckedChanged+=(s,e)=>UpdateActions();
        install.Click+=async(s,e)=>await Work(async()=>{
            SaveConsent();var progress=new Progress<string>(text=>{if(!disposed)activity.Text=text;});reboot=await installComponent(progress,cancellation.Token);
            await ReadStatus();activity.Text=reboot?"Компонент установлен. Перезагрузите Windows, затем подключитесь.":"Компонент установлен. Нажмите «Подключить».";
        });
        connect.Click+=async(s,e)=>await Work(async()=>{SaveConsent();activity.Text="Настраиваем подключение WARP…";await warp.Connect(consent.Checked,cancellation.Token);await ReadStatus();activity.Text="Команда подключения принята. Статус обновляется автоматически.";});
        disconnect.Click+=async(s,e)=>await Work(async()=>{activity.Text="Отключаем WARP…";await warp.Disconnect(cancellation.Token);await ReadStatus();activity.Text="Команда отключения принята. Статус обновляется автоматически.";});
        check.Click+=async(s,e)=>await Work(async()=>{activity.Text="Проверяем прохождение трафика через WARP…";bool confirmed=await probe(cancellation.Token);if(disposed)return;activity.Text=confirmed?"Трафик к Cloudflare проходит через WARP. Доступность отдельных игр проверяется отдельно.":"Туннель WARP не подтверждён. Проверьте подключение и другой VPN.";});
        timer.Tick+=async(s,e)=>{if(!busy)await Work(ReadStatus);};Shown+=async(s,e)=>{await Work(ReadStatus);if(!disposed)timer.Start();};
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;activity.Text="Дождитесь завершения текущего действия. Установку Windows нельзя прерывать.";}};
        UpdateActions();
    }
    private void SaveConsent()
    {
        if(!consent.Checked)throw new InvalidOperationException("Перед установкой и подключением подтвердите согласие с условиями Cloudflare.");
        Directory.CreateDirectory(Path.GetDirectoryName(consentFile)!);File.WriteAllText(consentFile,new JObject{["cloudflareTermsAccepted"]=true,["acceptedAtUtc"]=DateTimeOffset.UtcNow.ToString("O")}.ToString());
    }
    private async Task ReadStatus()
    {
        var snapshot=await warp.Status(cancellation.Token);if(disposed)throw new OperationCanceledException();current=snapshot.State;
        state.Text=current switch{WarpState.Missing=>"Компонент не установлен",WarpState.Connected=>"WARP подключён",WarpState.Disconnected=>"WARP отключён",WarpState.Connecting=>"Подключение меняется…",WarpState.RegistrationMissing=>"Готов к настройке",_=>"Нужна проверка подключения"};
        state.ForeColor=current==WarpState.Connected?Color.FromArgb(113,218,193):Color.FromArgb(236,236,248);detail.Text=snapshot.Message;
    }
    private void UpdateActions(){install.Enabled=!busy&&consent.Checked&&current==WarpState.Missing;connect.Enabled=!busy&&consent.Checked&&!reboot&&current is not(WarpState.Missing or WarpState.Connected or WarpState.Connecting);disconnect.Enabled=!busy&&current!=WarpState.Missing;check.Enabled=!busy&&current!=WarpState.Missing;diagnostics.Enabled=!busy;consent.Enabled=!busy;}
    private async Task Work(Func<Task> action)
    {
        if(busy||disposed)return;busy=true;UpdateActions();
        try{await action();}catch(OperationCanceledException){if(!disposed)activity.Text="Действие отменено или сервер не ответил вовремя.";}
        catch(Exception ex)when(ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Net.Http.HttpRequestException){
            if(!disposed)activity.Text=ex is System.ComponentModel.Win32Exception win&&win.NativeErrorCode==1223?"Установка отменена в запросе Windows.":ex.Message;
        }finally{busy=false;if(!disposed)UpdateActions();}
    }
    private void OpenLink(string url){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){activity.Text="Не удалось открыть браузер.";}}
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;timer.Stop();timer.Dispose();cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
