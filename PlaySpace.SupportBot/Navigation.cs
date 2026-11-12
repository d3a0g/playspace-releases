using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlaySpace.SupportBot;

internal sealed class SupportTicket
{
    public int Id{get;set;}
    public long Chat{get;set;}
    public long LastMessageId{get;set;}
    public long AssignedTo{get;set;}
    public Dictionary<long,long> OperatorMessages{get;set;}=new();
    public string Name{get;set;}="Пользователь";
    public string Status{get;set;}="Ожидает ответа";
    public DateTimeOffset Updated{get;set;}=DateTimeOffset.UtcNow;
}
internal sealed class SupportState
{
    public List<SupportTicket> Tickets{get;set;}=new();
    public Dictionary<long,int> ReplyPrompts{get;set;}=new();
    public Dictionary<string,int> OperatorPrompts{get;set;}=new();
}
internal sealed partial class Bot
{
    private const string Repository="https://github.com/d3a0g/LauncherPlaySpace";
    private const string Latest="https://github.com/d3a0g/playspace-releases/releases/latest";
    private SupportState tickets=new();
    private string TicketFile=>Path.Combine(folder,"support-tickets.local.json");
    private void LoadTickets(){if(File.Exists(TicketFile))tickets=JsonSerializer.Deserialize<SupportState>(File.ReadAllText(TicketFile))??throw new IOException("Invalid ticket state.");}
    private void SaveTickets(){Directory.CreateDirectory(folder);File.WriteAllText(TicketFile+".tmp",JsonSerializer.Serialize(tickets));File.Move(TicketFile+".tmp",TicketFile,true);}
    private static object Button(string text,string data)=>new{text,callback_data=data};
    private static object Link(string text,string url)=>new{text,url};
    private static object Inline(params object[][] rows)=>new{inline_keyboard=rows};
    private object Keyboard(bool admin=false)=>new{resize_keyboard=true,is_persistent=true,input_field_placeholder=admin?"Выберите обращение и нажмите «Ответить»":"Выберите раздел или опишите проблему",keyboard=admin?
        new[]{new[]{"📥 Обращения","📊 Статистика"},new[]{"✅ Закрытые","📖 Помощь оператору"},new[]{"❓ FAQ","🏠 Главное меню"},new[]{"👥 Операторы"}}:
        new[]{new[]{"💬 Написать в поддержку","❓ FAQ"},new[]{"⬇️ Скачать лаунчер","🌐 Репозиторий"},new[]{"📨 Моё обращение","📚 Инструкции"},new[]{"ℹ️ О проекте","🏠 Главное меню"}}};
    private Task<JsonObject> Menu(long chat,CancellationToken ct)=>Send(chat,IsOperator(chat)?
        "🛠 Панель поддержки PlaySpace\n\nОткройте «Обращения», выберите карточку и нажмите «Ответить». Ответ отправляйте ответом на подсказку бота — адресат всегда указан. Закрытые обращения доступны отдельно.":
        "👋 Привет! Это поддержка LauncherPlaySpace.\n\n❓ FAQ — быстрые ответы\n💬 Поддержка — помощь оператора\n⬇️ Скачать — актуальная версия\n\nМожно сразу описать проблему и приложить скриншот. Пароли, API-ключи, платёжные данные и коды подтверждения не присылайте.",ct,Keyboard(IsOperator(chat)));
    internal async Task ConfigureMenus(CancellationToken ct)
    {
        await Call("setMyCommands",new{commands=new[]{new{command="start",description="Главное меню"},new{command="faq",description="Частые вопросы"},new{command="support",description="Обратиться в поддержку"},new{command="cancel",description="Вернуться в меню"}}},ct);
        await Call("setChatMenuButton",new{menu_button=new{type="commands"}},ct);
        if(configuration.OwnerChatId>0)await Call("setMyCommands",new{scope=new{type="chat",chat_id=configuration.OwnerChatId},commands=new[]{new{command="start",description="Панель поддержки"},new{command="inbox",description="Активные обращения"},new{command="cancel",description="Отменить ответ и вернуться в меню"},new{command="faq",description="Частые вопросы"}}},ct);
        await Call("setMyDescription",new{description="Поддержка LauncherPlaySpace: ответы на частые вопросы, установка и обновления, помощь с играми и модами. Напишите проблему — оператор ответит прямо здесь."},ct);
        await Call("setMyShortDescription",new{short_description="Помощь с LauncherPlaySpace · FAQ · свежая версия · связь с поддержкой"},ct);
    }
    private static readonly (string Title,string Text)[] Faq={
        ("⬇️ Установка и обновление","Скачайте ZIP последней версии по кнопке ниже, распакуйте и запустите LauncherPlaySpace.exe. Среда .NET включена. Перед заменой версии полностью закройте лаунчер, включая трей. Библиотека и настройки хранятся отдельно в %LOCALAPPDATA%\\GameLauncherPRO. update-source.json необязателен начиная с 1.0.3."),
        ("🎮 Игра не найдена","Нажмите «Найти игры». Если игра не появилась, используйте «Добавить игру» и выберите её настоящий EXE. Для Steam проверьте, что игра установлена. Если запускается папка или лаунчер вместо игры, пришлите название игры и скриншот ошибки; полный личный путь отправлять необязательно."),
        ("🏆 Часы и достижения Steam","Откройте «Настройки → Профиль Steam», укажите SteamID64 и свой Web API key, затем сохраните и обновите игры. Статистика должна быть доступна через Steam API. Часы берутся из Steam; локальный счётчик применяется к играм вне Steam. Ключ вводите только в приложении — не отправляйте его поддержке."),
        ("🧩 Установка модов","Выберите установленную игру в разделе «Моды». Возможности зависят от адаптера: каталог Thunderstore, импорт ZIP или управление модами Paradox. Поддерживаются не все игры и форматы. Перед изменениями закройте игру; сохраняйте резервные копии. Не используйте несовместимые моды с античитом."),
        ("✨ DLSS и производительность","Настройки и ожидаемый FPS зависят от игры и ПК. Экспериментальный UniversalDLSS5 требует отдельного модуля и подходящего NVIDIA runtime; он не гарантирует работу во всех играх. Не используйте экспериментальный режим в сетевых играх и с античитом. Подробные ограничения — в инструкции DLSS 5."),
        ("🔔 Трей и уведомления","В «Настройки → Приложение» можно изменить действие крестика и включить или отключить уведомления. Тот же переключатель есть в меню значка трея. Чтобы полностью выйти, выберите «Выход»; для возврата окна дважды нажмите значок трея."),
        ("🛡 Что отправить поддержке","Укажите версию Windows и лаунчера, название игры, что нажали, что ожидали и что получилось. Приложите скриншот без личных данных. Обращение и ваш Telegram-профиль видит оператор; бот сохраняет номер и статус обращения, но не ведёт локальный архив текста сообщений. Пароли и токены не нужны.")
    };
    private Task<JsonObject> ShowFaq(long chat,CancellationToken ct)=>Send(chat,"❓ Частые вопросы\n\nВыберите тему. Если ответ не помог, нажмите «Написать в поддержку».",ct,Inline(Faq.Select((entry,i)=>new[]{Button(entry.Title,"faq:"+i)}).Append(new[]{Button("💬 Написать в поддержку","support")}).ToArray()));
    private Task<JsonObject> Instructions(long chat,CancellationToken ct)=>Send(chat,"📚 Инструкции PlaySpace\n\nВыберите нужный раздел документации.",ct,Inline(
        new[]{Link("🚀 Начало работы",Repository+"#скачать-и-запустить")},
        new[]{Link("🧩 Моды",Repository+"/blob/main/docs/features/mods/MODS.md"),Link("⚙️ Производительность",Repository+"/blob/main/docs/features/performance/PERFORMANCE.md")},
        new[]{Link("✨ Ограничения DLSS 5",Repository+"/blob/main/docs/features/dlss/UNIVERSAL-DLSS5.md")},new[]{Button("❓ FAQ","faq"),Button("💬 Поддержка","support")}));
    private Task<JsonObject> AskSupport(long chat,CancellationToken ct)=>IsOperator(chat)?Menu(chat,ct):Send(chat,"💬 Опишите проблему одним сообщением:\n\n1. Версия лаунчера и Windows.\n2. Название игры или раздел приложения.\n3. Что не работает и как повторить.\n\nМожно приложить скриншот, видео или файл с пояснением. Сообщение попадёт оператору; ответ придёт сюда. Пароли и ключи не нужны.\n\nЧтобы вернуться, нажмите «Главное меню».",ct,Keyboard());
    private SupportTicket? UserTicket(long chat)=>tickets.Tickets.Where(t=>t.Chat==chat).MaxBy(t=>t.Id);
    private Task<JsonObject> Card(SupportTicket ticket,CancellationToken ct,long chat)=>Send(chat,$"📨 Обращение #{ticket.Id}\nПользователь: {ticket.Name}\nСтатус: {ticket.Status}\nОператор: {OperatorName(ticket.AssignedTo)}\nОбновлено: {ticket.Updated:dd.MM.yyyy HH:mm} UTC\n\nНажмите цитату над карточкой, чтобы перейти к последнему сообщению пользователя. Ответ отправляется только через подсказку с номером обращения.",ct,Inline(
        new[]{Button("🙋 Взять",$"take:{ticket.Id}"),Button("↩ Освободить",$"release:{ticket.Id}")},
        new[]{Button("✍️ Ответить",$"reply:{ticket.Id}"),Button("✅ Закрыть",$"close:{ticket.Id}")},
        new[]{Button("📥 К списку","queue:0"),Button("📋 Запросить подробности",$"details:{ticket.Id}")}),ticket.OperatorMessages.GetValueOrDefault(chat,chat==configuration.OwnerChatId?ticket.LastMessageId:0));
    private Task<JsonObject> Queue(long chat,bool closed,int page,CancellationToken ct)
    {
        var list=tickets.Tickets.Where(t=>(t.Status=="Закрыто")==closed).OrderByDescending(t=>t.Updated).ToList();int pages=Math.Max(1,(list.Count+7)/8);page=Math.Clamp(page,0,pages-1);
        var rows=list.Skip(page*8).Take(8).Select(t=>new[]{Button($"#{t.Id} · {t.Name[..Math.Min(t.Name.Length,24)]} · {t.Status}","ticket:"+t.Id)}).ToList();
        string route=closed?"closed:":"queue:";var nav=new List<object>();if(page>0)nav.Add(Button("← Назад",route+(page-1)));if(page+1<pages)nav.Add(Button("Далее →",route+(page+1)));if(nav.Count>0)rows.Add(nav.ToArray());rows.Add(new[]{Button("🔄 Обновить",route+page),Button("🏠 Меню","menu")});
        return Send(chat,$"{(closed?"✅ Закрытые обращения":"📥 Активные обращения")} · {list.Count}\nСтраница {page+1}/{pages}\n\n"+(list.Count==0?"Обращений пока нет.":"Выберите обращение для ответа или изменения статуса."),ct,Inline(rows.ToArray()));
    }
    internal async Task HandleCallback(JsonObject callback,CancellationToken ct)
    {
        long chat=callback["message"]?["chat"]?["id"]?.GetValue<long>()??0;long sender=callback["from"]?["id"]?.GetValue<long>()??0;
        string type=callback["message"]?["chat"]?["type"]?.GetValue<string>()??"";string data=callback["data"]?.GetValue<string>()??"";
        bool admin=IsOperator(sender)&&chat==sender;
        await Call("answerCallbackQuery",new{callback_query_id=callback["id"]?.GetValue<string>(),text=chat==sender&&type=="private"?"":"Откройте личный чат бота."},ct);
        if(chat==0||chat!=sender||type!="private")return;
        if(await OperatorCallback(chat,data,ct))return;
        if(data=="menu"){await Menu(chat,ct);return;}if(data=="faq"){await ShowFaq(chat,ct);return;}if(data=="support"){await AskSupport(chat,ct);return;}
        var parts=data.Split(':',2);if(parts.Length!=2||!int.TryParse(parts[1],out int id)||id<0)return;
        if(parts[0]=="faq"&&id<Faq.Length){await Send(chat,Faq[id].Title+"\n\n"+Faq[id].Text,ct,Inline(new[]{Button("← Все вопросы","faq"),Button("💬 Поддержка","support")},new[]{Link("⬇️ Свежая версия",Latest),Link("📚 Документация",Repository)}));return;}
        if(!admin){await Send(chat,"Эта кнопка доступна только оператору поддержки.",ct);return;}
        if(parts[0] is "queue" or "closed"){await Queue(chat,parts[0]=="closed",id,ct);return;}
        var ticket=tickets.Tickets.SingleOrDefault(t=>t.Id==id);if(ticket==null){await Send(chat,"Обращение не найдено. Обновите список.",ct);return;}
        if(parts[0]=="ticket"){await Card(ticket,ct,chat);return;}
        if(ticket.Status=="Закрыто"){await Send(chat,"Обращение закрыто. Новый вопрос пользователя создаст новое обращение.",ct);return;}
        if(parts[0]=="take"){
            if(ticket.AssignedTo!=0&&ticket.AssignedTo!=chat&&chat!=configuration.OwnerChatId){await Send(chat,"Обращение уже взято другим оператором.",ct);return;}
            ticket.AssignedTo=chat;SaveTickets();await Card(ticket,ct,chat);return;
        }
        if(parts[0]=="release"){
            if(ticket.AssignedTo!=chat&&chat!=configuration.OwnerChatId){await Send(chat,"Освободить обращение может назначенный оператор или владелец.",ct);return;}
            ticket.AssignedTo=0;SaveTickets();await Card(ticket,ct,chat);return;
        }
        if(parts[0] is not ("reply" or "close" or "details"))return;
        if(!Acquire(ticket,chat)){await Send(chat,"Обращение уже взято другим оператором. Владелец может переназначить его кнопкой «Взять».",ct);return;}
        if(parts[0]=="reply"){
            var prompt=await Send(chat,$"✍️ Ответ на обращение #{ticket.Id} · {ticket.Name}\n\nОтветьте именно на это сообщение: текстом, скриншотом или файлом. Бот отправит ответ этому пользователю. Для отмены нажмите «Главное меню».",ct,new{force_reply=true,input_field_placeholder=$"Ответ на обращение #{ticket.Id}"});
            long promptId=prompt["result"]!["message_id"]!.GetValue<long>();if(tickets.OperatorPrompts.Count>=200)tickets.OperatorPrompts.Remove(tickets.OperatorPrompts.Keys.First());tickets.OperatorPrompts[chat+":"+promptId]=ticket.Id;SaveTickets();return;
        }
        if(parts[0]=="close"){
            await Send(ticket.Chat,$"✅ Обращение #{ticket.Id} закрыто поддержкой. Если вопрос остался, напишите снова — создадим новое обращение.",ct,Keyboard());ticket.Status="Закрыто";ticket.Updated=DateTimeOffset.UtcNow;
            foreach(long key in tickets.ReplyPrompts.Where(p=>p.Value==ticket.Id).Select(p=>p.Key).ToArray())tickets.ReplyPrompts.Remove(key);foreach(string key in tickets.OperatorPrompts.Where(p=>p.Value==ticket.Id).Select(p=>p.Key).ToArray())tickets.OperatorPrompts.Remove(key);SaveTickets();await Card(ticket,ct,chat);return;
        }
        if(parts[0]=="details"){
            await Send(ticket.Chat,$"📋 По обращению #{ticket.Id} нужны подробности: версия лаунчера и Windows, название игры, шаги воспроизведения и скриншот ошибки. Пришлите их сюда без паролей и API-ключей.",ct,Keyboard());ticket.Status="Ждём пользователя";ticket.Updated=DateTimeOffset.UtcNow;SaveTickets();await Card(ticket,ct,chat);
        }
    }
    internal async Task Handle(JsonObject message,CancellationToken ct)
    {
        long chat=message["chat"]?["id"]?.GetValue<long>()??0;if(chat==0||message["chat"]?["type"]?.GetValue<string>()!="private")return;
        if(message["from"]?["id"] is JsonNode senderNode&&senderNode.GetValue<long>()!=chat)return;
        string text=message["text"]?.GetValue<string>()??"";
        if(text.StartsWith("/start op_",StringComparison.Ordinal)){await RedeemInvite(chat,text[10..].Trim(),message["from"]?["first_name"]?.GetValue<string>()??"Оператор",ct);return;}
        if(text.StartsWith("/claim ",StringComparison.Ordinal)&&configuration.OwnerChatId==0){string supplied=text[7..].Trim();if(!string.IsNullOrEmpty(configuration.PairingCode)&&CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied),System.Text.Encoding.UTF8.GetBytes(configuration.PairingCode))){configuration.OwnerChatId=chat;configuration.PairingCode="";Save();await Menu(chat,ct);}return;}
        bool admin=IsOperator(chat);
        if(text.StartsWith("/start")||text is "/help" or "/menu" or "/cancel" or "🏠 Главное меню"){
            if(admin&&text is "/cancel" or "🏠 Главное меню"){ClearPrompts(chat);SaveTickets();}await Menu(chat,ct);return;
        }
        if(text is "❓ FAQ" or "/faq"){await ShowFaq(chat,ct);return;}
        if(text=="⬇️ Скачать лаунчер"){await Send(chat,"⬇️ Актуальная версия LauncherPlaySpace\n\nОфициальная страница последнего выпуска. Скачайте ZIP для Windows x64, распакуйте и запустите EXE.",ct,Inline(new[]{Link("⬇️ Открыть последний выпуск",Latest)}));return;}
        if(text=="🌐 Репозиторий"){await Send(chat,"🌐 LauncherPlaySpace — открытый проект под лицензией MIT. Здесь исходники, скриншоты, документация и обсуждение ошибок.",ct,Inline(new[]{Link("🌐 Открыть GitHub",Repository),Link("🐞 Сообщить об ошибке",Repository+"/issues")}));return;}
        if(text=="📚 Инструкции"){await Instructions(chat,ct);return;}
        if(text=="ℹ️ О проекте"){await Send(chat,"🎮 LauncherPlaySpace объединяет игры, статистику Steam, 500 рекомендаций и управление модами для поддерживаемых игр.\n\nЭто независимый проект. Обращения передаются оператору; срок ответа не гарантируется. Бот сохраняет номера и статусы обращений, Telegram хранит отправленные сообщения. Не отправляйте секреты.",ct,Inline(new[]{Link("🌐 Проект",Repository),Link("⬇️ Скачать",Latest)}));return;}
        if(admin){
            if(text=="👥 Операторы"){if(chat==configuration.OwnerChatId)await OperatorsMenu(chat,ct);else await Send(chat,"Управление операторами доступно только владельцу.",ct);return;}
            if(text is "📥 Обращения" or "/inbox"){await Queue(chat,false,0,ct);return;}if(text=="✅ Закрытые"){await Queue(chat,true,0,ct);return;}
            if(text=="📊 Статистика"){await Send(chat,$"📊 Обращения\n\nВсего: {tickets.Tickets.Count}\nОжидают ответа: {tickets.Tickets.Count(t=>t.Status=="Ожидает ответа")}\nЖдём пользователя: {tickets.Tickets.Count(t=>t.Status=="Ждём пользователя")}\nОтвет отправлен: {tickets.Tickets.Count(t=>t.Status=="Ответ отправлен")}\nЗакрыто: {tickets.Tickets.Count(t=>t.Status=="Закрыто")}",ct,Keyboard(true));return;}
            if(text=="📖 Помощь оператору"){await Send(chat,"📖 Как отвечать\n\n1. Откройте «Обращения» и выберите карточку.\n2. Нажмите «Ответить».\n3. Напишите ответом на подсказку бота. Адресат и номер указаны в подсказке. Можно отправить файл или изображение.\n4. Если данных мало, нажмите «Запросить подробности». Когда вопрос решён — «Закрыть».\n\n«Главное меню» отменяет активные подсказки. Обычные сообщения оператора никому не пересылаются.",ct,Keyboard(true));return;}
            long replyId=message["reply_to_message"]?["message_id"]?.GetValue<long>()??0;
            if(tickets.OperatorPrompts.TryGetValue(chat+":"+replyId,out int ticketId)||(chat==configuration.OwnerChatId&&tickets.ReplyPrompts.TryGetValue(replyId,out ticketId))){
                var ticket=tickets.Tickets.Single(t=>t.Id==ticketId);if(ticket.Status=="Закрыто"){await Send(chat,"Обращение уже закрыто.",ct);return;}
                if(!Acquire(ticket,chat)){await Send(chat,"Обращение уже назначено другому оператору. Ответ не отправлен.",ct);return;}
                await Call("copyMessage",new{chat_id=ticket.Chat,from_chat_id=chat,message_id=message["message_id"]!.GetValue<long>()},ct);
                if(chat==configuration.OwnerChatId)tickets.ReplyPrompts.Remove(replyId);tickets.OperatorPrompts.Remove(chat+":"+replyId);ticket.Status="Ответ отправлен";ticket.Updated=DateTimeOffset.UtcNow;SaveTickets();await Send(chat,$"✅ Ответ по обращению #{ticket.Id} отправлен.",ct,Keyboard(true));await Card(ticket,ct,chat);return;
            }
            // Keep the existing command as an optional compatibility path; buttons need no chat IDs.
            if(text.StartsWith("/reply ")){var parts=text.Split(' ',3,StringSplitOptions.RemoveEmptyEntries);if(parts.Length==3&&long.TryParse(parts[1],out long target)&&tickets.Tickets.Any(t=>t.Chat==target&&t.Status!="Закрыто"&&(t.AssignedTo==0||t.AssignedTo==chat))){var legacyTicket=tickets.Tickets.Last(t=>t.Chat==target&&t.Status!="Закрыто");if(!Acquire(legacyTicket,chat))return;await Send(target,"Поддержка LauncherPlaySpace:\n\n"+parts[2],ct,Keyboard());await Send(chat,"✅ Ответ отправлен.",ct,Keyboard(true));return;}}
            await Send(chat,"Выберите обращение и нажмите «Ответить». Затем ответьте на подсказку бота — так сообщение попадёт нужному пользователю.",ct,Keyboard(true));return;
        }
        if(text is "💬 Написать в поддержку" or "/support"){await AskSupport(chat,ct);return;}
        if(text=="📨 Моё обращение"){var last=UserTicket(chat);await Send(chat,last==null?"Вы ещё не отправляли обращений. Нажмите «Написать в поддержку». Обмен сообщениями начнётся после отправки описания проблемы.":$"📨 Обращение #{last.Id}\nСтатус: {last.Status}\n\nОтвет придёт в этот чат. Для дополнения просто отправьте сообщение.",ct,Keyboard());return;}
        if(text.StartsWith('/')){await Menu(chat,ct);return;}
        if(configuration.OwnerChatId==0){await Send(chat,"Оператор пока не подключён. Попробуйте позже; FAQ и инструкции доступны в меню.",ct,Keyboard());return;}
        if(cooldown.TryGetValue(chat,out var lastSent)&&DateTimeOffset.UtcNow-lastSent<TimeSpan.FromSeconds(2)){await Send(chat,"Подождите пару секунд перед следующим сообщением, затем отправьте его ещё раз.",ct);return;}
        if(cooldown.Count>10000)cooldown.Clear();
        if(message["text"]==null&&message["photo"]==null&&message["document"]==null&&message["video"]==null&&message["voice"]==null&&message["audio"]==null){await Send(chat,"Пришлите текст, скриншот, видео, аудио или документ с описанием проблемы.",ct,Keyboard());return;}
        var current=UserTicket(chat);if(current==null||current.Status=="Закрыто"){
            string name=message["from"]?["first_name"]?.GetValue<string>()??"Пользователь";
            current=new SupportTicket{Id=tickets.Tickets.Count==0?1:tickets.Tickets.Max(t=>t.Id)+1,Chat=chat,Name=name[..Math.Min(name.Length,80)]};tickets.Tickets.Add(current);
        }
        current.Status="Ожидает ответа";current.Updated=DateTimeOffset.UtcNow;SaveTickets();
        int delivered=0;
        foreach(long operatorChat in OperatorChats()){
            try{
                var forwarded=await Call("forwardMessage",new{chat_id=operatorChat,from_chat_id=chat,message_id=message["message_id"]!.GetValue<long>()},ct);
                long forwardedId=forwarded["result"]!["message_id"]!.GetValue<long>();
                current.OperatorMessages[operatorChat]=forwardedId;if(operatorChat==configuration.OwnerChatId)current.LastMessageId=forwardedId;
                SaveTickets();await Card(current,ct,operatorChat);delivered++;
            }catch(IOException){Console.WriteLine("An operator notification could not be delivered; ticket retained.");}
        }
        if(delivered==0){await Send(chat,"Не удалось уведомить операторов. Обращение сохранено в очереди; попробуйте дополнить его позже.",ct,Keyboard());return;}
        cooldown[chat]=DateTimeOffset.UtcNow;await Send(chat,$"✅ Обращение #{current.Id} передано поддержке. Ответ придёт сюда. Дополнения можно отправлять в этот же чат.",ct,Keyboard());
    }
}
