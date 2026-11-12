using System.Net;
using System.Net.Http;
using System.Reflection;
using GameLauncherGUI;

internal static class DiagnosticChecks
{
    private sealed class Probe : IDiagnosticProbe
    {
        public bool Available=true,FailDns,FailHttps;public int Http=200,Calls;
        public bool NetworkAvailable(){Calls++;return Available;}
        public Task Resolve(string host,CancellationToken ct){Calls++;if(FailDns)throw new IOException("C:\\Users\\Private\\secret-token=fixture-secret 192.0.2.1");return Task.CompletedTask;}
        public Task Tcp(string host,CancellationToken ct){Calls++;return Task.CompletedTask;}
        public Task<int> Https(string host,CancellationToken ct){Calls++;if(FailHttps)throw new HttpRequestException("https://user:secret@example.com/?token=fixture-secret");return Task.FromResult(Http);}
    }
    private sealed class Warp : IWarpClient
    {
        public int Reads,Mutations;public WarpState State=WarpState.Missing;
        public Task<WarpStatus> Status(CancellationToken ct){Reads++;return Task.FromResult(new WarpStatus(State,"private path C:\\Users\\Private and token=fixture-secret"));}
        public Task Connect(bool accepted,CancellationToken ct){Mutations++;throw new Exception("Diagnostics connected WARP");}
        public Task Disconnect(CancellationToken ct){Mutations++;throw new Exception("Diagnostics disconnected WARP");}
    }
    internal static void Run(bool ui)
    {
        int count=0;void Check(bool condition,string message){if(!condition)throw new Exception(message);count++;}
        void Pump(Func<bool> done){var deadline=DateTime.UtcNow.AddSeconds(3);while(!done()&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(5);}Check(done(),"Diagnostic UI didn't finish");}
        var probe=new Probe();var warp=new Warp();int traces=0;var runner=new NetworkDiagnostics(probe,warp,ct=>{traces++;return Task.FromResult(false);});
        NetworkDiagnosticReport Run()=>runner.Run(DiagnosticTarget.All[0],null,default).GetAwaiter().GetResult();
        var report=Run();Check(report.Checks.Count==7,"Diagnostic checks missing");Check(report.Checks.All(c=>c.Outcome is CheckOutcome.Ok or CheckOutcome.Info),"Healthy connection incorrectly failed");Check(warp.Reads==1&&warp.Mutations==0&&traces==1,"Diagnostics changed WARP state");Check(report.ToSafeText().Contains("UDP")&&report.ToSafeText().Contains("не является пингом"),"Report overstates game reachability or latency");
        probe.Available=false;Check(Run().Checks.First().Outcome==CheckOutcome.Failed,"Unavailable adapter not reported");probe.Available=true;
        foreach(int status in new[]{403,451,429,503,401}){probe.Http=status;Check(Run().Checks.Single(c=>c.Name=="HTTPS выбранного сервиса").Outcome==CheckOutcome.Warning,"Server rejection mistaken for network failure");}
        foreach(int status in new[]{200,301,404}){probe.Http=status;Check(Run().Checks.Single(c=>c.Name=="HTTPS выбранного сервиса").Outcome==CheckOutcome.Ok,"Reachable HTTPS endpoint rejected");}
        probe.FailDns=probe.FailHttps=true;report=Run();Check(report.Checks.Single(c=>c.Name=="DNS выбранного сервиса").Outcome==CheckOutcome.Failed,"DNS failure hidden");string text=report.ToSafeText();Check(!text.Contains("fixture-secret")&&!text.Contains("192.0.2.1")&&!text.Contains("C:\\Users")&&!text.Contains("user:secret"),"Private exception/status content leaked to report");Check(text.Contains("Что сделать:"),"Report lacks next actions");probe.FailDns=probe.FailHttps=false;
        int before=probe.Calls;bool rejected=false;try{runner.Run(new("custom","Private","127.0.0.1"),null,default).GetAwaiter().GetResult();}catch(ArgumentException){rejected=true;}Check(rejected&&probe.Calls==before,"Unlisted/private target accepted");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool stopped=false;try{runner.Run(DiagnosticTarget.All[0],null,cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}Check(stopped&&probe.Calls==before,"Cancelled diagnostics performed network work");}
        warp.State=WarpState.Connected;runner=new NetworkDiagnostics(probe,warp,ct=>Task.FromResult(true));Check(Run().Checks.Last().Outcome==CheckOutcome.Ok,"Confirmed WARP trace not reported");
        if(ui){
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;int runs=0;
            using var form=new NetworkDiagnosticsForm(async(t,p,c)=>{runs++;return await runner.Run(t,p,c);});form.Show();Application.DoEvents();
            T Field<T>(string name)=>(T)typeof(NetworkDiagnosticsForm).GetField(name,flags)!.GetValue(form)!;
            var run=Field<ModernButton>("run");var export=Field<ModernButton>("report");Check(runs==0&&!export.Enabled,"Opening diagnostics ran checks or enabled stale export");run.PerformClick();Pump(()=>run.Enabled);Check(runs==1&&export.Enabled,"Complete report wasn't available");
            form.Size=form.MinimumSize;Application.DoEvents();Check(Field<FormattedDescription>("results").Height>150&&Field<FormattedDescription>("results").Width>650,"Diagnostic layout collapsed");form.Size=new Size(980,800);Application.DoEvents();Directory.CreateDirectory("artifacts");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save("artifacts/diagnostics-preview.png");}
            Field<ModernSelect>("target").SelectedIndex=1;Check(!export.Enabled,"Changing service left previous report available");
            using var preview=new DiagnosticReportForm(report.ToSafeText());preview.Show();Application.DoEvents();Check(preview.Controls.Count>0,"Report preview didn't open");
            using var cancelling=new NetworkDiagnosticsForm(async(t,p,c)=>{await Task.Delay(Timeout.Infinite,c);throw new Exception("Should cancel");});cancelling.Show();Application.DoEvents();var cancelRun=(ModernButton)typeof(NetworkDiagnosticsForm).GetField("run",flags)!.GetValue(cancelling)!;var cancel=(ModernButton)typeof(NetworkDiagnosticsForm).GetField("cancel",flags)!.GetValue(cancelling)!;cancelRun.PerformClick();Check(cancel.Enabled&&!cancelRun.Enabled,"Running diagnostic isn't cancellable");cancel.PerformClick();Pump(()=>cancelRun.Enabled);Check(!((ModernButton)typeof(NetworkDiagnosticsForm).GetField("report",flags)!.GetValue(cancelling)!).Enabled,"Cancelled report can be exported");
            typeof(NetworkDiagnosticsForm).GetMethod("ProcessCmdKey",flags)!.Invoke(form,new object[]{new Message(),Keys.Escape});Check(!form.Visible,"Escape didn't close diagnostics");
        }
        Console.WriteLine($"PASS: {count} diagnostics checks: network/server distinction, WARP read-only, target allowlist, cancellation, report privacy"+(ui?", UI/report preview and export readiness":"")+". Fixtures only; no real network calls or messages.");
    }
}
