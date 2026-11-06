using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlaySpace.SupportBot;
internal sealed class Configuration
{
    public string Token{get;set;}="";
    public long OwnerChatId{get;set;}
    public string PairingCode{get;set;}="";
    public string? ProxyUrl{get;set;}
}
internal sealed class Bot
{
    private readonly Configuration configuration;private readonly HttpClient http;private readonly string folder,configFile;private long offset;private readonly Dictionary<long,DateTimeOffset> cooldown=new();
    public Bot(Configuration configuration,string configFile,HttpClient? client=null){this.configuration=configuration;this.configFile=configFile;folder=Path.GetDirectoryName(configFile)!;http=client??CreateHttp(configuration);}
    private static HttpClient CreateHttp(Configuration configuration)
    {
        var handler=new HttpClientHandler();
        if(!string.IsNullOrWhiteSpace(configuration.ProxyUrl)){
            var uri=new Uri(configuration.ProxyUrl);if(uri.Scheme!="http"&&uri.Scheme!="https"&&uri.Scheme!="socks5")throw new IOException("Unsupported proxy scheme.");handler.Proxy=new System.Net.WebProxy(uri);handler.UseProxy=true;
        }
        return new HttpClient(handler){Timeout=TimeSpan.FromSeconds(50)};
    }
    private string Url(string method)=>$"https://api.telegram.org/bot{configuration.Token}/{method}";
    private async Task<JsonObject> Call(string method,object payload,CancellationToken cancellation){using var response=await http.PostAsJsonAsync(Url(method),payload,cancellation);response.EnsureSuccessStatusCode();var result=JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation))!.AsObject();if(result["ok"]?.GetValue<bool>()!=true)throw new IOException("Telegram rejected request.");return result;}
    private Task<JsonObject> Send(long chat,string text,CancellationToken cancellation)=>Call("sendMessage",new{chat_id=chat,text,disable_web_page_preview=true},cancellation);
    private void Save(){var options=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};string temp=configFile+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(configuration,options));File.Move(temp,configFile,true);}
    public async Task Run(CancellationToken cancellation)
    {
                JsonObject me;
        while(true){try{me=await Call("getMe",new{},cancellation);break;}catch(Exception ex)when(ex is HttpRequestException or IOException or TaskCanceledException){if(cancellation.IsCancellationRequested)return;Console.WriteLine("Telegram temporarily unavailable; retrying connection.");await Task.Delay(5000,cancellation);}}
        if(!string.Equals(me["result"]?["username"]?.GetValue<string>(),"PlaySpaceSupport_bot",StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected bot username.");
        Console.WriteLine("Support bot connected: @"+me["result"]?["username"]);
        if(configuration.OwnerChatId==0&&string.IsNullOrEmpty(configuration.PairingCode)){configuration.PairingCode=Convert.ToHexString(RandomNumberGenerator.GetBytes(8));Save();}
        string state=Path.Combine(folder,"support-bot-offset.txt");if(File.Exists(state))long.TryParse(File.ReadAllText(state),out offset);
        while(!cancellation.IsCancellationRequested){
            try {
                var response=await Call("getUpdates",new{offset,timeout=30,allowed_updates=new[]{"message"}},cancellation);
                foreach(var update in response["result"]!.AsArray()){
                    if(update is not JsonObject item)continue;long id=item["update_id"]!.GetValue<long>();
                    if(item["message"] is JsonObject message)await Handle(message,cancellation);
                    offset=id+1;File.WriteAllText(state,offset.ToString());
                }
            }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){break;}
            catch(Exception ex)when(ex is HttpRequestException or IOException or TaskCanceledException or JsonException){Console.WriteLine("Telegram temporarily unavailable; retrying.");await Task.Delay(5000,cancellation);}
        }
    }
    internal async Task Handle(JsonObject message,CancellationToken cancellation)
    {
        long chat=message["chat"]?["id"]?.GetValue<long>()??0;string type=message["chat"]?["type"]?.GetValue<string>()??"";
        if(chat==0||type!="private")return;string text=message["text"]?.GetValue<string>()??"";
        if(text.StartsWith("/claim ",StringComparison.Ordinal)&&configuration.OwnerChatId==0){
            string supplied=text[7..].Trim();if(!string.IsNullOrEmpty(configuration.PairingCode)&&CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied),System.Text.Encoding.UTF8.GetBytes(configuration.PairingCode))){configuration.OwnerChatId=chat;configuration.PairingCode="";Save();await Send(chat,"Ты подключён как оператор поддержки. Ответ: /reply CHAT_ID текст",cancellation);}return;
        }
        if(chat==configuration.OwnerChatId){
            if(text.StartsWith("/reply ")){var parts=text.Split(' ',3,StringSplitOptions.RemoveEmptyEntries);if(parts.Length==3&&long.TryParse(parts[1],out long target)&&target>0){await Send(target,"Поддержка LauncherPlaySpace:\n\n"+parts[2],cancellation);await Send(chat,"Ответ отправлен.",cancellation);}else await Send(chat,"Формат: /reply CHAT_ID текст",cancellation);}else await Send(chat,"Ответ пользователю: /reply CHAT_ID текст. Не отправляй пользователям чужие данные.",cancellation);return;
        }
        if(text.StartsWith("/start")||text.StartsWith("/help")){await Send(chat,"Привет! Это поддержка LauncherPlaySpace.\n\nПришли версию лаунчера, название игры, описание проблемы и при необходимости скриншот. Сообщение будет передано оператору. Пароли, API-ключи, платёжные данные и коды подтверждения не присылай.\n\n/start — помощь\n/cancel — отменить ввод",cancellation);return;}
        if(text=="/cancel"){await Send(chat,"Обращение не отправлено. Можно написать новое сообщение.",cancellation);return;}
        if(configuration.OwnerChatId==0){await Send(chat,"Оператор ещё подключается. Повтори обращение позже или напиши @d3a0g.",cancellation);return;}
        if(cooldown.TryGetValue(chat,out var last)&&DateTimeOffset.UtcNow-last<TimeSpan.FromSeconds(10)){await Send(chat,"Подожди 10 секунд перед следующим сообщением.",cancellation);return;}
        if(cooldown.Count>10000)cooldown.Clear();cooldown[chat]=DateTimeOffset.UtcNow;
        await Send(configuration.OwnerChatId,$"Новое обращение LauncherPlaySpace\nChat ID: {chat}\nОтвет: /reply {chat} текст",cancellation);
        await Call("forwardMessage",new{chat_id=configuration.OwnerChatId,from_chat_id=chat,message_id=message["message_id"]!.GetValue<long>()},cancellation);
        await Send(chat,"Обращение передано поддержке. Ответ придёт сюда.",cancellation);
    }
}
internal static class Program
{
    public static async Task Main(string[] args)
    {
        string file=Path.GetFullPath(args.FirstOrDefault()??Path.Combine(AppContext.BaseDirectory,"support-bot.local.json"));
        try {
            var configuration=JsonSerializer.Deserialize<Configuration>(await File.ReadAllTextAsync(file),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new IOException();
            string? environment=Environment.GetEnvironmentVariable("PLAYSPACE_BOT_TOKEN");if(!string.IsNullOrWhiteSpace(environment))configuration.Token=environment;
            if(string.IsNullOrWhiteSpace(configuration.Token)){Console.WriteLine("Set bot token in the private config or PLAYSPACE_BOT_TOKEN. Never include it in the launcher.");Environment.ExitCode=2;return;}
            using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cancellation.Cancel();};await new Bot(configuration,file).Run(cancellation.Token);
        }catch(OperationCanceledException){}
        catch{Console.WriteLine("Support bot could not start. Check private configuration and network. Tokens are never printed.");Environment.ExitCode=1;}
    }
}
