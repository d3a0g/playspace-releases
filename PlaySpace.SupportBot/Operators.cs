using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PlaySpace.SupportBot;

internal sealed class SupportOperator
{
    public long ChatId{get;set;}
    public string Name{get;set;}="Оператор";
}
internal sealed class OperatorInvite
{
    public string Hash{get;set;}="";
    public DateTimeOffset Expires{get;set;}
}
internal sealed partial class Bot
{
    private bool IsOperator(long chat)=>chat>0&&(chat==configuration.OwnerChatId||configuration.Operators.Any(o=>o.ChatId==chat));
    private IEnumerable<long> OperatorChats()=>configuration.Operators.Select(o=>o.ChatId).Append(configuration.OwnerChatId).Where(id=>id>0).Distinct();
    private static string InviteHash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private string OperatorName(long chat)=>chat==configuration.OwnerChatId?"Владелец":configuration.Operators.FirstOrDefault(o=>o.ChatId==chat)?.Name??"Не назначен";
    private void ClearPrompts(long chat)
    {
        foreach(var key in tickets.OperatorPrompts.Keys.Where(k=>k.StartsWith(chat+":",StringComparison.Ordinal)).ToArray())tickets.OperatorPrompts.Remove(key);
        if(chat==configuration.OwnerChatId)tickets.ReplyPrompts.Clear();
    }
    private bool Acquire(SupportTicket ticket,long chat)
    {
        if(ticket.AssignedTo!=0&&ticket.AssignedTo!=chat)return false;
        ticket.AssignedTo=chat;SaveTickets();return true;
    }
    private Task<JsonObject> OperatorsMenu(long chat,CancellationToken ct)
    {
        var rows=configuration.Operators.Select(o=>new[]{Button("Удалить · "+o.Name[..Math.Min(o.Name.Length,24)],"removeop:"+o.ChatId)}).ToList();
        rows.Add(new[]{Button("➕ Пригласить оператора","inviteop"),Button("Отозвать приглашения","cancelinvites")});
        rows.Add(new[]{Button("🏠 Меню","menu")});
        return Send(chat,$"👥 Операторы · {configuration.Operators.Count}\n\nВладелец: вы. Операторы видят обращения и могут отвечать пользователям. Управлять доступом можете только вы. Приглашение действует 24 часа и используется один раз. Передавайте его лично нужному человеку.",ct,Inline(rows.ToArray()));
    }
    private async Task<bool> OperatorCallback(long chat,string data,CancellationToken ct)
    {
        if(data is not ("operators" or "inviteop" or "cancelinvites")&&!data.StartsWith("removeop:")&&!data.StartsWith("confirmremoveop:"))return false;
        if(chat!=configuration.OwnerChatId){await Send(chat,"Управление операторами доступно только владельцу.",ct);return true;}
        if(data=="operators"){await OperatorsMenu(chat,ct);return true;}
        if(data=="inviteop")
        {
            configuration.OperatorInvites.RemoveAll(i=>i.Expires<=DateTimeOffset.UtcNow);
            if(configuration.OperatorInvites.Count>=20){await Send(chat,"Сначала отзовите старые приглашения.",ct);return true;}
            string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            configuration.OperatorInvites.Add(new(){Hash=InviteHash(token),Expires=DateTimeOffset.UtcNow.AddHours(24)});Save();
            await Send(chat,"🔗 Одноразовое приглашение оператора (24 часа).\nПередайте ссылку лично сотруднику. После открытия он должен нажать Start. Любой получивший ссылку сможет активировать доступ.\n\nhttps://t.me/PlaySpaceSupport_bot?start=op_"+token,ct,Inline(new[]{Button("← Операторы","operators")}));return true;
        }
        if(data=="cancelinvites"){configuration.OperatorInvites.Clear();Save();await OperatorsMenu(chat,ct);return true;}
        var parts=data.Split(':',2);
        if(!long.TryParse(parts[1],out long target)||target==configuration.OwnerChatId||!configuration.Operators.Any(o=>o.ChatId==target))return true;
        if(parts[0]=="removeop"){await Send(chat,"Отозвать доступ у «"+OperatorName(target)+"»? Сохранённые им ранее сообщения Telegram не удаляются.",ct,Inline(new[]{Button("Да, отозвать","confirmremoveop:"+target),Button("Отмена","operators")}));return true;}
        configuration.Operators.RemoveAll(o=>o.ChatId==target);Save();ClearPrompts(target);
        foreach(var ticket in tickets.Tickets.Where(t=>t.AssignedTo==target))ticket.AssignedTo=0;
        SaveTickets();await OperatorsMenu(chat,ct);return true;
    }
    private async Task RedeemInvite(long chat,string token,string name,CancellationToken ct)
    {
        configuration.OperatorInvites.RemoveAll(i=>i.Expires<=DateTimeOffset.UtcNow);
        var invite=token.Length==48?configuration.OperatorInvites.FirstOrDefault(i=>i.Hash==InviteHash(token)):null;
        if(invite==null){Save();await Send(chat,"Приглашение недействительно, уже использовано или истекло. Попросите владельца создать новое.",ct);return;}
        if(IsOperator(chat)){await Menu(chat,ct);return;}
        configuration.OperatorInvites.Remove(invite);
        configuration.Operators.Add(new(){ChatId=chat,Name=name[..Math.Min(name.Length,80)]});Save();
        await Menu(chat,ct);await Send(configuration.OwnerChatId,"✅ Подключён оператор: "+OperatorName(chat)+". Управление доступом — в разделе «Операторы».",ct);
    }
}
