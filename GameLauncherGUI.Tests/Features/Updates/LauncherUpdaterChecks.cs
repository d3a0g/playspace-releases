using System.Net;
using System.Security.Cryptography;
using GameLauncherGUI;

internal static class LauncherUpdaterChecks
{
    private sealed class Package(byte[] bytes,bool insecure=false):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(insecure)request.RequestUri=new Uri("http://example.com/setup.exe");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes)});
        }
    }
    internal static void Run()
    {
        void Check(bool okay,string message){if(!okay)throw new Exception(message);}
        void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or OperationCanceledException){return;}throw new Exception("Unsafe update accepted");}
        byte[] bytes="MZ fixture package"u8.ToArray();string hash=Convert.ToHexString(SHA256.HashData(bytes));
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceUpdaterTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var release=new UpdateRelease("9.0.0","https://example.com/setup.exe",InstallerSha256:hash);
            var parsed=UpdateService.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(release));Check(parsed.InstallerSha256==hash,"Feed hash lost");
            using var client=new HttpClient(new Package(bytes));
            string output=Path.Combine(folder,"setup.exe");
            var progress=new Progress<(long Done,long? Total)>();
            void Download(UpdateRelease value,CancellationToken token=default)=>LauncherUpdater.Download(client,value,output,progress,token).GetAwaiter().GetResult();
            Download(release);Check(File.ReadAllBytes(output).SequenceEqual(bytes),"Package changed");File.Delete(output);
            Reject(()=>Download(release with{InstallerSha256=new string('0',64)}));Check(!File.Exists(output),"Corrupt package retained");
            Reject(()=>Download(release with{InstallerSha256=null}));
            Reject(()=>Download(release with{DownloadUrl="https://example.com/file.zip"}));
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();Reject(()=>Download(release,cancelled.Token));Check(!File.Exists(output),"Cancelled package retained");
            using var insecure=new HttpClient(new Package(bytes,true));Reject(()=>LauncherUpdater.Download(insecure,release,output,progress,default).GetAwaiter().GetResult());
            byte[] html="<html>wrong content</html>"u8.ToArray();File.WriteAllBytes(output,html);Reject(()=>LauncherUpdater.Verify(output,Convert.ToHexString(SHA256.HashData(html)),default).GetAwaiter().GetResult());
            var job=new UpdateJob(output,hash,Path.Combine(folder,"folder with spaces","LauncherPlaySpace.exe"),123,1,"9.0.0");
            var start=LauncherUpdater.InstallerStart(job,folder);
            Check(!start.UseShellExecute&&start.CreateNoWindow,"Installer isn't silent");
            foreach(string flag in new[]{"/VERYSILENT","/SUPPRESSMSGBOXES","/SP-","/NORESTART","/NOCLOSEAPPLICATIONS","/NORESTARTAPPLICATIONS"})Check(start.ArgumentList.Contains(flag),"Missing "+flag);
            Check(start.ArgumentList.Contains("/DIR="+Path.GetDirectoryName(job.Target)),"Install path with spaces split");
            using var form=new LauncherUpdateForm(release,true);Check(form.MinimumSize.Width>=560,"Update surface too small");
            form.Show();Application.DoEvents();
            IEnumerable<Control> All(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(All(child)));
            var labels=All(form).OfType<SmoothLabel>().ToArray();
            labels.Single(l=>l.Text=="Подготовка обновления").Text="Загрузка обновления";
            labels.Single(l=>l.Text=="").Text="Версия 1.0.6 · 24,8 / 48,2 Мбайт · 51%\n4,2 Мбайт/с";
            All(form).OfType<UpdateActivity>().Single().Progress=.51;
            Directory.CreateDirectory("artifacts/in-app-update-previews");
            foreach(var size in new[]{new Size(640,440),form.MinimumSize})
            {
                form.Size=size;Application.DoEvents();
                foreach(var label in labels)Check(label.Height>=label.Font.Height,"Update text clipped");
                using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,form.ClientRectangle);bitmap.Save($"artifacts/in-app-update-previews/update-{size.Width}.png");
            }
            form.Close();
        }
        finally{Directory.Delete(folder,true);}
        Console.WriteLine("PASS: updater download/hash, missing checksum, invalid payload, cancellation cleanup, HTTPS redirects and silent installer arguments. No installation or external network.");
    }
}
