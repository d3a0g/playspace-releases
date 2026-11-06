using System.Net;
using System.Text.Json.Nodes;
using PlaySpace.SupportBot;

internal static class SupportBotChecks
{
    private sealed class TelegramFixture : HttpMessageHandler
    {
        public readonly List<(string Method,JsonObject Body)> Calls=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject();Calls.Add((request.RequestUri!.Segments.Last(),body));
            return new(HttpStatusCode.OK){Content=new StringContent("{\"ok\":true,\"result\":{\"message_id\":1}}")};
        }
    }
    public static void Run()
    {
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceBotChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var configuration=new Configuration{Token="fixture-not-a-real-token",PairingCode="fixture-pair-code"};using var handler=new TelegramFixture();using var client=new HttpClient(handler);var bot=new Bot(configuration,Path.Combine(folder,"config.json"),client);
        JsonObject Message(long chat,string text,string type="private")=>JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new{chat=new{id=chat,type},text,message_id=123}))!.AsObject();
        void Handle(long chat,string text,string type="private")=>bot.Handle(Message(chat,text,type),default).GetAwaiter().GetResult();
        Handle(100,"/claim wrong");if(configuration.OwnerChatId!=0)throw new Exception("Wrong pairing code accepted");
        Handle(100,"/claim fixture-pair-code");if(configuration.OwnerChatId!=100||configuration.PairingCode.Length!=0)throw new Exception("Operator pairing failed");
        int before=handler.Calls.Count;Handle(200,"test","group");if(handler.Calls.Count!=before)throw new Exception("Group support messages accepted");
        Handle(200,"/start");if(!handler.Calls.Last().Body["text"]!.GetValue<string>().Contains("Пароли"))throw new Exception("Support onboarding missing privacy guidance");
        Handle(200,"fixture issue");if(!handler.Calls.Any(c=>c.Method=="forwardMessage"&&c.Body["chat_id"]!.GetValue<long>()==100&&c.Body["from_chat_id"]!.GetValue<long>()==200))throw new Exception("Support message wasn't forwarded to operator");
        int forwards=handler.Calls.Count(c=>c.Method=="forwardMessage");Handle(200,"second issue");if(handler.Calls.Count(c=>c.Method=="forwardMessage")!=forwards)throw new Exception("Support flood control failed");
        Handle(100,"/reply 200 fixture answer");if(!handler.Calls.Any(c=>c.Method=="sendMessage"&&c.Body["chat_id"]!.GetValue<long>()==200&&c.Body["text"]!.GetValue<string>().Contains("fixture answer")))throw new Exception("Operator reply failed");
        before=handler.Calls.Count(c=>c.Method=="sendMessage"&&c.Body["chat_id"]!.GetValue<long>()==999);Handle(300,"/reply 999 unauthorized");if(handler.Calls.Count(c=>c.Method=="sendMessage"&&c.Body["chat_id"]!.GetValue<long>()==999)!=before)throw new Exception("Non-operator could send an arbitrary reply");
        Console.WriteLine("PASS: support bot pairing, private chat filter, forwarding, operator reply, authorization and cooldown. Mock Telegram only; no real messages sent.");
    }
}
