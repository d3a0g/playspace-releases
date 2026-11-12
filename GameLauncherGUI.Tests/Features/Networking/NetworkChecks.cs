using System.Net;
using System.Net.Http;
using System.Reflection;
using GameLauncherGUI;

internal static class NetworkChecks
{
    private sealed class DownloadFixture : HttpMessageHandler
    {
        public int Requests;public bool Redirect,TooLarge,Html;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Requests++;var response=new HttpResponseMessage(Redirect?HttpStatusCode.Redirect:HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(Html?new byte[8]:new byte[]{0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1})};
            if(Redirect)response.Headers.Location=new Uri("https://untrusted.example/package.msi");if(TooLarge)response.Content.Headers.ContentLength=WarpInstaller.MaxBytes+1;return Task.FromResult(response);
        }
    }
    private sealed class FakeWarp : IWarpClient
    {
        public WarpState State=WarpState.Missing;public int Connected,Disconnected;
        public Task<WarpStatus> Status(CancellationToken cancellation)=>Task.FromResult(new WarpStatus(State,"Fixture component status. No actual network modified."));
        public Task Connect(bool accepted,CancellationToken cancellation){if(!accepted)throw new Exception("Missing consent");Connected++;State=WarpState.Connected;return Task.CompletedTask;}
        public Task Disconnect(CancellationToken cancellation){Disconnected++;State=WarpState.Disconnected;return Task.CompletedTask;}
    }
    internal static void Run(bool ui)
    {
        int count=0;void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException){count++;return;}throw new Exception("Unsafe network action accepted");}
        WarpStatus Parse(int code,string output)=>WarpClient.ParseStatus(new(code,output));
        Check(Parse(0,"Status update: Connected\r\n").State==WarpState.Connected,"Connected status missing");
        Check(Parse(0,"Status update: Disconnected\nReason: Manual disconnection").State==WarpState.Disconnected,"Disconnected was confused with connected");
        Check(Parse(0,"Status update: Connecting").State==WarpState.Connecting,"Connecting not recognized");
        Check(Parse(1,"Status update: Connected").State==WarpState.Unknown,"Failed status became connected");
        Check(Parse(1,"Error: Missing registration. Try registration new").State==WarpState.RegistrationMissing,"Missing registration not detected");
        Check(Parse(0,"A future unrecognized status").State==WarpState.Unknown,"Unknown output became connected");
        Check(WarpInstaller.TraceConfirmsWarp("ip=not-saved\nwarp=on\n")&&WarpInstaller.TraceConfirmsWarp("warp=plus\r\n"),"Trace confirmation failed");
        Check(!WarpInstaller.TraceConfirmsWarp("warp=off\nstatus=on")&&!WarpInstaller.TraceConfirmsWarp("notwarp=on"),"Trace false positive");
        foreach(string url in new[]{"http://downloads.cloudflareclient.com/x","https://user:secret@downloads.cloudflareclient.com/x","https://downloads.cloudflareclient.com.evil.example/x","https://untrusted.example/x","https://downloads.cloudflareclient.com:8443/x"})Check(!WarpInstaller.AllowedDownload(new Uri(url)),"Untrusted installer URL allowed");
        Check(WarpInstaller.AllowedDownload(new Uri(WarpInstaller.DownloadUrl)),"Official download rejected");
        Reject(()=>WarpInstaller.ValidateHeader(new byte[8]));Check(WarpInstaller.CheckExitCode(0)==0&&WarpInstaller.CheckExitCode(3010)==3010,"Installer success/reboot flags lost");Reject(()=>WarpInstaller.CheckExitCode(1602));Reject(()=>WarpInstaller.CheckExitCode(1618));Reject(()=>WarpInstaller.CheckExitCode(1603));
        var calls=new List<string>();int verified=0;bool missing=true,managed=false;
        var controller=new WarpClient(()=>"fixture-cli",(path,args,ct)=>{string command=string.Join(' ',args);calls.Add(command);return Task.FromResult(command.EndsWith("registration show")?new WarpResult(missing?1:0,missing?"Registration Missing":managed?"Account type: Team":"Account type: Free"):new WarpResult(0,command=="status"?"Status update: Disconnected":"Success"));},(path,ct)=>{verified++;return Task.CompletedTask;});
        Reject(()=>controller.Connect(false,default).GetAwaiter().GetResult());Check(calls.Count==0&&verified==0,"No-consent connection performed work");
        controller.Connect(true,default).GetAwaiter().GetResult();Check(calls.SequenceEqual(new[]{"--accept-tos registration show","--accept-tos registration new","--accept-tos mode warp+doh","--accept-tos connect"}),"First connection commands incorrect");Check(verified==calls.Count,"Unsigned CLI command could run");
        calls.Clear();missing=false;controller.Connect(true,default).GetAwaiter().GetResult();Check(!calls.Any(c=>c.EndsWith("registration new")),"Existing registration overwritten");
        calls.Clear();managed=true;Reject(()=>controller.Connect(true,default).GetAwaiter().GetResult());Check(calls.Count==1,"Managed organization profile changed");
        calls.Clear();controller.Disconnect(default).GetAwaiter().GetResult();Check(calls.SequenceEqual(new[]{"disconnect"}),"Disconnect needs consent or rewrites registration");
        var absent=new WarpClient(()=>null,(p,a,c)=>throw new Exception("Shouldn't execute"),(p,c)=>throw new Exception("Shouldn't verify"));Check(absent.Status(default).Result.State==WarpState.Missing,"Missing component not handled");
        var unsigned=new WarpClient(()=>"fixture-cli",(p,a,c)=>throw new Exception("Unsigned process ran"),(p,c)=>throw new IOException("Invalid signature"));Reject(()=>unsigned.Status(default).GetAwaiter().GetResult());
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceNetworkChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        using(var handler=new DownloadFixture())using(var http=new HttpClient(handler)){
            bool signatureChecked=false;string file=WarpInstaller.Download(http,folder,new Progress<string>(),(path,ct)=>{signatureChecked=File.Exists(path);return Task.CompletedTask;},default).GetAwaiter().GetResult();Check(signatureChecked&&File.Exists(file),"Installer download wasn't verified");File.Delete(file);
            Reject(()=>WarpInstaller.Download(http,folder,new Progress<string>(),(p,c)=>throw new IOException("Bad signature"),default).GetAwaiter().GetResult());Check(!Directory.EnumerateFiles(folder).Any(),"Invalid installer left behind");
            handler.Redirect=true;handler.Requests=0;Reject(()=>WarpInstaller.Download(http,folder,new Progress<string>(),(p,c)=>Task.CompletedTask,default).GetAwaiter().GetResult());Check(handler.Requests==1,"Untrusted redirect was requested");handler.Redirect=false;
            handler.TooLarge=true;Reject(()=>WarpInstaller.Download(http,folder,new Progress<string>(),(p,c)=>Task.CompletedTask,default).GetAwaiter().GetResult());handler.TooLarge=false;handler.Html=true;Reject(()=>WarpInstaller.Download(http,folder,new Progress<string>(),(p,c)=>Task.CompletedTask,default).GetAwaiter().GetResult());
        }
        if(ui){
            var fake=new FakeWarp();int installs=0,probes=0;using var form=new NetworkForm(folder,fake,(progress,ct)=>{installs++;fake.State=WarpState.Disconnected;return Task.FromResult(false);},ct=>{probes++;return Task.FromResult(true);});form.Show();Application.DoEvents();
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;T Field<T>(string name)=>(T)typeof(NetworkForm).GetField(name,flags)!.GetValue(form)!;
            var consent=Field<ModernToggle>("consent");var install=Field<ModernButton>("install");var connect=Field<ModernButton>("connect");var disconnect=Field<ModernButton>("disconnect");
            Check(!install.Enabled&&!connect.Enabled&&installs==0&&fake.Connected==0,"Opening network form changed system or bypassed consent");consent.Checked=true;Check(install.Enabled&&!connect.Enabled,"Missing component UI incorrect");install.PerformClick();Application.DoEvents();Check(installs==1&&connect.Enabled,"Install didn't enable connection");connect.PerformClick();Application.DoEvents();Check(fake.Connected==1&&!connect.Enabled&&disconnect.Enabled,"Connection buttons incorrect");
            Field<ModernButton>("check").PerformClick();Application.DoEvents();Check(probes==1&&Field<Label>("activity").Text.Contains("проходит через WARP"),"Tunnel verification not displayed");
            form.Size=form.MinimumSize;Application.DoEvents();Check(Field<Label>("state").Width>500&&connect.Width>180,"Network UI collapsed at minimum size");form.Size=new Size(900,760);Application.DoEvents();Directory.CreateDirectory("artifacts");using(var screenshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(screenshot,form.ClientRectangle);screenshot.Save("artifacts/network-preview.png");}
            disconnect.PerformClick();Application.DoEvents();Check(fake.Disconnected==1&&connect.Enabled,"Disconnect failed");Check(File.ReadAllText(Path.Combine(folder,"network-consent.json")).Contains("cloudflareTermsAccepted"),"Consent not persisted");
            typeof(NetworkForm).GetMethod("ProcessCmdKey",flags)!.Invoke(form,new object[]{new Message(),Keys.Escape});Check(!form.Visible,"Escape didn't close network form");
            using var main=new Form1(folder);main.Show();main.Size=main.MinimumSize;Application.DoEvents();
            IEnumerable<Control> Children(Control parent)=>parent.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Children(child)));
            var navigation=Children(main).OfType<ModernButton>().ToArray();var network=navigation.Single(button=>button.AccessibleName=="Сеть и подключение WARP");var settings=navigation.Single(button=>button.Text.Contains("Настройки"));var support=navigation.Single(button=>button.Text.Contains("Написать боту"));
            Check(network.Visible&&network.Height>=36&&settings.Height>=36&&support.Height>=36,"Sidebar actions clipped at minimum size");Check(network.Bottom<=settings.Top&&settings.Bottom<=support.Top,"Network/settings/support sidebar actions overlap");
        }
        Console.WriteLine($"PASS: {count} WARP checks: status/trace, consent, registration, signed commands, managed profiles, installer URLs/redirects/size/signature/cleanup, exit codes"+(ui?", control panel and Escape":"")+". Fixtures only; no actual installation or network changes.");
    }
}
