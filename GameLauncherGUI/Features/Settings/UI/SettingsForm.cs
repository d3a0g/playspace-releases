using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace GameLauncherGUI;

public partial class Form1
{
    private readonly SectionNavigationState settingsNavigation=new();
    private readonly SemaphoreSlim achievementGate = new(1,1);
    private int steamRevision;
    private string syncedLibrary = "";
    private string AchievementLibrarySignature() => string.Join(",",myGames.Select(g=>g.SteamAppId).Where(id=>!string.IsNullOrEmpty(id)&&id.All(char.IsAsciiDigit)).Distinct().OrderBy(id=>id));
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal HttpClient? AchievementClient { get; set; }
    private string CredentialTarget => "PLAYSPACE/Steam/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(appDataFolder)))[..16];
    private void LoadSteamSettings()
    {
        var saved=SteamCredential.Load(CredentialTarget);
        if(saved.HasValue) { steamUser=saved.Value.User;steamKey=saved.Value.Key; }
    }
    private void OpenSettings()
    {
        using var form=new SettingsForm(steamUser,steamKey, async (user,key,remember,status)=> {
            steamRevision++;steamUser=user;steamKey=key;
            foreach(var game in myGames){game.AchievementSummary=null;if(game.SteamPlaytimeUser!=steamUser)SteamPlaytime.Clear(game);}
            if(remember)SteamCredential.Save(CredentialTarget,user,key);else SteamCredential.Remove(CredentialTarget);
            SaveData();RefreshList();await SyncLibraryAchievements(status);
        },()=> {
            steamRevision++;steamUser="";steamKey="";SteamCredential.Remove(CredentialTarget);
            foreach(var game in myGames){game.AchievementSummary=null;if(game.SteamPlaytimeUser!=steamUser)SteamPlaytime.Clear(game);}SaveData();RefreshList();
        },preferences.CloseBehavior,behavior=>{
            var next=LauncherPreferences.Load(appDataFolder);next.CloseBehavior=behavior;next.Save(appDataFolder);preferences=next;
        },preferences.NotificationsEnabled,SetNotifications,FeedUrl,SaveFeed,async()=>{await CheckUpdates(true);return lblStatus.Text;},preferences.HideWhilePlaying,preferences.RestoreAfterPlaying,SaveGameMode,healthAction:OpenHealth,appearanceChanged:ApplyWorkspaceSettings,navigationState:settingsNavigation);
        form.ShowDialog(this);
    }
    private async Task SyncLibraryAchievements(Action<string>? status=null)
    {
        if(Workspace.For(appDataFolder).Offline){status?.Invoke("Офлайн-режим: сохранённая статистика Steam остаётся доступной.");return;}
        if(IsDisposed||resourcesDisposed)return;
        var token=Tasks.Token;
        await achievementGate.WaitAsync(token);
        if(IsDisposed || steamKey.Length==0){achievementGate.Release();return;}
        string user=steamUser,key=steamKey;int revision=steamRevision; string signature=AchievementLibrarySignature();
        var games=myGames.Where(g=>!string.IsNullOrEmpty(g.SteamAppId)&&g.SteamAppId.All(char.IsAsciiDigit)).GroupBy(g=>g.SteamAppId).ToArray();
        int done=0,unavailable=0;
        string hoursMessage="";
        try {
            hoursMessage=await SyncSteamHours(user,key,revision);
            for(int i=0;i<games.Length;i++) {
                if(IsDisposed || resourcesDisposed || token.IsCancellationRequested || revision!=steamRevision)return;
                string message=$"Достижения: {i+1}/{games.Length}";lblStatus.Text=message;status?.Invoke(message);
                try {
                    string url=$"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={key}&steamid={user}&appid={games[i].Key}&l=russian";
                    var stats=JObject.Parse(await (AchievementClient ?? mediaClient).GetStringAsync(url,token))["playerstats"];
                    if(IsDisposed || resourcesDisposed || token.IsCancellationRequested || revision!=steamRevision)return;
                    if(stats?["success"]?.Value<bool>()!=true) { unavailable++;continue; }
                    var entries=stats["achievements"] as JArray??new JArray();int total=entries.Count,earned=entries.Count(a=>a["achieved"]?.Value<int>()==1);
                    foreach(var game in games[i])game.AchievementSummary=total==0?"Нет достижений":$"🏆 {earned}/{total} · {100.0*earned/total:0}%";
                    done++;
                } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or Newtonsoft.Json.JsonException) { unavailable++; }
            }
            if(!IsDisposed && !resourcesDisposed && !token.IsCancellationRequested && revision==steamRevision) {
                SaveData();syncedLibrary=signature;RefreshList();string message=$"{hoursMessage} Достижения: {done}; недоступны: {unavailable}.";lblStatus.Text=message;status?.Invoke(message);
            }
        } finally { achievementGate.Release(); }
    }
}

