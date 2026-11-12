namespace GameLauncherGUI;

// UI-thread owned: callers borrow images; only this cache disposes them.
// Register an in-flight task before starting work so reentrant UI calls share one load.
internal sealed class UiImageCache : IDisposable
{
    private readonly int ownerThread=Environment.CurrentManagedThreadId;
    private readonly Dictionary<string,Bitmap> images=new();
    private readonly Dictionary<string,TaskCompletionSource<Bitmap?>> pending=new();
    private readonly CancellationTokenSource lifetime=new();
    private bool disposed;

    internal void VerifyAccess()
    {
        if(Environment.CurrentManagedThreadId!=ownerThread)
            throw new InvalidOperationException("Image cache must be accessed on its owning UI thread.");
    }
    internal Task<Bitmap?> GetAsync(string id,Func<CancellationToken,Task<Bitmap?>> loader)
    {
        VerifyAccess();
        if(disposed)return Task.FromResult<Bitmap?>(null);
        if(images.TryGetValue(id,out var image))return Task.FromResult<Bitmap?>(image);
        if(pending.TryGetValue(id,out var current))return current.Task;
        if(SynchronizationContext.Current is not System.Windows.Forms.WindowsFormsSynchronizationContext)
            throw new InvalidOperationException("Image loading requires the WinForms synchronization context.");
        var completion=new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Add(id,completion);
        _=CompleteAsync(id,loader,completion,lifetime.Token);
        return completion.Task;
    }
    private async Task CompleteAsync(string id,Func<CancellationToken,Task<Bitmap?>> loader,TaskCompletionSource<Bitmap?> completion,CancellationToken token)
    {
        Bitmap? loaded=null;
        try{
            loaded=await loader(token);
            VerifyAccess();
            if(disposed||token.IsCancellationRequested){loaded?.Dispose();loaded=null;}
            if(loaded!=null)images.Add(id,loaded);
            completion.TrySetResult(loaded);
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){loaded?.Dispose();completion.TrySetResult(null);}
        catch(Exception ex){loaded?.Dispose();completion.TrySetException(ex);_ = completion.Task.Exception;/* Observe even if every consumer cancelled its wait; awaiters still receive the fault. */}
        finally{pending.Remove(id);}
    }
    public void Dispose()
    {
        VerifyAccess();if(disposed)return;disposed=true;lifetime.Cancel();
        foreach(var image in images.Values)image.Dispose();images.Clear();
        foreach(var completion in pending.Values)completion.TrySetResult(null);pending.Clear();
        lifetime.Dispose();
    }
}
