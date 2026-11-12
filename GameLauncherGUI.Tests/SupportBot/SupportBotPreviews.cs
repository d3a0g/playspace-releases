using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaySpace.SupportBot;

internal static class SupportBotPreviews
{
    private sealed class Capture : HttpMessageHandler
    {
        internal readonly List<JsonObject> Messages=new();
        private int id=100;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            if(request.RequestUri!.Segments.Last()=="sendMessage")Messages.Add(body);
            return new(HttpStatusCode.OK){Content=new StringContent($"{{\"ok\":true,\"result\":{{\"message_id\":{++id}}}}}")};
        }
    }
    internal static void Run()
    {
        string root=Directory.GetCurrentDirectory();string output=Path.Combine(root,"artifacts/bot-previews");Directory.CreateDirectory(output);
        string temp=Path.Combine(Path.GetTempPath(),"PlaySpaceBotPreview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try{
            using var capture=new Capture();using var client=new HttpClient(capture);
            var bot=new Bot(new(){Token="preview-placeholder",OwnerChatId=100,Operators=new(){new(){ChatId=300,Name="Оператор"}}},Path.Combine(temp,"config.json"),client);
            void Message(long chat,string text)=>bot.Handle(JsonNode.Parse(JsonSerializer.Serialize(new{chat=new{id=chat,type="private"},from=new{id=chat,first_name="Демо-пользователь"},message_id=1,text}))!.AsObject(),default).GetAwaiter().GetResult();
            JsonObject Last(long chat)=>capture.Messages.Last(m=>m["chat_id"]!.GetValue<long>()==chat);
            Message(200,"/start");var user=Last(200);Render("ПОЛЬЗОВАТЕЛЬ",new[]{user},user["reply_markup"]!,Path.Combine(output,"user.png"));
            Message(100,"/start");var adminKeyboard=Last(100)["reply_markup"]!;Message(100,"👥 Операторы");Render("ВЛАДЕЛЕЦ / АДМИНИСТРАТОР",new[]{Last(100)},adminKeyboard,Path.Combine(output,"admin.png"));
            Message(300,"/start");var operatorKeyboard=Last(300)["reply_markup"]!;Message(200,"Демонстрационное обращение");
            bot.HandleCallback(JsonNode.Parse("{\"id\":\"preview\",\"from\":{\"id\":300},\"message\":{\"chat\":{\"id\":300,\"type\":\"private\"}},\"data\":\"take:1\"}")!.AsObject(),default).GetAwaiter().GetResult();
            Render("ОПЕРАТОР · ОБРАЩЕНИЕ В РАБОТЕ",new[]{Last(300)},operatorKeyboard,Path.Combine(output,"operator.png"));
            Console.WriteLine("Saved 3 Telegram-style previews using actual bot response payloads. Mock API and fictional account data only: "+output);
        }finally{Directory.Delete(temp,true);}
    }
    private static void Render(string role,IEnumerable<JsonObject> messages,JsonNode keyboard,string file)
    {
        using var bitmap=new Bitmap(900,1030);using var g=Graphics.FromImage(bitmap);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        Color background=Color.FromArgb(14,23,32),panel=Color.FromArgb(23,35,47),bubble=Color.FromArgb(30,48,64),button=Color.FromArgb(39,57,75),white=Color.FromArgb(235,242,249),muted=Color.FromArgb(143,164,182);
        g.Clear(background);using var font=new Font("Segoe UI",13);using var small=new Font("Segoe UI",10);using var bold=new Font("Segoe UI",14,FontStyle.Bold);
        void Box(Color color,float x,float y,float w,float h){using var brush=new SolidBrush(color);g.FillRectangle(brush,x,y,w,h);}
        void Text(string text,Font f,Color color,RectangleF rect,bool center=false){text=System.Text.RegularExpressions.Regex.Replace(text,@"[\p{Cs}\p{So}\uFE0F\u200D]","");using var brush=new SolidBrush(color);using var format=new StringFormat{Alignment=center?StringAlignment.Center:StringAlignment.Near,LineAlignment=center?StringAlignment.Center:StringAlignment.Near};g.DrawString(text,f,brush,rect,format);}
        Box(panel,0,0,900,120);Text("МАКЕТ TELEGRAM · "+role,small,Color.FromArgb(177,153,255),new(24,12,850,23));
        string avatar=Path.Combine(Directory.GetCurrentDirectory(),"PlaySpace.SupportBot/Assets/support-avatar.png");
        if(File.Exists(avatar)){using var icon=Image.FromFile(avatar);var saved=g.Save();using var clip=new GraphicsPath();clip.AddEllipse(24,48,54,54);g.SetClip(clip);g.DrawImage(icon,new Rectangle(24,48,54,54));g.Restore(saved);}
        Text("LauncherPlaySpace Support",bold,white,new(95,48,750,30));Text("бот · @PlaySpaceSupport_bot",small,muted,new(95,81,750,25));
        int keyboardRows=keyboard["keyboard"]!.AsArray().Count;float keyboardTop=1030-46-keyboardRows*49-20;
        float y=145;
        foreach(var message in messages){
            string text=message["text"]!.GetValue<string>();
            // Timestamps in live ticket payloads are replaced with a stable demonstration value.
            text=System.Text.RegularExpressions.Regex.Replace(text,@"Обновлено: .*? UTC","Обновлено: 06.11.2026 12:00 UTC");
            float height=g.MeasureString(text,font,760).Height+34;Box(bubble,26,y,810,height);Text(text,font,white,new(44,y+16,770,height-24));y+=height+8;
            if(message["reply_markup"]?["inline_keyboard"] is JsonArray inline){foreach(var row in inline.AsArray()){
                var buttons=row!.AsArray();float width=810f/buttons.Count;
                for(int i=0;i<buttons.Count;i++){Box(button,26+i*width,y,width-5,44);Text(buttons[i]!["text"]!.GetValue<string>(),small,white,new(30+i*width,y,width-13,44),true);}y+=50;
            }}
        }
        Box(panel,0,keyboardTop-49,900,1030-keyboardTop+49);Text("Menu",small,Color.FromArgb(117,191,245),new(24,keyboardTop-36,70,25));Text("Написать сообщение…",font,muted,new(110,keyboardTop-39,700,30));
        float rowY=keyboardTop;foreach(var row in keyboard["keyboard"]!.AsArray()){
            var keys=row!.AsArray();float width=864f/keys.Count;
            for(int i=0;i<keys.Count;i++){Box(button,18+i*width,rowY,width-7,42);Text(keys[i]!.GetValue<string>(),small,white,new(22+i*width,rowY,width-15,42),true);}rowY+=49;
        }
        Text("Превью по коду бота · демонстрационные данные · не снимок Telegram",small,muted,new(20,1000,860,24),true);
        bitmap.Save(file,System.Drawing.Imaging.ImageFormat.Png);
    }
}
