using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace GameLauncherGUI;

public partial class Form1
{
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
            var next=preferences.Copy();next.CloseBehavior=behavior;next.Save(appDataFolder);preferences=next;
        },preferences.NotificationsEnabled,SetNotifications,FeedUrl,SaveFeed,async()=>{await CheckUpdates(true);return lblStatus.Text;});
        form.ShowDialog(this);
    }
    private async Task SyncLibraryAchievements(Action<string>? status=null)
    {
        await achievementGate.WaitAsync();
        if(IsDisposed || steamKey.Length==0){achievementGate.Release();return;}
        string user=steamUser,key=steamKey;int revision=steamRevision; syncedLibrary=AchievementLibrarySignature();
        var games=myGames.Where(g=>!string.IsNullOrEmpty(g.SteamAppId)&&g.SteamAppId.All(char.IsAsciiDigit)).GroupBy(g=>g.SteamAppId).ToArray();
        int done=0,unavailable=0;
        string hoursMessage="";
        try {
            hoursMessage=await SyncSteamHours(user,key,revision);
            for(int i=0;i<games.Length;i++) {
                if(IsDisposed || revision!=steamRevision)return;
                string message=$"Достижения: {i+1}/{games.Length}";lblStatus.Text=message;status?.Invoke(message);
                try {
                    string url=$"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={key}&steamid={user}&appid={games[i].Key}&l=russian";
                    var stats=JObject.Parse(await (AchievementClient ?? mediaClient).GetStringAsync(url))["playerstats"];
                    if(IsDisposed || revision!=steamRevision)return;
                    if(stats?["success"]?.Value<bool>()!=true) { unavailable++;continue; }
                    var entries=stats["achievements"] as JArray??new JArray();int total=entries.Count,earned=entries.Count(a=>a["achieved"]?.Value<int>()==1);
                    foreach(var game in games[i])game.AchievementSummary=total==0?"Нет достижений":$"🏆 {earned}/{total} · {100.0*earned/total:0}%";
                    done++;
                } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or Newtonsoft.Json.JsonException) { unavailable++; }
            }
            if(!IsDisposed && revision==steamRevision) {
                SaveData();RefreshList();string message=$"{hoursMessage} Достижения: {done}; недоступны: {unavailable}.";lblStatus.Text=message;status?.Invoke(message);
            }
        } finally { achievementGate.Release(); }
    }
}

internal sealed class SettingsForm : StudioDialog
{
    public SettingsForm(string currentUser,string currentKey,Func<string,string,bool,Action<string>,Task> connect,Action disconnect,CloseBehavior closeBehavior=CloseBehavior.Ask,Action<CloseBehavior>? saveCloseBehavior=null,bool notificationsEnabled=true,Action<bool>? saveNotifications=null,string updateFeed="",Action<string>? saveUpdateFeed=null,Func<Task<string>>? checkUpdates=null)
    {
        Text="PLAYSPACE / Настройки";Size=new Size(780,590);MinimumSize=new Size(640,500);
        var tabs=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};tabs.RowStyles.Add(new RowStyle(SizeType.Absolute,48));tabs.RowStyles.Add(new RowStyle(SizeType.Percent,100));var section=new ModernButton("Профиль Steam",Color.FromArgb(49,38,77)){Width=200,Height=38,IsActive=true};tabs.Controls.Add(section,0,0);var page=new Panel {Dock=DockStyle.Fill,BackColor=Color.FromArgb(14,17,27),ForeColor=Color.White};tabs.Controls.Add(page,0,1);ContentHost.Controls.Add(tabs);
        var generalPage=new Panel{Dock=DockStyle.Fill,Visible=false,BackColor=Color.FromArgb(14,17,27)};
        var header=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};tabs.Controls.Remove(section);header.Controls.Add(section);
        var generalButton=new ModernButton("Приложение",Color.FromArgb(31,35,51)){Width=180,Height=38};header.Controls.Add(generalButton);tabs.Controls.Add(header,0,0);
        void ShowSection(bool general){tabs.Controls.Remove(page);tabs.Controls.Remove(generalPage);var active=general?generalPage:page;tabs.Controls.Add(active,0,1);active.Show();section.IsActive=!general;generalButton.IsActive=general;}
        section.Click+=(s,e)=>ShowSection(false);generalButton.Click+=(s,e)=>ShowSection(true);
        var generalLayout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(28),AutoScroll=true};generalPage.Controls.Add(generalLayout);
        generalLayout.Controls.Add(new Label{Text="Закрытие приложения",Font=new Font("Segoe UI",18,FontStyle.Bold),Width=620,Height=54});
        generalLayout.Controls.Add(new Label{Text="Что делать при нажатии на крестик? Выбор можно изменить в любое время.",Width=580,Height=55});
        var closeChoice=new ModernSelect{Width=460,Height=42};closeChoice.Items.AddRange(new object[]{"Каждый раз спрашивать","Всегда сворачивать в трей","Всегда выходить полностью"});closeChoice.SelectedIndex=(int)closeBehavior;generalLayout.Controls.Add(closeChoice);
        var closeStatus=new Label{Width=580,Height=48};generalLayout.Controls.Add(closeStatus);
        closeChoice.SelectedIndexChanged+=(s,e)=>{try{saveCloseBehavior?.Invoke((CloseBehavior)closeChoice.SelectedIndex);closeStatus.Text="Настройка сохранена.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){closeStatus.Text="Не удалось сохранить настройку.";}};
        var notifications=new CheckBox{Text="Уведомлять о новых версиях PLAYSPACE",Checked=notificationsEnabled,Width=580,Height=36};generalLayout.Controls.Add(notifications);
        notifications.CheckedChanged+=(s,e)=>saveNotifications?.Invoke(notifications.Checked);
        generalLayout.Controls.Add(new Label{Text="Обновления · версия "+UpdateService.DisplayVersion,Width=580,Height=32,Font=new Font("Segoe UI",12,FontStyle.Bold)});
        var feed=new TextBox{Text=updateFeed,PlaceholderText="HTTPS-ссылка на latest.json",Width=560,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White};generalLayout.Controls.Add(feed);
        var updateActions=new FlowLayoutPanel{Width=580,Height=48,WrapContents=false};
        var saveFeed=new ModernButton("Сохранить канал",Color.FromArgb(35,40,63)){Width=225,Height=38};var check=new ModernButton("Проверить обновления",Color.FromArgb(115,94,244)){Width=290,Height=38};updateActions.Controls.Add(saveFeed);updateActions.Controls.Add(check);generalLayout.Controls.Add(updateActions);
        var updateStatus=new Label{Text="Проверка при запуске и каждый час, включая работу в трее. Отключение уведомлений убирает автоматические окна и уведомления Windows.",Width=560,Height=80};generalLayout.Controls.Add(updateStatus);
        saveFeed.Click+=(s,e)=>{try{saveUpdateFeed?.Invoke(feed.Text.Trim());updateStatus.Text="Канал обновлений сохранён.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException){updateStatus.Text="Не удалось сохранить канал. Нужна HTTPS-ссылка на JSON.";}};
        check.Click+=async(s,e)=>{check.Enabled=false;try{if(checkUpdates!=null){string message=await checkUpdates();if(!IsDisposed)updateStatus.Text=message;}}finally{if(!IsDisposed)check.Enabled=true;}};
        Disposed+=(s,e)=>{if(!page.IsDisposed)page.Dispose();if(!generalPage.IsDisposed)generalPage.Dispose();};
        var layout=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(28),AutoScroll=true};page.Controls.Add(layout);
        layout.Controls.Add(new Label {Text="Статистика Steam",Font=new Font("Segoe UI",18,FontStyle.Bold),Width=640,Height=48});
        layout.Controls.Add(new Label {Text="Подключи свой Steam-профиль один раз. Общее время Steam и достижения появятся у всех доступных игр. Открой доступ к игровой статистике и наигранному времени.",Width=600,Height=58});
        layout.Controls.Add(new Label {Text="SteamID64",AutoSize=true});
        var user=new TextBox {BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White,Text=currentUser,PlaceholderText="SteamID64 — 17 цифр",Width=560};var key=new TextBox {BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White,Text=currentKey,PlaceholderText="Steam Web API key",UseSystemPasswordChar=true,Width=560};layout.Controls.Add(user);layout.Controls.Add(new Label {Text="Web API key",AutoSize=true});layout.Controls.Add(key);
        var remember=new CheckBox {Text="Запомнить в защищённом хранилище Windows",Checked=true,Width=580,Height=36};layout.Controls.Add(remember);
        var link=new LinkLabel {Text="Получить API-ключ на официальном сайте Steam ↗",Width=580,Height=32,LinkColor=Color.FromArgb(166,153,255)};
        link.LinkClicked+=(s,e)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://steamcommunity.com/dev/apikey") {UseShellExecute=true});layout.Controls.Add(link);
        var apply=new ModernButton("Сохранить и обновить все игры",Color.FromArgb(115,94,244)) {Width=360,Height=44};var remove=new ModernButton("Отключить профиль",Color.FromArgb(40,45,65)) {Width=240,Height=40};layout.Controls.Add(apply);layout.Controls.Add(remove);
        var status=new Label {Width=600,Height=70,Text="Ключ не сохраняется в библиотеке игр и не передаётся сторонним сервисам."};layout.Controls.Add(status);
        apply.Click+=async(s,e)=> {
            if(user.Text.Trim().Length!=17||!user.Text.Trim().All(char.IsAsciiDigit)||key.Text.Trim().Length!=32||!key.Text.Trim().All(Uri.IsHexDigit)) {status.Text="Проверь SteamID64 и API-ключ.";return;}
            apply.Enabled=remove.Enabled=false;
            try {await connect(user.Text.Trim(),key.Text.Trim(),remember.Checked,text=>{if(!IsDisposed)status.Text=text;});}
            catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception) {if(!IsDisposed)status.Text="Не удалось сохранить настройки профиля.";}
            finally {if(!IsDisposed)apply.Enabled=remove.Enabled=true;}
        };
        remove.Click+=(s,e)=>{try{disconnect();user.Clear();key.Clear();status.Text="Профиль отключён.";}catch(System.ComponentModel.Win32Exception){status.Text="Не удалось удалить сохранённый профиль.";}};
    }
}
