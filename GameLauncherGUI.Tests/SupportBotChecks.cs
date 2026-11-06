using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaySpace.SupportBot;

internal static class SupportBotChecks
{
    private sealed class TelegramFixture : HttpMessageHandler
    {
        public readonly List<(string Method,JsonObject Body,long Id)> Calls=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject();long id=Calls.Count+1000;
            string method=request.RequestUri!.Segments.Last();
            if(method=="sendMessage"&&(body.ContainsKey("reply_markup")&&body["reply_markup"]==null||body.ContainsKey("reply_parameters")&&body["reply_parameters"]==null))
                return new(HttpStatusCode.BadRequest){Content=new StringContent("{\"ok\":false,\"description\":\"Bad Request: reply parameters must be an object\"}")};
            Calls.Add((method,body,id));
            return new(HttpStatusCode.OK){Content=new StringContent($"{{\"ok\":true,\"result\":{{\"message_id\":{id}}}}}")};
        }
    }
    public static void Run()
    {
        int count=0;void Check(bool condition,string message){if(!condition)throw new Exception(message);count++;}
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceBotChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var config=new Configuration{Token="fixture-not-a-real-token",PairingCode="fixture-pair-code"};using var handler=new TelegramFixture();using var client=new HttpClient(handler);
        string configPath=Path.Combine(folder,"config.json");var bot=new Bot(config,configPath,client);
        JsonObject Message(long chat,string text,string type="private",long reply=0)=>JsonNode.Parse(JsonSerializer.Serialize(new{chat=new{id=chat,type},from=new{first_name="Test user"},text,message_id=123,reply_to_message=new{message_id=reply}}))!.AsObject();
        void Handle(long chat,string text,string type="private",long reply=0)=>bot.Handle(Message(chat,text,type,reply),default).GetAwaiter().GetResult();
        void Callback(long chat,string data,long? sender=null,string type="private")=>bot.HandleCallback(JsonNode.Parse(JsonSerializer.Serialize(new{id="fixture-callback",from=new{id=sender??chat},message=new{chat=new{id=chat,type}},data}))!.AsObject(),default).GetAwaiter().GetResult();
        int Delivered(string method,long chat)=>handler.Calls.Count(c=>c.Method==method&&c.Body["chat_id"]?.GetValue<long>()==chat);
        Handle(100,"/claim wrong");Check(config.OwnerChatId==0,"Wrong pairing code accepted");
        Handle(100,"/claim fixture-pair-code");Check(config.OwnerChatId==100&&config.PairingCode.Length==0,"Operator pairing failed");
        Check(handler.Calls.Last().Body["reply_markup"]?["keyboard"]!=null,"Operator keyboard missing");
        int before=handler.Calls.Count;Handle(200,"test","group");Check(handler.Calls.Count==before,"Group message accepted");
        Handle(200,"/start");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("Пароли")&&handler.Calls.Last().Body["reply_markup"]?["keyboard"]?.AsArray().Count==4,"User onboarding/keyboard missing");
        int forwarded=Delivered("forwardMessage",100);Handle(200,"❓ FAQ");Callback(200,"faq:2");
        Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("SteamID64"),"Steam FAQ missing");
        Check(Delivered("forwardMessage",100)==forwarded,"FAQ created a support ticket");
        Handle(200,"🌐 Репозиторий");Check(handler.Calls.Last().Body.ToJsonString().Contains("https://github.com/d3a0g/LauncherPlaySpace"),"Repository URL incorrect");
        Handle(200,"⬇️ Скачать лаунчер");Check(handler.Calls.Last().Body.ToJsonString().Contains("/releases/latest"),"Download isn't latest release");
        Handle(200,"💬 Написать в поддержку");Check(Delivered("forwardMessage",100)==forwarded,"Support button was forwarded as an issue");
        Handle(200,"fixture issue secret body");Check(Delivered("forwardMessage",100)==forwarded+1,"Issue wasn't forwarded");
        Check(handler.Calls.Any(c=>c.Body["reply_markup"]?.ToJsonString().Contains("reply:1")==true),"Ticket action buttons missing");
        Handle(200,"second issue");Check(Delivered("forwardMessage",100)==forwarded+1,"Flood control failed");
        Handle(200,"📨 Моё обращение");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("#1"),"User ticket status unavailable");
        Handle(100,"ordinary private operator note");Check(Delivered("copyMessage",200)==0,"Operator note leaked");
        Callback(200,"reply:1");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("только оператору"),"User could activate operator controls");
        before=Delivered("sendMessage",100);Callback(100,"close:1",sender:300);Check(Delivered("sendMessage",100)==before,"Forged callback accepted");
        Callback(100,"reply:1");long prompt=handler.Calls.Last().Id;Check(handler.Calls.Last().Body["reply_markup"]?["force_reply"]?.GetValue<bool>()==true,"Reply prompt not bound to message");
        bot=new Bot(config,configPath,client);Handle(100,"fixture answer",reply:prompt);
        Check(Delivered("copyMessage",200)==1,"Button reply wasn't delivered after restart");
        Check(handler.Calls.Single(c=>c.Method=="copyMessage").Body["message_id"]!.GetValue<long>()==123,"Reply copied wrong message");
        Handle(100,"duplicate reply",reply:prompt);Check(Delivered("copyMessage",200)==1,"Stale prompt sent a duplicate reply");
        Callback(100,"details:1");Check(handler.Calls.Any(c=>c.Body["chat_id"]?.GetValue<long>()==200&&c.Body["text"]?.GetValue<string>().Contains("нужны подробности")==true),"Request-details button failed");
        Callback(100,"reply:1");prompt=handler.Calls.Last().Id;Handle(100,"🏠 Главное меню");Handle(100,"cancelled reply",reply:prompt);Check(Delivered("copyMessage",200)==1,"Cancelled reply was sent");
        Callback(100,"close:1");Handle(200,"📨 Моё обращение");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("Закрыто"),"Ticket wasn't closed");
        Callback(100,"reply:1");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("закрыто"),"Closed ticket accepted reply prompt");
        bot=new Bot(config,configPath,client);Handle(200,"new issue after closure");Handle(200,"📨 Моё обращение");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("#2"),"New issue didn't get a new ticket");
        Handle(100,"/reply 200 legacy answer");Check(handler.Calls.Any(c=>c.Body["chat_id"]?.GetValue<long>()==200&&c.Body["text"]?.GetValue<string>().Contains("legacy answer")==true),"Legacy replies broken");
        before=Delivered("sendMessage",999);Handle(300,"/reply 999 unauthorized");Check(Delivered("sendMessage",999)==before,"Unauthorized command reply sent");
        for(int i=0;i<10;i++)Handle(400+i,"pagination issue");Callback(100,"queue:0");Check(handler.Calls.Last().Body.ToJsonString().Contains("queue:1"),"Queue pagination missing");
        Callback(100,"queue:1");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("2/2"),"Queue next page failed");
        Callback(100,"closed:0");Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("Закрытые"),"Closed queue missing");
        string state=File.ReadAllText(Path.Combine(folder,"support-tickets.local.json"));Check(!state.Contains("fixture issue secret body")&&!state.Contains(config.Token),"Ticket state stored message text or token");
        bot.ConfigureMenus(default).GetAwaiter().GetResult();Check(handler.Calls.Any(c=>c.Method=="setMyCommands")&&handler.Calls.Any(c=>c.Method=="setChatMenuButton"),"Telegram menu configuration missing");
        Check(handler.Calls.Where(c=>c.Method=="sendMessage").All(c=>!c.Body.ContainsKey("reply_parameters")||c.Body["reply_parameters"]!=null),"Null reply parameters were sent to Telegram");
        Check(handler.Calls.Where(c=>c.Method=="sendMessage").All(c=>!c.Body.ContainsKey("reply_markup")||c.Body["reply_markup"]!=null),"Null keyboard was sent to Telegram");
        Console.WriteLine($"PASS: {count} support bot checks: keyboards, FAQ/links, tickets/pagination, callback authorization, reply binding/restart/cancellation, closure, privacy and cooldown. Mock Telegram only.");
    }
}
