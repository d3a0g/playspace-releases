using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace GameLauncherGUI;

internal enum CheckOutcome { Ok, Warning, Failed, Info }
internal sealed record DiagnosticCheck(string Name,CheckOutcome Outcome,string Detail,string Advice,long? Milliseconds=null);
internal sealed record DiagnosticTarget(string Key,string Name,string Host)
{
    public override string ToString()=>Name;
    internal static readonly DiagnosticTarget[] All={new("steam","Steam","store.steampowered.com"),new("telegram","Telegram","api.telegram.org"),new("ubisoft","Rainbow Six Siege / Ubisoft","www.ubisoft.com"),new("github","GitHub / обновления PlaySpace","github.com")};
}
internal sealed record NetworkDiagnosticReport(DateTimeOffset Created,DiagnosticTarget Target,IReadOnlyList<DiagnosticCheck> Checks)
{
    internal string Summary=>Checks.Any(c=>c.Outcome==CheckOutcome.Failed)?"Есть ошибки подключения. Ниже указаны проверки и следующие действия.":Checks.Any(c=>c.Outcome==CheckOutcome.Warning)?"Есть замечания. Полная работоспособность сервиса не подтверждена.":"Проверки завершены. Доступ к игровым серверам и вход в аккаунт нужно проверить отдельно.";
    internal static string OutcomeName(CheckOutcome outcome)=>outcome switch{CheckOutcome.Ok=>"OK",CheckOutcome.Warning=>"ВНИМАНИЕ",CheckOutcome.Failed=>"ОШИБКА",_=>"ИНФОРМАЦИЯ"};
    internal static string DisplayChecks(IEnumerable<DiagnosticCheck> checks)=>string.Join("\n\n",checks.Select(c=>$"[{OutcomeName(c.Outcome)}] {c.Name}"+(c.Milliseconds.HasValue?$" · {c.Milliseconds} мс":"")+"\n"+c.Detail+(string.IsNullOrEmpty(c.Advice)?"":"\nЧто сделать: "+c.Advice)));
    internal string ToSafeText()=>new StringBuilder()
        .AppendLine("LauncherPlaySpace — отчёт о подключении")
        .AppendLine($"Дата UTC: {Created:yyyy-MM-dd HH:mm:ss}")
        .AppendLine($"Версия лаунчера: {UpdateService.DisplayVersion}")
        .AppendLine($"Windows: {Environment.OSVersion.Version}; архитектура: {RuntimeInformation.OSArchitecture}")
        .AppendLine($"Выбранный сервис: {Target.Name} ({Target.Host})")
        .AppendLine().AppendLine(Summary).AppendLine().AppendLine(DisplayChecks(Checks))
        .AppendLine().AppendLine("Границы проверки: DNS, TCP 443 и HTTPS главного публичного адреса. Это не проверка UDP, игровых серверов, входа в аккаунт или всех функций приложения. Время HTTPS-запроса не является пингом в игре.")
        .AppendLine("Проверки выполняются без HTTP-прокси; отдельные приложения могут использовать другой маршрут. Ответ WARP относится только к контрольному запросу Cloudflare.")
        .AppendLine("Отчёт составлен из результатов перечисленных проверок. Личные пути, имя ПК, IP-адреса, конфигурации VPN, ключи Steam и токены Telegram не собираются. Сетевые настройки не изменялись. Автоматическая отправка отключена.").ToString();
}
internal interface IDiagnosticProbe
{
    bool NetworkAvailable();
    Task Resolve(string host,CancellationToken cancellation);
    Task Tcp(string host,CancellationToken cancellation);
    Task<int> Https(string host,CancellationToken cancellation);
}
internal sealed class DiagnosticProbe : IDiagnosticProbe
{
    public bool NetworkAvailable()=>NetworkInterface.GetIsNetworkAvailable();
    public async Task Resolve(string host,CancellationToken cancellation){var addresses=await Dns.GetHostAddressesAsync(host,cancellation);if(addresses.Length==0)throw new SocketException((int)SocketError.HostNotFound);}
    public async Task Tcp(string host,CancellationToken cancellation){using var socket=new TcpClient();await socket.ConnectAsync(host,443,cancellation);}
    public async Task<int> Https(string host,CancellationToken cancellation)
    {
        using var handler=new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false};using var http=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        using var request=new HttpRequestMessage(HttpMethod.Get,new Uri("https://"+host+"/"));request.Headers.UserAgent.ParseAdd("LauncherPlaySpace-Diagnostics/"+UpdateService.DisplayVersion);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);return (int)response.StatusCode;
    }
}
internal sealed class NetworkDiagnostics
{
    private readonly IDiagnosticProbe probe;private readonly IWarpClient warp;private readonly Func<CancellationToken,Task<bool>> tunnel;
    internal NetworkDiagnostics(IDiagnosticProbe? probe=null,IWarpClient? warp=null,Func<CancellationToken,Task<bool>>? tunnel=null){this.probe=probe??new DiagnosticProbe();this.warp=warp??new WarpClient();this.tunnel=tunnel??WarpInstaller.VerifyTunnel;}
    internal async Task<NetworkDiagnosticReport> Run(DiagnosticTarget target,IProgress<DiagnosticCheck>? progress,CancellationToken cancellation)
    {
        if(!DiagnosticTarget.All.Contains(target))throw new ArgumentException("Выберите сервис из списка.");
        var checks=new List<DiagnosticCheck>();
        void Add(DiagnosticCheck check){checks.Add(check);progress?.Report(check);}
        cancellation.ThrowIfCancellationRequested();
        Add(probe.NetworkAvailable()?new("Подключение ПК",CheckOutcome.Ok,"Windows обнаружила активное сетевое подключение.","Это ещё не подтверждает доступ в интернет."):new("Подключение ПК",CheckOutcome.Failed,"Windows не обнаружила активное сетевое подключение.","Проверьте Wi-Fi, кабель и режим полёта."));
        async Task Step(string name,Func<CancellationToken,Task<DiagnosticCheck>> run,string advice)
        {
            cancellation.ThrowIfCancellationRequested();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(8));var watch=Stopwatch.StartNew();
            try{var result=await run(timeout.Token);cancellation.ThrowIfCancellationRequested();Add(result with{Milliseconds=watch.ElapsedMilliseconds});}
            catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){Add(new(name,CheckOutcome.Failed,"Проверка не завершилась за 8 секунд.",advice));}
            catch(Exception ex)when(ex is SocketException or HttpRequestException or IOException or InvalidDataException or InvalidOperationException or System.Security.Authentication.AuthenticationException or UnauthorizedAccessException){Add(new(name,CheckOutcome.Failed,"Проверка не выполнена. Подробности из системных исключений не включаются в отчёт.",advice));}
        }
        await Step("DNS выбранного сервиса",async ct=>{await probe.Resolve(target.Host,ct);return new("DNS выбранного сервиса",CheckOutcome.Ok,"Имя "+target.Host+" разрешается в сетевой адрес.","Ответ DNS не доказывает доступность сайта.");},"Повторите проверку. Если HTTPS работает, возможен отдельный сбой DNS или кеша.");
        await Step("TCP 443 выбранного сервиса",async ct=>{await probe.Tcp(target.Host,ct);return new("TCP 443 выбранного сервиса",CheckOutcome.Ok,"Сервер принимает TCP-соединение на порту HTTPS.","UDP и игровые порты этой проверкой не проверяются.");},"Проверьте интернет и ограничения антивируса или брандмауэра. Не отключайте защиту целиком.");
        async Task<DiagnosticCheck> Https(string host,string title,CancellationToken ct)
        {
            int code=await probe.Https(host,ct);
            if(code is 403 or 451)return new(title,CheckOutcome.Warning,$"Сервер ответил HTTP {code}. Соединение есть, но запрос ограничен.","Это не доказывает блокировку провайдером. Проверьте сервис в браузере или с другим подключением.");
            if(code==429)return new(title,CheckOutcome.Warning,"Сервер ответил HTTP 429: слишком много запросов.","Подождите и повторите позже.");
            if(code>=500||code<200||code>=400&&code!=404)return new(title,CheckOutcome.Warning,$"HTTPS-сервер ответил HTTP {code}.","Проверьте официальный статус сервиса; ошибка сайта не обязательно связана с вашим подключением.");
            return new(title,CheckOutcome.Ok,$"Получен ответ HTTPS (HTTP {code})."+(code==404?" Проверяемый корневой адрес не содержит страницы, но сервер отвечает.":code>=300?" Сервер предложил перенаправление; конечная страница не проверялась.":""),"Это проверка публичного адреса, а не входа в аккаунт или игровых серверов.");
        }
        await Step("HTTPS выбранного сервиса",ct=>Https(target.Host,"HTTPS выбранного сервиса",ct),"Откройте сервис в браузере. При необходимости вернитесь в «Сеть / WARP» и сравните результат после подключения.");
        await Step("Контрольный HTTPS-запрос",ct=>Https("www.cloudflare.com","Контрольный HTTPS-запрос",ct),"Не удалось получить ответ Cloudflare. Это не означает, что весь интернет недоступен.");
        // A WARP check is read-only: never accepts terms, registers, connects or disconnects.
        await Step("Состояние клиента WARP",async ct=>{
            var state=await warp.Status(ct);return state.State switch{
                WarpState.Connected=>new("Состояние клиента WARP",CheckOutcome.Ok,"Клиент сообщает о подключении.","Подтверждение трафика приведено отдельной проверкой."),
                WarpState.Missing=>new("Состояние клиента WARP",CheckOutcome.Info,"Компонент WARP не установлен.","Установка доступна в «Сеть / WARP», если она вам нужна."),
                WarpState.Disconnected=>new("Состояние клиента WARP",CheckOutcome.Info,"Клиент WARP отключён.","Для сравнения результатов можно подключиться через «Сеть / WARP»."),
                _=>new("Состояние клиента WARP",CheckOutcome.Warning,"Подключение клиента не подтверждено.","Проверьте его состояние в разделе «Сеть / WARP».")};
        },"Проверьте службу Cloudflare и повторите запрос из раздела «Сеть / WARP».");
        await Step("Контрольный трафик WARP",async ct=>await tunnel(ct)?new("Контрольный трафик WARP",CheckOutcome.Ok,"Cloudflare подтверждает WARP для контрольного запроса.","Маршрут игры или Telegram может отличаться."):new("Контрольный трафик WARP",CheckOutcome.Info,"Cloudflare не подтвердила WARP для контрольного запроса.","Если WARP не нужен, это нормально. Если нужен — проверьте подключение в разделе «Сеть / WARP»."),"Контрольный запрос не выполнен. Состояние туннеля не подтверждено; отключение WARP из этого не следует.");
        return new(DateTimeOffset.UtcNow,target,checks.AsReadOnly());
    }
}
