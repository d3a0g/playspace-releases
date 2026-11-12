using System.Net;
using System.Net.Http;
using System.Reflection;
using GameLauncherGUI;

internal static class UpdateChecks
{
    private sealed class Feed : HttpMessageHandler
    {
        public string Json="{\"version\":\"1.10.0\",\"downloadUrl\":\"https://example.com/release.zip\",\"notes\":\"New version\"}";
        public HttpStatusCode Status=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Json),RequestMessage=request});
    }
    public static void Run(bool ui)
    {
        void Check(bool okay,string message){if(!okay)throw new Exception(message);}
        void Reject(Action work){try{work();}catch(Exception ex)when(ex is IOException or InvalidDataException or Newtonsoft.Json.JsonException or HttpRequestException or OperationCanceledException){return;}throw new Exception("Invalid update accepted");}
        Check(UpdateService.ParseVersion("v1.10.0")>UpdateService.ParseVersion("1.9.9"),"Numeric version comparison failed");
        var installer=UpdateService.Parse("{\"version\":\"1.10.0\",\"downloadUrl\":\"https://example.com/portable.zip\",\"installerUrl\":\"https://example.com/setup.exe\"}");
        Check(installer.HasInstaller&&installer.DownloadUri.AbsoluteUri=="https://example.com/setup.exe","Installer was not preferred to ZIP");
        var direct=UpdateService.Parse("{\"version\":\"1.10.0\",\"downloadUrl\":\"https://example.com/setup.exe\"}");Check(direct.HasInstaller,"Installer URL not recognized by legacy-compatible feed");
        var legacy=UpdateService.Parse("{\"version\":\"1.10.0\",\"downloadUrl\":\"https://example.com/portable.zip\"}");Check(!legacy.HasInstaller&&legacy.DownloadUri.AbsolutePath.EndsWith(".zip"),"Legacy release fallback broken");
        Reject(()=>UpdateService.Parse("{\"version\":\"1.10.0\",\"downloadUrl\":\"https://example.com/setup.exe\",\"installerUrl\":\"http://example.com/setup.exe\"}"));
        Reject(()=>UpdateService.Parse("{\"version\":\"1.10.0\",\"downloadUrl\":\"https://example.com/setup.exe\",\"installerUrl\":\"https://user:secret@example.com/setup.exe\"}"));
        Check(UpdateAvailableForm.FormatNotes("## Библиотека\r\n- **Поиск** теперь очищается.\r\n\r\n### Steam\n* Часы сохранены.").Replace("\r\n","\n")=="БИБЛИОТЕКА\n• Поиск теперь очищается.\n\nSTEAM\n• Часы сохранены.","Release note sections/bullets/newlines weren't formatted");
        Check(UpdateAvailableForm.FormatNotes(" ").Contains("не добавил список"),"Missing release notes aren't explained");
        Check(UpdateAvailableForm.FormatNotes("DLSS 5; версия 1.0.3; 500 игр.")=="DLSS 5; версия 1.0.3; 500 игр.","Plain release notes were changed");
        Check(UpdateService.FeedUri("https://drive.google.com/file/d/ABC_123/view?usp=sharing").Query.Contains("id=ABC_123"),"Drive file link wasn't converted");
        foreach(string url in new[]{"http://example.com/latest.json","file:///C:/update.exe","https://user:secret@example.com/latest.json"})Reject(()=>UpdateService.HttpsUrl(url));
        Reject(()=>UpdateService.Parse("{}"));Reject(()=>UpdateService.Parse("{\"version\":\"bad\",\"downloadUrl\":\"https://example.com\"}"));Reject(()=>UpdateService.Parse("<html>Sign in</html>"));
        using var handler=new Feed();using var client=new HttpClient(handler);
        Check(UpdateService.Check(client,"https://example.com/latest.json",new Version(1,9,9),default).GetAwaiter().GetResult()!=null,"New update wasn't detected");
        Check(UpdateService.Check(client,"https://example.com/latest.json",new Version(1,10,0),default).GetAwaiter().GetResult()==null,"Same version triggered update");
        Check(UpdateService.Check(client,"https://example.com/latest.json",new Version(2,0,0),default).GetAwaiter().GetResult()==null,"Old version triggered update");
        handler.Status=HttpStatusCode.ServiceUnavailable;Reject(()=>UpdateService.Check(client,"https://example.com/latest.json",new Version(1,0),default).GetAwaiter().GetResult());handler.Status=HttpStatusCode.OK;
        handler.Json=new string('a',129*1024);Reject(()=>UpdateService.Check(client,"https://example.com/latest.json",new Version(1,0),default).GetAwaiter().GetResult());
        string folder=Path.Combine(Path.GetTempPath(),"PlayspaceUpdates-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var preferences=new LauncherPreferences{NotificationsEnabled=false,CloseBehavior=CloseBehavior.Tray,UpdateFeedUrl="https://example.com/latest.json",LastNotifiedVersion="1.1.0"};preferences.Save(folder);
        var copied=LauncherPreferences.Load(folder).Copy();copied.CloseBehavior=CloseBehavior.Exit;copied.Save(folder);var loaded=LauncherPreferences.Load(folder);
        Check(!loaded.NotificationsEnabled&&loaded.UpdateFeedUrl==preferences.UpdateFeedUrl&&loaded.LastNotifiedVersion=="1.1.0","Close settings reset update preferences");
        if(ui){
            using(var installerWindow=new UpdateAvailableForm(installer)){IEnumerable<Control> All(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(All(child)));Check(All(installerWindow).OfType<ModernButton>().Any(b=>b.Text.Contains("Обновить и перезапустить")),"Update UI doesn't offer in-app update");}
            // Empty source prevents any real network check. No balloon notifications are sent.
            loaded.UpdateFeedUrl="";loaded.Save(folder);using var form=new Form1(folder);form.Show();Application.DoEvents();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var tray=(NotifyIcon)typeof(Form1).GetField("tray",flags)!.GetValue(form)!;
            var toggle=tray.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item=>item.Text=="Уведомления PLAYSPACE");Check(!toggle.Checked,"Tray didn't load notification preference");toggle.PerformClick();Check(toggle.Checked&&LauncherPreferences.Load(folder).NotificationsEnabled,"Tray didn't persist notification toggle");
            typeof(Form1).GetMethod("SetNotifications",flags)!.Invoke(form,new object[]{false});Check(!toggle.Checked&&!LauncherPreferences.Load(folder).NotificationsEnabled,"Settings notification toggle didn't update tray");
            string notes=File.ReadAllText("docs/releases/1.0.4.md");
            using var dialog=new UpdateAvailableForm(new("1.0.4","https://example.com/release.zip",notes));dialog.Show();Application.DoEvents();
            IEnumerable<Control> Children(Control parent)=>parent.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Children(child)));
            var details=Children(dialog).OfType<FormattedDescription>().Single();
            Check(details.Text.Contains("ОКНО ОБНОВЛЕНИЯ")&&details.Text.Contains("• "),"Release notes card didn't show formatted changes");
            dialog.Size=dialog.MinimumSize;Application.DoEvents();Check(details.Width>350&&details.Height>120,"Release notes card collapsed at minimum window size");
            dialog.Size=new Size(760,650);Application.DoEvents();
            Directory.CreateDirectory("artifacts");using var preview=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(preview,dialog.ClientRectangle);preview.Save("artifacts/update-available.png");
        }
        Console.WriteLine("PASS: update versions/JSON, HTTPS and Drive links, server errors/size limit, notification persistence and tray/settings toggles, update window. Fixture responses only.");
    }
}
