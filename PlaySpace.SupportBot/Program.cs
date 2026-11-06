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
internal sealed partial class Bot
{
    private static readonly JsonSerializerOptions ApiJson=new(){DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    private readonly Configuration configuration;private readonly HttpClient http;private readonly string folder,configFile;private long offset;private readonly Dictionary<long,DateTimeOffset> cooldown=new();
    public Bot(Configuration configuration,string configFile,HttpClient? client=null){this.configuration=configuration;this.configFile=configFile;folder=Path.GetDirectoryName(configFile)!;http=client??CreateHttp(configuration);LoadTickets();}
    private static HttpClient CreateHttp(Configuration configuration)
    {
        var handler=new HttpClientHandler();
        if(!string.IsNullOrWhiteSpace(configuration.ProxyUrl)){
            var uri=new Uri(configuration.ProxyUrl);if(uri.Scheme!="http"&&uri.Scheme!="https"&&uri.Scheme!="socks5")throw new IOException("Unsupported proxy scheme.");handler.Proxy=new System.Net.WebProxy(uri);handler.UseProxy=true;
        }
        return new HttpClient(handler){Timeout=TimeSpan.FromSeconds(50)};
    }
    private string Url(string method)=>$"https://api.telegram.org/bot{configuration.Token}/{method}";
    private async Task<JsonObject> Call(string method,object payload,CancellationToken cancellation)
    {
        using var response=await http.PostAsJsonAsync(Url(method),payload,ApiJson,cancellation);
        var result=JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation))!.AsObject();
        if(!response.IsSuccessStatusCode||result["ok"]?.GetValue<bool>()!=true){
            string reason=(result["description"]?.GetValue<string>()??"Request rejected").Replace(configuration.Token,"[redacted]");
            throw new IOException($"Telegram {method} ({(int)response.StatusCode}): {reason[..Math.Min(reason.Length,240)]}");
        }
        return result;
    }
    private Task<JsonObject> Send(long chat,string text,CancellationToken cancellation,object? markup=null,long replyTo=0)=>Call("sendMessage",new{chat_id=chat,text,disable_web_page_preview=true,reply_markup=markup,reply_parameters=replyTo>0?new{message_id=replyTo,allow_sending_without_reply=true}:null},cancellation);
    private void Save(){var options=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};string temp=configFile+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(configuration,options));File.Move(temp,configFile,true);}
    public async Task Run(CancellationToken cancellation)
    {
                JsonObject me;
        while(true){try{me=await Call("getMe",new{},cancellation);break;}catch(Exception ex)when(ex is HttpRequestException or IOException or TaskCanceledException){if(cancellation.IsCancellationRequested)return;Console.WriteLine("Telegram temporarily unavailable; retrying connection.");await Task.Delay(5000,cancellation);}}
        if(!string.Equals(me["result"]?["username"]?.GetValue<string>(),"PlaySpaceSupport_bot",StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected bot username.");
        Console.WriteLine("Support bot connected: @"+me["result"]?["username"]);
        if(configuration.OwnerChatId==0&&string.IsNullOrEmpty(configuration.PairingCode)){configuration.PairingCode=Convert.ToHexString(RandomNumberGenerator.GetBytes(8));Save();}
        await ConfigureMenus(cancellation);
        string state=Path.Combine(folder,"support-bot-offset.txt");if(File.Exists(state))long.TryParse(File.ReadAllText(state),out offset);
        while(!cancellation.IsCancellationRequested){
            try {
                var response=await Call("getUpdates",new{offset,timeout=30,allowed_updates=new[]{"message","callback_query"}},cancellation);
                foreach(var update in response["result"]!.AsArray()){
                    if(update is not JsonObject item)continue;long id=item["update_id"]!.GetValue<long>();
                    if(item["message"] is JsonObject message)await Handle(message,cancellation);
                    if(item["callback_query"] is JsonObject callback)await HandleCallback(callback,cancellation);
                    offset=id+1;File.WriteAllText(state,offset.ToString());
                }
            }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){break;}
            catch(Exception ex)when(ex is HttpRequestException or IOException or TaskCanceledException or JsonException){Console.WriteLine($"{DateTimeOffset.UtcNow:O} "+(ex is IOException?ex.Message:"Telegram connection failed: "+ex.GetType().Name)+"; retrying.");await Task.Delay(5000,cancellation);}
        }
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
