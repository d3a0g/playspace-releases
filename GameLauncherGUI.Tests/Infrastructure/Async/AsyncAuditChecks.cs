using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using GameLauncherGUI;

internal static class AsyncAuditChecks
{
    private static int checks;
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    private static void Pump(Task task,bool cancelled=false)
    {
        var context=SynchronizationContext.Current;var watch=Stopwatch.StartNew();
        while(!task.IsCompleted){Application.DoEvents();SynchronizationContext.SetSynchronizationContext(context);Thread.Sleep(1);if(watch.Elapsed>TimeSpan.FromSeconds(5))throw new Exception("Async audit fixture timed out");}
        Application.DoEvents();SynchronizationContext.SetSynchronizationContext(context);
        try{task.GetAwaiter().GetResult();if(cancelled)throw new Exception("Expected cancellation");}catch(OperationCanceledException)when(cancelled){}
    }
    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,Flags)!.GetValue(instance)!;
    private sealed class BlockingHandler : HttpMessageHandler
    {
        internal bool Started,Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Started=true;try{await Task.Delay(Timeout.Infinite,token);throw new Exception("Unreachable");}finally{Cancelled=token.IsCancellationRequested;}
        }
    }
    internal static void Run()
    {
        Exception? failure=null;var thread=new Thread(()=>{try{RunUi();}catch(Exception ex){failure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void RunUi()
    {
        Application.EnableVisualStyles();using var host=new Form();host.Show();Application.DoEvents();
        using var context=new WindowsFormsSynchronizationContext();SynchronizationContext.SetSynchronizationContext(context);
        var errors=new List<(string,Exception)>();
        using(var scope=new UiTaskScope(host,(name,error)=>errors.Add((name,error)))){
            Pump(scope.RunAsync("sync",_=>throw new IOException("fixture")));Check(errors.Count==1&&scope.ActiveCount==0,"Synchronous factory fault was lost");
            var work=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int starts=0;
            Check(scope.StartUnique("job",_=>{starts++;return work.Task;}),"Unique work did not start");Check(!scope.StartUnique("job",_=>{starts++;return Task.CompletedTask;})&&starts==1,"Duplicate work queued");
            Pump(Task.Run(()=>work.SetException(new IOException("fixture"))));Pump(scope.RunAsync("yield",_=>Task.Yield().AsTask()));
            Check(errors.Count==2&&scope.ActiveCount==0,"Detached async fault was not reported/released");
            Check(scope.StartUnique("job",_=>Task.CompletedTask),"Failed unique work cannot be retried");
            var canceled=scope.RunAsync("cancel",ct=>Task.Delay(Timeout.Infinite,ct));scope.Dispose();Pump(canceled);Check(errors.Count==2&&scope.ActiveCount==0,"Owner cancellation treated as failure");
            scope.Start("closed",_=>{starts++;return Task.CompletedTask;});Check(starts==1,"Closed scope accepted work");
            var wrongThread=Task.Run(()=>scope.Start("worker",_=>Task.CompletedTask));try{Pump(wrongThread);throw new Exception("Cross-thread start accepted");}catch(InvalidOperationException){checks++;}
        }
        using(var child=new Control()){
            using var scope=new UiTaskScope(child,(name,error)=>errors.Add((name,error)));var late=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task=scope.RunAsync("late",_=>late.Task);child.Dispose();late.SetException(new IOException("late fixture"));Pump(task);
            Check(scope.ActiveCount==0&&errors.Count==3,"Late failure after disposal was lost");
        }
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceAsyncAudit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);new LauncherPreferences{OfflineMode=false,OnboardingCompleted=true}.Save(folder);
        using(var form=new Form1(folder,false)){
            Field<System.Windows.Forms.Timer>(form,"sessionTimer").Stop();
            typeof(Form1).GetField("steamKey",Flags)!.SetValue(form,"fixture");typeof(Form1).GetField("steamUser",Flags)!.SetValue(form,"fixture-user");
            using var handler=new BlockingHandler();using var client=new HttpClient(handler);form.AchievementClient=client;
            var task=(Task)typeof(Form1).GetMethod("SyncLibraryAchievements",Flags)!.Invoke(form,new object?[]{null})!;Check(handler.Started,"Steam fixture request did not start");form.Dispose();Pump(task);
            Check(handler.Cancelled,"Closing launcher did not cancel Steam request");Check(Field<SemaphoreSlim>(form,"achievementGate").CurrentCount==1,"Steam gate leaked on close");
        }
        using(var form=new Form1(folder,false)){
            Field<System.Windows.Forms.Timer>(form,"sessionTimer").Stop();typeof(Form1).GetField("steamKey",Flags)!.SetValue(form,"fixture");
            var gate=Field<SemaphoreSlim>(form,"achievementGate");Check(gate.Wait(0),"Cannot acquire fixture gate");
            var task=(Task)typeof(Form1).GetMethod("RefreshSteamHours",Flags)!.Invoke(form,null)!;Check(!task.IsCompleted,"Queued Steam operation did not wait");form.Dispose();Pump(task,true);
            Check(!Field<bool>(form,"refreshingSteamHours"),"Cancelled queued refresh left busy flag set");gate.Release();
        }
        using(var recommendations=new RecommendationsForm(folder,_=>Task.FromException<Bitmap?>(new IOException("fixture")))){
            typeof(RecommendationsForm).GetMethod("RefreshGames",Flags)!.Invoke(recommendations,null);
            var scope=Field<UiTaskScope>(recommendations,"tasks");Check(scope.ActiveCount==0,"Failed poster refresh remained active");Check(Field<Label>(recommendations,"status").Text.Contains("Не удалось"),"Poster error not visible");
        }
        var transfers=new TransferCenter(Path.Combine(folder,"transfers"),()=>throw new InvalidOperationException("fixture client"),()=>false);
        var failed=transfers.Fetch("fixture",new Uri("https://example.test/file"),1024,default);
        try{Pump(failed);throw new Exception("Transfer failure swallowed");}catch(InvalidOperationException){checks++;}
        Check(transfers.Items().Single().State==TransferState.Failed,"Unexpected exception left transfer running");
        using(var handler=new BlockingHandler()){
            var queue=new TransferCenter(Path.Combine(folder,"cancel-queue"),()=>new HttpClient(handler,false),()=>false);
            var first=queue.Fetch("active",new Uri("https://example.test/first"),1024,default);
            var second=queue.Fetch("queued",new Uri("https://example.test/second"),1024,default);queue.CancelAll();Pump(first,true);Pump(second,true);
            Check(handler.Cancelled,"Application shutdown did not cancel active transfer");Check(queue.Items().All(item=>item.State==TransferState.Cancelled),"Application shutdown left queued transfer active");
        }
        host.Close();Console.WriteLine($"PASS: {checks} async audit checks: observed sync/async/late faults, unique jobs/retry, cancellation, UI ownership, Steam HTTP/gate disposal and recommendation error boundary. No network or game changes.");
    }
    private static async Task AsTask(this System.Runtime.CompilerServices.YieldAwaitable value){await value;}
}
