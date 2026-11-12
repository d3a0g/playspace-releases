using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using GameLauncherGUI;

internal static class UpdateHelpChecks
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Reject(Action action,string message){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException or OperationCanceledException or InvalidOperationException){checks++;return;}throw new Exception(message);}
    private sealed class FakeHttp(byte[] bytes,Action count):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){count();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes)});}
    }
    private static IEnumerable<Control> Children(Control control){foreach(Control child in control.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    private static async Task Network(string folder)
    {
        int calls=0;var bytes=new byte[16384];var center=new TransferCenter(Path.Combine(folder,"transfer-cache"),()=>new HttpClient(new FakeHttp(bytes,()=>Interlocked.Increment(ref calls))),()=>false);
        center.ConfigureBandwidth(100000,DownloadSpeedUnit.Megabits);
        var watch=Stopwatch.StartNew();var downloaded=await center.Fetch("Limited",new Uri("https://example.invalid/a"),100000,default).ConfigureAwait(false);
        Check(downloaded.Length==bytes.Length&&watch.ElapsedMilliseconds>=150,"Download ignored rate limit");Check(center.Items()[0].BytesPerSecond==0,"Completed transfer has live speed");
        var reloaded=new TransferCenter(Path.Combine(folder,"transfer-cache"));Check(reloaded.RateLimit==100000&&reloaded.SpeedUnit==DownloadSpeedUnit.Megabits,"Download policy not persisted");
        center.ConfigureBandwidth(1,DownloadSpeedUnit.Megabytes);int before=calls;await center.Fetch("Cache",new Uri("https://example.invalid/a"),100000,default).ConfigureAwait(false);Check(calls==before,"Cached download used network");
        using(var cancel=new CancellationTokenSource(30)){try{await center.Fetch("Cancelled",new Uri("https://example.invalid/cancel"),100000,cancel.Token).ConfigureAwait(false);throw new Exception("Throttle ignored cancellation");}catch(OperationCanceledException){checks++;}}
        Check(center.Items()[0].State==TransferState.Cancelled,"Throttle cancellation state wrong");
        var pending=center.Fetch("Live change",new Uri("https://example.invalid/live"),100000,default);await Task.Delay(20).ConfigureAwait(false);center.ConfigureBandwidth(0,DownloadSpeedUnit.Megabytes);
        Check(await Task.WhenAny(pending,Task.Delay(2000)).ConfigureAwait(false)==pending,"Disabling rate limit did not affect active job");await pending.ConfigureAwait(false);
        Reject(()=>center.ConfigureBandwidth(-1,DownloadSpeedUnit.Megabytes),"Negative rate accepted");Reject(()=>center.ConfigureBandwidth(0,(DownloadSpeedUnit)99),"Invalid unit accepted");
    }
    internal static void Run(bool ui)
    {
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceUpdateHelp-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        Check(DownloadSpeed.Bytes(8,DownloadSpeedUnit.Megabits)==1000000,"Mbps conversion wrong");Check(DownloadSpeed.Bytes(1,DownloadSpeedUnit.Megabytes)==1000000,"MBps conversion wrong");Check(DownloadSpeed.Value(1000000,DownloadSpeedUnit.Megabits)==8,"Mbps display wrong");Check(DownloadSpeed.Format(1000000,DownloadSpeedUnit.Megabytes).Contains("Мбайт/с"),"Speed unit label missing");
        Task.Run(()=>Network(folder)).GetAwaiter().GetResult();
        var game=new GameInfo{Name="Fixture game",SteamAppId="730",ExePath=Path.Combine(folder,"game.exe"),InstallDirectory=Path.Combine(folder,"Game")};
        string Manifest(string extra)=>"\"AppState\" { \"appid\" \"730\" \"buildid\" \"100\" "+extra+" }";
        var current=GameUpdates.Steam(game,Manifest("\"StateFlags\" \"4\""));Check(current.State==GameUpdateState.CheckInClient&&current.Detail.Contains("не подтверждение"),"Idle manifest falsely guarantees latest version");
        var pending=GameUpdates.Steam(game,Manifest("\"TargetBuildID\" \"101\""));Check(pending.State==GameUpdateState.Pending,"Target build change missing");
        var partial=GameUpdates.Steam(game,Manifest("\"BytesToDownload\" \"10000\" \"BytesDownloaded\" \"5000\""));Check(partial.State==GameUpdateState.Partial,"Partial update missing");
        Check(GameUpdates.Steam(game,Manifest("\"StateFlags\" \"6\"")).State==GameUpdateState.Pending,"Client pending flag missing");Reject(()=>GameUpdates.Steam(game,"\"appid\" \"570\""),"Foreign manifest accepted");
        Check(GameUpdates.Client(new(){Name="VALORANT",ExePath="C:\\Games\\valorant.exe"})=="Riot Client","Riot client not recognized");Check(GameUpdates.Client(new(){ExePath="C:\\Ubisoft Game Launcher\\games\\x.exe"})=="Ubisoft Connect","Ubisoft client not recognized");Check(GameUpdates.PageUri(pending)=="steam://nav/games/details/730","Steam action does not target selected game");Check(GameUpdates.PageUri(pending with{Game=new(){SteamAppId="730/../unsafe"}})==null,"Unsafe Steam ID creates URI");
        string steam=Path.Combine(folder,"Steam");Directory.CreateDirectory(Path.Combine(steam,"steamapps"));File.WriteAllText(Path.Combine(steam,"steamapps","appmanifest_730.acf"),Manifest("\"TargetBuildID\" \"101\""));
        var local=new GameInfo{Name="Local",SteamAppId="",ExePath="C:\\Games\\local.exe"};var epic=new GameInfo{Name="Epic fixture",SteamAppId="",ExePath="C:\\Custom\\Game\\game.exe",InstallDirectory="C:\\Custom\\Game"};
        var entries=GameUpdates.Read(new[]{game,local,epic},new[]{steam},epicRoots:new[]{epic.InstallDirectory});Check(entries.Count==3&&entries[0].State==GameUpdateState.Pending,"Unified update list/sort failed");Check(entries.Single(e=>e.Game==local).State==GameUpdateState.Unknown,"Local game has invented update status");Check(entries.Single(e=>e.Game==epic).Client=="Epic Games","Custom Epic install not recognized");
        Reject(()=>GameUpdates.Read(new[]{game},new[]{steam},new CancellationToken(true),Array.Empty<string>()),"Update scan ignored cancellation");
        var now=DateTimeOffset.UtcNow;var tracker=new LaunchAttemptTracker();tracker.Arm(game,now);Check(tracker.Observe(now,_=>true,new[]{game}).Count==0,"Running game produces warning");Check(tracker.Observe(now.AddSeconds(5),_=>false,new[]{game}).Count==0,"No handoff grace period");Check(tracker.Observe(now.AddSeconds(19),_=>false,new[]{game}).Count==0,"Handoff grace too short");var issues=tracker.Observe(now.AddSeconds(20),_=>false,new[]{game});Check(issues.Single().Kind==LaunchIssueKind.ClosedQuickly,"Quick close not identified");Check(tracker.Observe(now.AddMinutes(1),_=>false,new[]{game}).Count==0,"Repeated quick-close notification");
        tracker.Arm(game,now);tracker.Observe(now,_=>true,new[]{game});tracker.Observe(now.AddSeconds(5),_=>false,new[]{game});Check(tracker.Observe(now.AddSeconds(10),_=>true,new[]{game}).Count==0,"Process handoff falsely called crash");tracker.Observe(now.AddSeconds(46),_=>true,new[]{game});Check(tracker.Observe(now.AddMinutes(1),_=>false,new[]{game}).Count==0,"Stable session exit called failure");
        tracker.Arm(game,now);Check(tracker.Observe(now.AddSeconds(179),_=>false,new[]{game}).Count==0,"Launch timeout too early");Check(tracker.Observe(now.AddMinutes(3),_=>false,new[]{game}).Single().Kind==LaunchIssueKind.NotObserved,"Missing process not diagnosed");tracker.Arm(game,now);tracker.Observe(now,_=>false,Array.Empty<GameInfo>());Check(tracker.Observe(now.AddMinutes(4),_=>false,new[]{game}).Count==0,"Removed game still triggers warning");
        Check(new LaunchAttemptTracker().Observe(now,_=>true,new[]{game}).Count==0,"External session triggers launch helper");
        var direct=new LaunchAttemptTracker();direct.Arm(game,now,true);direct.Observe(now.AddSeconds(5),_=>false,new[]{game});Check(direct.Observe(now.AddSeconds(20),_=>false,new[]{game}).Single().Kind==LaunchIssueKind.ClosedQuickly,"Direct process exit between polls was missed");
        for(int i=0;i<35;i++)LaunchIssues.Add(folder,new(game.LibraryId,game.Name,now.AddSeconds(i),LaunchIssueKind.NotObserved,"fixture"));Check(LaunchIssues.Load(folder).Length==30&&LaunchIssues.Load(folder)[0].At==now.AddSeconds(5),"Launch issue history is not bounded");
        game.Performance=new(){ApplyOnLaunch=true};File.WriteAllText(game.ExePath,"fixture");var localUnsupported=new GameInfo{Name="Unsupported",ExePath=game.ExePath,SteamAppId="",Performance=game.Performance};Check(GameReadiness.Inspect(localUnsupported,()=>false,folder,prepare:false).All(r=>r.Name!="Профиль производительности"),"Plain launch still applies unsupported profile blocker");
        var productArgs=new[]{"--launch-product=valorant","--launch-patchline=live"};Check(LaunchPreparation.Arguments(localUnsupported,productArgs,true,false).SequenceEqual(productArgs),"Diagnostic retry loses required client arguments");Check(LaunchPreparation.Arguments(localUnsupported,productArgs,true,true).Length==0,"Safe mode preparation policy changed");
        File.WriteAllText(Path.Combine(folder,"launch-issues.json"),"[null,{\"Kind\":99}]");Check(LaunchIssues.Load(folder).Length==0,"Malformed launch history is accepted");
        if(ui)
        {
            var center=new TransferCenter(Path.Combine(folder,"transfer-cache"));center.ConfigureBandwidth(1000000,DownloadSpeedUnit.Megabytes);
            using(var panel=new DownloadBandwidthPanel(center)){var amount=Children(panel).OfType<NumericUpDown>().Single();var unit=Children(panel).OfType<ModernSelect>().Single();unit.SelectedIndex=0;Check(amount.Value==8,"Unit switch changes physical cap");unit.SelectedIndex=1;Check(amount.Value==1,"Unit switch does not round-trip");}
            using(var main=new Form1(folder,false)){center.ConfigureBandwidth(123456,DownloadSpeedUnit.Megabits);typeof(Form1).GetMethod("SaveGameMode",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{false,true});Check(LauncherPreferences.Load(folder).DownloadLimitBytesPerSecond==123456,"Unrelated preference save resets download cap");}
            int launched=0;using(var helper=new LaunchHelpForm(game,issues[0],()=>launched++,()=>{},()=>{},()=>{},()=>{},()=>{},()=>{})){helper.Show();Application.DoEvents();Children(helper).OfType<ModernButton>().Single(b=>b.Text=="Повторить без подготовки").PerformClick();Check(launched==1,"Plain launch button is not wired");}
            string shots=Path.GetFullPath("artifacts/update-help-previews");Directory.CreateDirectory(shots);
            using var downloads=new DownloadsForm(center);using var updates=new GameUpdatesForm(new[]{game,local},folder,reader:_=>Task.FromResult(entries));using var help=new LaunchHelpForm(game,issues[0],()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{});
            int index=0;foreach(var window in new Form[]{downloads,updates,help}){window.Show();Application.DoEvents();using var image=new Bitmap(window.Width,window.Height);window.DrawToBitmap(image,window.ClientRectangle);image.Save(Path.Combine(shots,(++index)+".png"));window.Close();checks++;}
        }
        Console.WriteLine($"PASS: {checks} update/help checks: bandwidth conversion/persistence/live throttling/cancellation/cache, manifest statuses/client routing, launch timeout/handoff/quick exit/stable sessions, bounded history, plain readiness, UI and settings preservation. Fixtures only.");
    }
}
