using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace GameLauncherGUI;

internal enum WarpState { Missing, Connected, Connecting, Disconnected, RegistrationMissing, Unknown }
internal sealed record WarpStatus(WarpState State,string Message);
internal sealed record WarpResult(int ExitCode,string Output);
internal interface IWarpClient
{
    Task<WarpStatus> Status(CancellationToken cancellation);
    Task Connect(bool accepted,CancellationToken cancellation);
    Task Disconnect(CancellationToken cancellation);
}
internal sealed class WarpClient : IWarpClient
{
    private readonly Func<string?> locate;
    private readonly Func<string,IReadOnlyList<string>,CancellationToken,Task<WarpResult>> execute;
    private readonly Func<string,CancellationToken,Task> verify;
    public WarpClient():this(FindCli,Run,VerifyCloudflare){}
    internal WarpClient(Func<string?> locate,Func<string,IReadOnlyList<string>,CancellationToken,Task<WarpResult>> execute,Func<string,CancellationToken,Task> verify){this.locate=locate;this.execute=execute;this.verify=verify;}
    internal static string? FindCli()
    {
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Cloudflare","Cloudflare WARP","warp-cli.exe");
        return File.Exists(path)?path:null;
    }
    internal static WarpStatus ParseStatus(WarpResult result)
    {
        string text=result.Output;
        if(Regex.IsMatch(text,@"registration (?:missing|not found)|missing registration|not registered",RegexOptions.IgnoreCase))return new(WarpState.RegistrationMissing,"Компонент установлен. Нужно настроить подключение.");
        if(result.ExitCode!=0)return new(WarpState.Unknown,"Не удалось получить статус WARP. Проверьте службу Cloudflare или повторите позже.");
        bool Is(string value)=>Regex.IsMatch(text,@"(?im)^\s*(?:Status update:\s*)?"+value+@"\s*$");
        if(Is("Disconnected"))return new(WarpState.Disconnected,"WARP отключён. Используется обычное подключение.");
        if(Is("Connected"))return new(WarpState.Connected,"Клиент WARP подключён. Для проверки трафика нажмите «Проверить туннель».");
        if(Is("Connecting")||Is("Disconnecting"))return new(WarpState.Connecting,"WARP меняет состояние подключения…");
        return new(WarpState.Unknown,"Статус WARP не распознан. Подключение не подтверждено.");
    }
    private async Task<WarpResult> Command(IReadOnlyList<string> args,CancellationToken cancellation)
    {
        string path=locate()??throw new IOException("Сначала установите компонент WARP.");
        await verify(path,cancellation);return await execute(path,args,cancellation);
    }
    public async Task<WarpStatus> Status(CancellationToken cancellation)=>locate()==null?new(WarpState.Missing,"Для подключения сначала установите официальный компонент Cloudflare WARP."):ParseStatus(await Command(new[]{"status"},cancellation));
    private async Task Required(string[] args,CancellationToken cancellation)
    {
        var result=await Command(args,cancellation);
        if(result.ExitCode!=0)throw new IOException("WARP не выполнил команду. Возможны недоступность Cloudflare, ограничения профиля или остановленная служба. Отключите другой VPN и проверьте официальный клиент.");
    }
    public async Task Connect(bool accepted,CancellationToken cancellation)
    {
        if(!accepted)throw new InvalidOperationException("Перед подключением ознакомьтесь с условиями Cloudflare и подтвердите согласие.");
        var registration=await Command(new[]{"--accept-tos","registration","show"},cancellation);
        if(registration.ExitCode!=0){
            if(ParseStatus(registration).State!=WarpState.RegistrationMissing)throw new IOException("Не удалось проверить регистрацию WARP. Существующий профиль не изменён.");
            await Required(new[]{"--accept-tos","registration","new"},cancellation);
        }
        if(Regex.IsMatch(registration.Output,@"Account type:\s*(?:Team|ZeroTrust|Zero Trust)|Organization:",RegexOptions.IgnoreCase))throw new IOException("Обнаружен управляемый профиль организации. Управляйте им через официальный клиент; PlaySpace не меняет его настройки.");
        await Required(new[]{"--accept-tos","mode","warp+doh"},cancellation);
        await Required(new[]{"--accept-tos","connect"},cancellation);
    }
    public Task Disconnect(CancellationToken cancellation)=>Required(new[]{"disconnect"},cancellation);
    internal static async Task<WarpResult> Run(string path,IReadOnlyList<string> args,CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        if(Path.GetFileName(path).StartsWith("warp",StringComparison.OrdinalIgnoreCase)){start.ArgumentList.Add("--no-ansi");start.ArgumentList.Add("--no-paginate");}
        foreach(string argument in args)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Не удалось запустить компонент WARP.");process.StandardInput.Close();
        Task<string> output=process.StandardOutput.ReadToEndAsync(timeout.Token),error=process.StandardError.ReadToEndAsync(timeout.Token);
        try{await process.WaitForExitAsync(timeout.Token);return new(process.ExitCode,(await output)+"\n"+(await error));}
        catch(OperationCanceledException){try{process.Kill(true);}catch(InvalidOperationException){}if(cancellation.IsCancellationRequested)throw;throw new IOException("WARP не ответил за 20 секунд. Проверьте службу Cloudflare.");}
    }
    internal static async Task VerifyCloudflare(string file,CancellationToken cancellation)
    {
        string script="$s=Get-AuthenticodeSignature -LiteralPath '"+file.Replace("'","''")+"';if($s.Status -eq 'Valid' -and $s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -in @('Cloudflare, Inc.','Cloudflare Inc.','Cloudflare, Inc')){exit 0}else{exit 1}";
        var result=await Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))},cancellation);
        if(result.ExitCode!=0)throw new IOException("Подпись Cloudflare не подтверждена. Запуск компонента отменён.");
    }
}
internal static class WarpInstaller
{
    internal const string DownloadUrl="https://downloads.cloudflareclient.com/v1/download/windows/ga";
    internal const long MaxBytes=180L*1024*1024;
    internal static bool AllowedDownload(Uri uri)=>uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo)&&uri.IsDefaultPort&&uri.Host.Equals("downloads.cloudflareclient.com",StringComparison.OrdinalIgnoreCase);
    internal static void ValidateHeader(byte[] header)
    {
        if(!header.Take(8).SequenceEqual(new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1}))throw new InvalidDataException("Сервер вернул неподходящий установщик WARP.");
    }
    // Caller owns a client with automatic redirects disabled; every redirect is checked before requesting it.
    internal static async Task<string> Download(HttpClient http,string folder,IProgress<string> progress,Func<string,CancellationToken,Task> verify,CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,"Cloudflare-WARP-"+Guid.NewGuid().ToString("N")+".msi");
        try{
            Uri uri=new(DownloadUrl);
            for(int redirect=0;redirect<6;redirect++){
                if(!AllowedDownload(uri))throw new InvalidDataException("Установщик должен загружаться с официального сервера Cloudflare по HTTPS.");
                using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellation);
                if(response.RequestMessage?.RequestUri is Uri final&&!AllowedDownload(final))throw new InvalidDataException("Недопустимое перенаправление установщика.");
                if((int)response.StatusCode is >=300 and <400){uri=new Uri(uri,response.Headers.Location??throw new IOException("Cloudflare не указал адрес загрузки."));continue;}
                response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>MaxBytes)throw new InvalidDataException("Установщик превышает допустимый размер.");
                await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    using var input=await response.Content.ReadAsStreamAsync(cancellation);byte[] block=new byte[81920];int count;long total=0;int last=-1;
                    while((count=await input.ReadAsync(block,cancellation))>0){total+=count;if(total>MaxBytes)throw new InvalidDataException("Установщик превышает допустимый размер.");await output.WriteAsync(block.AsMemory(0,count),cancellation);int mb=(int)(total/(1024*1024));if(mb!=last){last=mb;progress.Report($"Загружаем компонент Cloudflare · {mb} МБ");}}
                }
                byte[] header=new byte[8];using(var file=File.OpenRead(path)){_ = await file.ReadAsync(header,cancellation);}ValidateHeader(header);
                progress.Report("Проверяем цифровую подпись Cloudflare…");await verify(path,cancellation);return path;
            }
            throw new IOException("Слишком много перенаправлений при загрузке WARP.");
        }catch{if(File.Exists(path))File.Delete(path);throw;}
    }
    internal static int CheckExitCode(int code)=>code switch{0=>code,3010=>code,1602=>throw new IOException("Установка WARP отменена."),1618=>throw new IOException("Windows уже выполняет другую установку. Завершите её и повторите."),_=>throw new IOException($"Установка WARP не завершена. Код Windows Installer: {code}.")};
    internal static async Task<bool> Install(string folder,IProgress<string> progress,CancellationToken cancellation)
    {
        if(!Environment.Is64BitOperatingSystem||System.Runtime.InteropServices.RuntimeInformation.OSArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new IOException("Автоматическая установка WARP пока доступна для Windows x64.");
        Directory.CreateDirectory(folder);progress.Report("Загружаем официальный компонент через центр загрузок…");
        byte[] bytes=await Workspace.Current.Transfers.Fetch("Cloudflare WARP",new Uri(DownloadUrl),MaxBytes,cancellation,AllowedDownload);try{ValidateHeader(bytes);}catch{Workspace.Current.Transfers.Forget(new Uri(DownloadUrl));throw;}
        string path=Path.Combine(folder,"Cloudflare-WARP-"+Guid.NewGuid().ToString("N")+".msi");
        try{
            await File.WriteAllBytesAsync(path,bytes,cancellation);
            // Verify again immediately before starting the elevated, original MSI installer.
            await WarpClient.VerifyCloudflare(path,cancellation);cancellation.ThrowIfCancellationRequested();progress.Report("Устанавливаем компонент WARP. Подтвердите запрос Windows…");
            using var installer=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe")){
                UseShellExecute=true,Verb="runas",Arguments=$"/i \"{path}\" /passive /norestart",WindowStyle=ProcessWindowStyle.Normal})??throw new IOException("Windows не запустила установку.");
            // Never terminate MSI mid-installation or force a restart.
            await installer.WaitForExitAsync();return CheckExitCode(installer.ExitCode)==3010;
        }finally{if(File.Exists(path))File.Delete(path);Workspace.Current.Transfers.Forget(new Uri(DownloadUrl));}
    }
    internal static bool TraceConfirmsWarp(string text)=>text.Split('\n').Select(line=>line.Trim()).Any(line=>line is "warp=on" or "warp=plus");
    internal static async Task<bool> VerifyTunnel(CancellationToken cancellation)
    {
        using var handler=new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false};using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(12)};
        using var response=await http.GetAsync("https://www.cloudflare.com/cdn-cgi/trace",HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();
        using var input=await response.Content.ReadAsStreamAsync(cancellation);using var bytes=new MemoryStream();byte[] buffer=new byte[4096];int count;
        while((count=await input.ReadAsync(buffer,cancellation))>0){if(bytes.Length+count>16384)throw new InvalidDataException("Неожиданный ответ проверки туннеля.");bytes.Write(buffer,0,count);}
        return TraceConfirmsWarp(Encoding.UTF8.GetString(bytes.ToArray()));
    }
}
