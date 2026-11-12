using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal static class SteamPlaytime
{
    // Missing/private data must never replace a previously fetched total with zero.
    public static int Apply(IEnumerable<GameInfo> games,JObject response,string user,DateTimeOffset time)
    {
        if(response["response"]?["games"] is not JArray entries)return 0;
        var totals=new Dictionary<string,long>();
        foreach(var entry in entries) {
            string? id=entry["appid"]?.ToString();
            if(id==null||!id.All(char.IsAsciiDigit)||entry["playtime_forever"]?.Type!=JTokenType.Integer)continue;
            if(long.TryParse(entry["playtime_forever"]!.ToString(),out long minutes)&&minutes>=0)totals[id]=minutes;
        }
        int count=0;
        foreach(var game in games)if(game.SteamAppId!=null&&totals.TryGetValue(game.SteamAppId,out long minutes)) {
            game.SteamPlaytimeMinutes=minutes;game.SteamPlaytimeUser=user;game.SteamPlaytimeUpdatedUtc=time;count++;
        }
        return count;
    }
    public static void Clear(GameInfo game){game.SteamPlaytimeMinutes=null;game.SteamPlaytimeUser=null;game.SteamPlaytimeUpdatedUtc=null;}
}
public partial class Form1
{
    private DateTimeOffset lastSteamHoursAttempt=DateTimeOffset.MinValue;
    private bool refreshingSteamHours;
    private async Task<string> SyncSteamHours(string user,string key,int revision)
    {
        var token=Tasks.Token;token.ThrowIfCancellationRequested();
        lastSteamHoursAttempt=DateTimeOffset.UtcNow;
        try {
            if(Workspace.For(appDataFolder).Offline)return "Офлайн-режим: используются сохранённые часы Steam.";
            string url=$"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={key}&steamid={user}&include_appinfo=false&include_played_free_games=true";
            var response=JObject.Parse(await (AchievementClient??mediaClient).GetStringAsync(url,token));
            if(IsDisposed||resourcesDisposed||token.IsCancellationRequested||revision!=steamRevision)return "";
            int count=SteamPlaytime.Apply(myGames,response,user,DateTimeOffset.UtcNow);
            SaveData();RefreshList();
            return count>0?$"Время Steam обновлено для {count} игр.":"Steam не предоставил время игр. Проверь открытость библиотеки и времени в профиле.";
        } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException) {
            return "Не удалось загрузить время Steam; последние полученные часы сохранены.";
        }
    }
    private async Task RefreshSteamHours()
    {
        if(refreshingSteamHours||steamKey.Length==0||IsDisposed)return;
        refreshingSteamHours=true;
        bool entered=false;
        try {await achievementGate.WaitAsync(Tasks.Token);entered=true;if(!IsDisposed&&!resourcesDisposed&&steamKey.Length>0){string message=await SyncSteamHours(steamUser,steamKey,steamRevision);if(!IsDisposed&&!resourcesDisposed&&!string.IsNullOrEmpty(message))lblStatus.Text=message;}}
        finally {if(entered)achievementGate.Release();refreshingSteamHours=false;}
    }
}
