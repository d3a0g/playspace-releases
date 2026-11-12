namespace GameLauncherGUI;

// Boundary for event/detached UI operations. Inner service methods still return faultable Tasks.
internal sealed class UiTaskScope : IDisposable
{
    private readonly int ownerThread=Environment.CurrentManagedThreadId;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Action<string,Exception> report;
    private readonly HashSet<string> running=new();
    private bool stopped,released,cancelling;
    private int active;
    internal CancellationToken Token {get;}
    internal int ActiveCount=>active;
    internal UiTaskScope(Control owner,Action<string,Exception> report)
    {
        this.report=report;Token=lifetime.Token;owner.Disposed+=(s,e)=>Dispose();
    }
    private void Verify(){if(Environment.CurrentManagedThreadId!=ownerThread)throw new InvalidOperationException("UI operations must start on their owning thread.");}
    internal void Start(string name,Func<CancellationToken,Task> work)
    {
        Verify();if(stopped)return;
        // RunCore observes every exception, including faults after the owning window closes.
        _=RunCore(name,work,false);
    }
    internal bool StartUnique(string name,Func<CancellationToken,Task> work)
    {
        Verify();if(stopped||!running.Add(name))return false;
        _=RunCore(name,work,true);return true;
    }
    internal Task RunAsync(string name,Func<CancellationToken,Task> work)
    {
        Verify();return stopped?Task.CompletedTask:RunCore(name,work,false);
    }
    private async Task RunCore(string name,Func<CancellationToken,Task> work,bool unique)
    {
        active++;
        try{Token.ThrowIfCancellationRequested();await work(Token);}
        catch(OperationCanceledException)when(Token.IsCancellationRequested){}
        catch(Exception ex){try{report(name,ex);}catch(Exception reportingError){System.Diagnostics.Trace.TraceError("Async error reporter failed: "+reportingError.GetType().Name);}}
        finally{if(unique)running.Remove(name);active--;ReleaseIfFinished();}
    }
    public void Dispose(){Verify();if(stopped)return;stopped=true;cancelling=true;try{lifetime.Cancel();}catch(AggregateException ex){try{report("Отмена фоновых операций",ex);}catch{}}finally{cancelling=false;ReleaseIfFinished();}}
    private void ReleaseIfFinished(){if(stopped&&!cancelling&&active==0&&!released){released=true;lifetime.Dispose();}}
}
