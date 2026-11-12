using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GameLauncherGUI;

internal static class UiMotion
{
    private static readonly ConditionalWeakTable<Form,Entrance> entrances=new();
    internal static float Ease(float value){value=Math.Clamp(value,0,1);return 1-(1-value)*(1-value)*(1-value);}
    internal static void Enter(Form form)
    {
        if(PresentationOptions.ReduceMotion||SystemInformation.TerminalServerSession||form.IsDisposed||entrances.TryGetValue(form,out _))return;
        var entrance=new Entrance(form);entrances.Add(form,entrance);entrance.Start();
    }
    private sealed class Entrance
    {
        private readonly Form form;
        private readonly System.Windows.Forms.Timer timer=new(){Interval=16};
        private readonly Stopwatch clock=new();
        private bool finished;
        internal Entrance(Form form){this.form=form;timer.Tick+=Tick;form.Disposed+=Disposed;}
        internal void Start(){form.Opacity=.92;clock.Start();timer.Start();}
        private void Tick(object? sender,EventArgs e)
        {
            if(form.IsDisposed||!form.Visible||PresentationOptions.ReduceMotion){Finish();return;}
            float progress=(float)clock.Elapsed.TotalMilliseconds/220;
            form.Opacity=.92+.08*Ease(progress);if(progress>=1)Finish();
        }
        private void Disposed(object? sender,EventArgs e)=>Finish();
        private void Finish()
        {
            if(finished)return;finished=true;timer.Stop();timer.Dispose();form.Disposed-=Disposed;
            if(!form.IsDisposed&&!form.Disposing)form.Opacity=1;
            entrances.Remove(form);
        }
    }
}
