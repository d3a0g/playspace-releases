using System.Collections;
using System.Diagnostics;
using System.Reflection;
using GameLauncherGUI;

internal static class ConcurrencyChecks
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void PumpUntil(Func<bool> ready){var context=SynchronizationContext.Current;var timer=Stopwatch.StartNew();while(!ready()){Application.DoEvents();SynchronizationContext.SetSynchronizationContext(context);Thread.Sleep(1);if(timer.Elapsed>TimeSpan.FromSeconds(5))throw new Exception("UI continuation timed out");}Application.DoEvents();SynchronizationContext.SetSynchronizationContext(context);}
    private static void Wait(Task task){PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static Task Sample(Form1 form)=>(Task)typeof(Form1).GetMethod("TickSessionsAsync",Fields)!.Invoke(form,null)!;
    private static T Field<T>(Form1 form,string name)=>(T)typeof(Form1).GetField(name,Fields)!.GetValue(form)!;
    private static bool Disposed(Bitmap image){try{_ = image.Width;return false;}catch(ArgumentException){return true;}}

    internal static void Run()
    {
        Exception? failure=null;
        var thread=new Thread(()=>{try{RunOnUiThread();}catch(Exception ex){failure=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void RunOnUiThread()
    {
        Application.EnableVisualStyles();
        using var host=new Form();host.Show();Application.DoEvents();
        var previousContext=SynchronizationContext.Current;
        using var context=new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        using(var cache=new UiImageCache()){
            int loads=0;var worker=new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Bitmap?> Load(CancellationToken token){loads++;return worker.Task;}
            var calls=Enumerable.Range(0,50).Select(_=>cache.GetAsync("620",Load)).ToArray();
            Check(loads==1&&calls.All(t=>ReferenceEquals(t,calls[0])),"Duplicate requests did not share a load");
            var image=new Bitmap(4,4);Wait(Task.Run(()=>worker.SetResult(image)));Wait(Task.WhenAll(calls));
            Check(calls.All(t=>ReferenceEquals(t.Result,image)),"Image ownership differs between callers");
            Check(ReferenceEquals(cache.GetAsync("620",Load).Result,image)&&loads==1,"Cache missed completed image");
            var rejected=Task.Run(()=>cache.GetAsync("999",Load));try{Wait(rejected);throw new Exception("Worker accessed UI cache");}catch(InvalidOperationException){checks++;}
            var failed=cache.GetAsync("bad",_=>Task.FromException<Bitmap?>(new IOException("fixture")));try{Wait(failed);throw new Exception("Load failure swallowed");}catch(IOException){checks++;}
            var retried=cache.GetAsync("bad",_=>Task.FromResult<Bitmap?>(new Bitmap(2,2)));Wait(retried);Check(retried.Result!=null,"Failed load cannot be retried");
            cache.Dispose();Check(Disposed(image),"Cached bitmap not released");Check(cache.GetAsync("620",Load).Result==null,"Disposed cache returned image");
        }
        using(var cache=new UiImageCache()){
            var worker=new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken token=default;
            var request=cache.GetAsync("pending",t=>{token=t;return worker.Task;});cache.Dispose();Wait(request);
            Check(request.Result==null&&token.IsCancellationRequested,"Disposal did not cancel pending consumers");
            var late=new Bitmap(3,3);worker.SetResult(late);PumpUntil(()=>Disposed(late));checks++;
        }
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceConcurrency-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        new LauncherPreferences{OfflineMode=true,OnboardingCompleted=true}.Save(folder);
        using(var form=new Form1(folder,false)){
            Field<System.Windows.Forms.Timer>(form,"sessionTimer").Stop();
            var games=Field<List<GameInfo>>(form,"myGames");var game=new GameInfo{Name="Fixture",ExePath=Path.Combine(folder,"fixture.exe")};File.WriteAllText(game.ExePath,"fixture");games.Add(game);
            typeof(Form1).GetMethod("ArmTracking",Fields)!.Invoke(form,new object[]{game});
            Field<Dictionary<GameInfo,DateTimeOffset>>(form,"sessionStarted")[game]=DateTimeOffset.UtcNow;
            var worker=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;form.SessionProcessSampler=()=>{calls++;return worker.Task;};
            var first=Sample(form);var second=Sample(form);Check(calls==1&&second.IsCompleted&&!first.IsCompleted,"Timer sampling overlapped");
            games.Clear();worker.SetResult(new[]{game.ExePath});Wait(first);
            Check(Field<IDictionary>(form,"sessions").Count==0&&Field<IDictionary>(form,"sessionStarted").Count==0,"Deleted game retained tracking state");
            Check(!Field<bool>(form,"sampling"),"Sampling guard not reset");
            form.SessionProcessSampler=()=>Task.FromException<string[]>(new IOException("fixture"));try{Wait(Sample(form));throw new Exception("Sample failure swallowed");}catch(IOException){checks++;}Check(!Field<bool>(form,"sampling"),"Failure left sampling stuck");
            form.SessionProcessSampler=()=>Task.FromResult(Array.Empty<string>());Wait(Sample(form));checks++;
            games.Add(game);game.LastLaunchUtc=null;game.PlayedSeconds=42;
            form.SessionProcessSampler=()=>Task.FromResult(new[]{game.ExePath});Wait(Sample(form));
            Check(game.LastLaunchUtc.HasValue,"Externally started game did not record first detection");
            Check(game.PlayedSeconds==42,"First detection charged an unobserved interval");
            var firstDetection=game.LastLaunchUtc;Wait(Sample(form));
            Check(game.PlayedSeconds>=42&&game.LastLaunchUtc==firstDetection,"Subsequent poll reset the session start");
            form.SessionProcessSampler=()=>Task.FromResult(Array.Empty<string>());Wait(Sample(form));
            var offThread=Task.Run(()=>Sample(form));try{Wait(offThread);throw new Exception("Worker accessed session state");}catch(InvalidOperationException){checks++;}
            var late=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);form.SessionProcessSampler=()=>late.Task;var pending=Sample(form);form.Dispose();late.SetResult(Array.Empty<string>());Wait(pending);Check(!Field<bool>(form,"sampling"),"Disposal left sampling stuck");
        }
        // Real cached-file path, no network or running game required.
        string posters=Path.Combine(folder,"posters");Directory.CreateDirectory(posters);using(var bitmap=new Bitmap(8,8))bitmap.Save(Path.Combine(posters,"620.jpg"),System.Drawing.Imaging.ImageFormat.Jpeg);
        var offline=PosterLoader.LoadAsync(folder,"620",true,default);Wait(offline);using(var bitmap=offline.Result)Check(bitmap?.Width==8,"Offline poster decode failed");
        var absent=PosterLoader.LoadAsync(folder,"999",true,default);Wait(absent);Check(absent.Result==null,"Offline loader tried unavailable image");
        host.Close();SynchronizationContext.SetSynchronizationContext(previousContext);Console.WriteLine($"PASS: {checks} concurrency/lifetime checks: UI ownership, shared in-flight loads, retry, bitmap disposal, timer reentrancy/failure, removed games and close during sampling. Offline fixtures only.");
    }
}
