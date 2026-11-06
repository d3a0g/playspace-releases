using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record UpdateRelease(string Version,string DownloadUrl,string Notes="")
{
    public Version ParsedVersion => UpdateService.ParseVersion(Version);
}
internal static class UpdateService
{
    public const string DefaultFeed="https://github.com/d3a0g/playspace-releases/releases/latest/download/latest.json";
    public static Version CurrentVersion => typeof(Form1).Assembly.GetName().Version??new Version(1,0,0,0);
    public static string DisplayVersion => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";
    public static Version ParseVersion(string text)
    {
        if(!Version.TryParse(text.Trim().TrimStart('v','V'),out var version))throw new InvalidDataException("Некорректная версия обновления.");
        return new Version(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));
    }
    public static Uri HttpsUrl(string text)
    {
        if(!Uri.TryCreate(text,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||!string.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("Нужна ссылка HTTPS.");
        return uri;
    }
    public static Uri FeedUri(string text)
    {
        var uri=HttpsUrl(text.Trim());
        if(uri.Host.Equals("drive.google.com",StringComparison.OrdinalIgnoreCase)) {
            var match=System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath,@"^/file/d/([A-Za-z0-9_-]+)/");
            if(match.Success)return new Uri("https://drive.google.com/uc?export=download&id="+match.Groups[1].Value);
        }
        return uri;
    }
    public static UpdateRelease Parse(string json)
    {
        var release=JsonConvert.DeserializeObject<UpdateRelease>(json)??throw new InvalidDataException("Пустой файл обновления.");
        if(string.IsNullOrWhiteSpace(release.Version)||string.IsNullOrWhiteSpace(release.DownloadUrl))throw new InvalidDataException("В файле обновления отсутствуют версия или ссылка.");
        _=release.ParsedVersion;_=HttpsUrl(release.DownloadUrl);
        if(release.Notes?.Length>20000)throw new InvalidDataException("Описание обновления слишком длинное.");return release;
    }
    public static async Task<UpdateRelease?> Check(HttpClient client,string feed,Version current,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,FeedUri(feed));request.Headers.CacheControl=new(){NoCache=true};
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();
        if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Канал обновления перенаправлен на небезопасный адрес.");
        using var stream=await response.Content.ReadAsStreamAsync(cancellation);using var buffer=new MemoryStream();byte[] block=new byte[8192];int read;
        while((read=await stream.ReadAsync(block,cancellation))>0){if(buffer.Length+read>128*1024)throw new InvalidDataException("Файл обновления слишком большой.");buffer.Write(block,0,read);}
        var release=Parse(Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF'));
        return release.ParsedVersion>ParseVersion(current.ToString())?release:null;
    }
    public static string BundledFeed(string? sourceFile=null)
    {
        try {
            string file=sourceFile??Path.Combine(AppContext.BaseDirectory,"update-source.json");if(!File.Exists(file))return DefaultFeed;
            string? configured=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file)).Value<string>("feedUrl");return string.IsNullOrWhiteSpace(configured)?DefaultFeed:configured;
        }catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return DefaultFeed;}
    }
}
internal sealed class UpdateAvailableForm : StudioDialog
{
    public UpdateAvailableForm(UpdateRelease release)
    {
        Text="PLAYSPACE / Доступно обновление";Size=new Size(650,480);MinimumSize=new Size(580,400);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new Label{Text="Новая версия PLAYSPACE",Font=new Font("Segoe UI",19,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(new Label{Text=$"Версия {UpdateService.DisplayVersion} → {release.Version}",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(171,160,223)},0,1);
        layout.Controls.Add(new TextBox{Text=string.IsNullOrWhiteSpace(release.Notes)?"Доступна новая версия приложения.":release.Notes,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White,BorderStyle=BorderStyle.None},0,2);
        var download=new ModernButton("Открыть загрузку обновления ↗",Color.FromArgb(115,94,244)){Dock=DockStyle.Fill};layout.Controls.Add(download,0,3);
        download.Click+=(s,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateService.HttpsUrl(release.DownloadUrl).AbsoluteUri){UseShellExecute=true});}catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidDataException){download.Text="Не удалось открыть ссылку";}};
    }
}
public partial class Form1
{
    private readonly System.Windows.Forms.Timer updateTimer=new(){Interval=60*60*1000};
    private readonly CancellationTokenSource updateCancellation=new();
    private readonly HttpClient updateClient=new(){Timeout=TimeSpan.FromSeconds(15)};
    private bool checkingUpdate;
    private readonly bool updateChecksEnabled;
    private bool updatesDisposed;
    private UpdateRelease? availableUpdate;
    private ToolStripMenuItem? notificationsMenu;
    private string FeedUrl=>string.IsNullOrWhiteSpace(preferences.UpdateFeedUrl)?UpdateService.BundledFeed():preferences.UpdateFeedUrl;
    private void InitializeUpdates()
    {
        notificationsMenu=new ToolStripMenuItem("Уведомления об обновлениях"){Checked=preferences.NotificationsEnabled,CheckOnClick=false};
        notificationsMenu.Click+=(s,e)=>SetNotifications(!preferences.NotificationsEnabled);tray!.ContextMenuStrip!.Items.Insert(1,notificationsMenu);
        tray.ContextMenuStrip.Items.Insert(2,new ToolStripMenuItem("Проверить обновления",null,async(s,e)=>await CheckUpdates(true)));
        tray.BalloonTipClicked+=(s,e)=>{if(availableUpdate!=null){RestoreWindow();ShowUpdate(availableUpdate);}};
        updateTimer.Tick+=async(s,e)=>await CheckUpdates(false);
        if(updateChecksEnabled){updateTimer.Start();Shown+=async(s,e)=>await CheckUpdates(false);}
    }
    private void SetNotifications(bool enabled)
    {
        var next=preferences.Copy();next.NotificationsEnabled=enabled;
        try{next.Save(appDataFolder);preferences=next;if(notificationsMenu!=null)notificationsMenu.Checked=enabled;}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){lblStatus.Text="Не удалось сохранить настройку уведомлений.";}
    }
    private void SaveFeed(string feed)
    {
        feed=feed.Trim();if(feed.Length>0)_=UpdateService.FeedUri(feed);
        var next=preferences.Copy();if(next.UpdateFeedUrl!=feed)next.LastNotifiedVersion="";next.UpdateFeedUrl=feed;next.Save(appDataFolder);preferences=next;
    }
    private async Task CheckUpdates(bool manual)
    {
        if(checkingUpdate||IsDisposed)return;
        string feed=FeedUrl;
        if(string.IsNullOrWhiteSpace(feed)){if(manual)lblStatus.Text="Канал обновлений ещё не настроен: укажи адрес latest.json в настройках приложения.";return;}
        checkingUpdate=true;
        try {
            var release=await UpdateService.Check(updateClient,feed,UpdateService.CurrentVersion,updateCancellation.Token);
            if(IsDisposed||updateCancellation.IsCancellationRequested||FeedUrl!=feed)return;
            availableUpdate=release;
            if(release==null){if(manual)lblStatus.Text="Установлена актуальная версия PLAYSPACE.";return;}
            lblStatus.Text=$"Доступна новая версия PLAYSPACE: {release.Version}";
            bool notify=preferences.NotificationsEnabled&&preferences.LastNotifiedVersion!=release.ParsedVersion.ToString();
            if(notify) {
                var next=preferences.Copy();next.LastNotifiedVersion=release.ParsedVersion.ToString();next.Save(appDataFolder);preferences=next;
                tray?.ShowBalloonTip(10000,"Доступно обновление PLAYSPACE",$"Версия {release.Version}. Нажми, чтобы открыть загрузку.",ToolTipIcon.Info);
            }
            if(manual || notify&&Visible&&WindowState!=FormWindowState.Minimized&&OwnedForms.Length==0)ShowUpdate(release);
        } catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidDataException or UnauthorizedAccessException) {
            if(manual&&!IsDisposed)lblStatus.Text="Не удалось проверить обновления. Проверь адрес канала и подключение к интернету.";
        } finally {checkingUpdate=false;}
    }
    private void ShowUpdate(UpdateRelease release)
    {
        using var dialog=new UpdateAvailableForm(release);dialog.ShowDialog(this);
    }
    private void DisposeUpdates(){if(updatesDisposed)return;updatesDisposed=true;updateTimer.Stop();updateTimer.Dispose();updateCancellation.Cancel();updateCancellation.Dispose();updateClient.Dispose();}
}
