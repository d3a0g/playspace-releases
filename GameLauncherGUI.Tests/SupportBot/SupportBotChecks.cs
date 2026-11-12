using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaySpace.SupportBot;

internal static class SupportBotChecks
{
    private sealed class TelegramFixture : HttpMessageHandler
    {
        public readonly List<(string Method,JsonObject Body,long Id)> Calls=new();
        public bool CollidingPromptIds;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject();long id=Calls.Count+1000;
            string method=request.RequestUri!.Segments.Last();
            if(CollidingPromptIds&&method=="sendMessage"&&body["reply_markup"]?["force_reply"]?.GetValue<bool>()==true)id=9000;
            if(method=="sendMessage"&&(body.ContainsKey("reply_markup")&&body["reply_markup"]==null||body.ContainsKey("reply_parameters")&&body["reply_parameters"]==null))
                return new(HttpStatusCode.BadRequest){Content=new StringContent("{\"ok\":false,\"description\":\"Bad Request: reply parameters must be an object\"}")};
            Calls.Add((method,body,id));
            return new(HttpStatusCode.OK){Content=new StringContent($"{{\"ok\":true,\"result\":{{\"message_id\":{id}}}}}")};
        }
    }
    public static void Run()
    {
        int count=0;void Check(bool condition,string message){if(!condition)throw new Exception(message);count++;}
        using(var direct=BotTransport.CreateHandler(null))Check(!direct.UseProxy,"Bot inherits system proxy instead of explicit direct route");
        foreach(string scheme in new[]{"http","https","socks5"}){
            using var transport=BotTransport.CreateHandler(scheme+"://fixture%40user:p%3Ass@127.0.0.1:19080");
            var destination=new Uri("https://api.telegram.org");var proxy=transport.Proxy!;
            Check(transport.UseProxy&&proxy.GetProxy(destination)!.Port==19080,"Explicit proxy route lost: "+scheme);
            var credential=proxy.Credentials!.GetCredential(destination,"Basic")!;
            Check(credential.UserName=="fixture@user"&&credential.Password=="p:ss","Escaped proxy credentials lost");
            Check(string.IsNullOrEmpty(proxy.GetProxy(destination)!.UserInfo),"Proxy URL retains credentials");
        }
        foreach(string invalid in new[]{"tg://proxy?server=fixture","ftp://127.0.0.1:1080","not-a-url","http://127.0.0.1:1080/path"}){
            bool rejected=false;try{using var unused=BotTransport.CreateHandler(invalid);}catch(IOException){rejected=true;}
            Check(rejected,"Invalid / MTProto proxy accepted");
        }
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
        Callback(200,"inviteop");Check(config.OperatorInvites.Count==0,"User created operator invitation");
        Callback(100,"inviteop");
        string invite=handler.Calls.Last().Body["text"]!.GetValue<string>().Split("start=op_")[1];
        Check(!File.ReadAllText(configPath).Contains(invite),"Raw invitation stored in private config");
        Handle(300,"/start op_"+invite);Check(config.Operators.Any(o=>o.ChatId==300)&&config.OperatorInvites.Count==0,"Operator invitation not redeemed");
        Handle(301,"/start op_"+invite);Check(!config.Operators.Any(o=>o.ChatId==301),"Invitation reused");
        Callback(300,"inviteop");Check(config.OperatorInvites.Count==0,"Operator invited another operator");
        Callback(100,"inviteop");string expired=handler.Calls.Last().Body["text"]!.GetValue<string>().Split("start=op_")[1];
        config.OperatorInvites[0].Expires=DateTimeOffset.UtcNow.AddSeconds(-1);Handle(302,"/start op_"+expired);Check(!config.Operators.Any(o=>o.ChatId==302),"Expired invitation accepted");
        Callback(100,"inviteop");string cancelled=handler.Calls.Last().Body["text"]!.GetValue<string>().Split("start=op_")[1];Callback(100,"cancelinvites");Handle(302,"/start op_"+cancelled);Check(!config.Operators.Any(o=>o.ChatId==302),"Revoked invitation accepted");
        Handle(800,"multi-operator issue");Check(Delivered("forwardMessage",300)==1,"Operator did not receive issue");
        int Ticket(long user)=>JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"support-tickets.local.json")))!["Tickets"]!.AsArray().Last(t=>t!["Chat"]!.GetValue<long>()==user)!["Id"]!.GetValue<int>();
        int first=Ticket(800);Callback(300,"take:"+first);Callback(100,"reply:"+first);
        Check(handler.Calls.Last().Body["reply_markup"]?["force_reply"]==null,"Owner replied to another operator's ticket without reassignment");
        Callback(100,"take:"+first);Callback(300,"close:"+first);Check(handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("другим оператором"),"Other operator closed assigned issue");
        Callback(100,"release:"+first);Callback(300,"reply:"+first);long operatorPrompt=handler.Calls.Last().Id;
        var loaded=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(configPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        bot=new Bot(loaded,configPath,client);Handle(300,"answer after operator restart",reply:operatorPrompt);Check(Delivered("copyMessage",800)==1,"Operator access/reply did not survive restart");
        Handle(801,"another issue");int second=Ticket(801);handler.CollidingPromptIds=true;
        Callback(100,"reply:"+second);Callback(300,"reply:"+first);Handle(300,"/cancel");Handle(100,"owner answer",reply:9000);
        Check(Delivered("copyMessage",801)==1,"Operator cancellation removed owner's same-ID prompt");
        Callback(300,"reply:"+first);Callback(100,"removeop:300");Check(loaded.Operators.Count==1,"Removal did not require confirmation");Callback(100,"confirmremoveop:300");Check(loaded.Operators.Count==0,"Operator not removed");
        before=Delivered("copyMessage",800);Handle(300,"revoked answer",reply:9000);Check(Delivered("copyMessage",800)==before,"Revoked operator replied with stale prompt");
        Callback(100,"reply:"+first);Check(handler.Calls.Last().Body["reply_markup"]?["force_reply"]?.GetValue<bool>()==true,"Revocation failed to release assigned ticket");
        int former=Delivered("forwardMessage",300);Handle(802,"post-revocation issue");Check(Delivered("forwardMessage",300)==former,"Revoked operator received new issue");
        Callback(100,"confirmremoveop:100");Check(loaded.OwnerChatId==100,"Owner removed through operator controls");
        Console.WriteLine($"PASS: {count} support bot checks: existing menus/tickets, operator invitations/expiry/reuse/revocation, assignment, chat-bound prompts, restart and owner-only controls. Mock Telegram only.");
    }
}
