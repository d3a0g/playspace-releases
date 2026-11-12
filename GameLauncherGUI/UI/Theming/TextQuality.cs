using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameLauncherGUI;

internal sealed class SmoothLabel : Label
{
    internal SmoothLabel(){UseCompatibleTextRendering=true;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;base.OnPaint(e);}
}
internal static class NativeTextQuality
{
    private sealed class State {internal IntPtr Font;internal bool Busy;}
    private static readonly ConditionalWeakTable<Control,State> states=new();
    internal static void Register(Control control)
    {
        if(control is not(TextBoxBase or ComboBox or UpDownBase))return;
        if(states.TryGetValue(control,out _))return;var state=new State();states.Add(control,state);
        control.HandleCreated+=(s,e)=>Apply(control,state);control.FontChanged+=(s,e)=>Apply(control,state);control.DpiChangedAfterParent+=(s,e)=>Apply(control,state);
        control.HandleDestroyed+=(s,e)=>Release(state);control.Disposed+=(s,e)=>Release(state);Apply(control,state);
    }
    private static void Apply(Control control,State state)
    {
        if(state.Busy||control.IsDisposed||!control.IsHandleCreated)return;state.Busy=true;
        try{using var graphics=control.CreateGraphics();var spec=new LogFont();control.Font.ToLogFont(spec,graphics);spec.Quality=5;var font=CreateFontIndirect(spec);if(font==IntPtr.Zero)return;SendMessage(control.Handle,0x30,font,new IntPtr(1));Release(state);state.Font=font;}
        finally{state.Busy=false;}
    }
    private static void Release(State state){if(state.Font!=IntPtr.Zero){DeleteObject(state.Font);state.Font=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private sealed class LogFont
    {
        public int Height,Width,Escapement,Orientation,Weight;
        public byte Italic,Underline,StrikeOut,CharacterSet,OutPrecision,ClipPrecision,Quality,PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string FaceName="";
    }
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,EntryPoint="CreateFontIndirectW")]private static extern IntPtr CreateFontIndirect(LogFont font);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
}
