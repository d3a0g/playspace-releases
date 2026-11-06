using System.Runtime.InteropServices;

namespace GameLauncherGUI;

internal enum ControllerAction { Up,Down,Left,Right,Launch,Back,NextCollection,PreviousCollection }
internal sealed class ControllerNavigation
{
    private ushort previous;private long lastDirection;
    internal ControllerAction[] Update(ushort buttons,long milliseconds)
    {
        ushort fresh=(ushort)(buttons&~previous);bool changed=(buttons&15)!=(previous&15);previous=buttons;
        if((fresh&0x2000)!=0)return new[]{ControllerAction.Back};
        var actions=new List<ControllerAction>();if((fresh&0x1010)!=0)actions.Add(ControllerAction.Launch);if((fresh&0x0200)!=0)actions.Add(ControllerAction.NextCollection);if((fresh&0x0100)!=0)actions.Add(ControllerAction.PreviousCollection);
        if((buttons&15)!=0&&(changed||milliseconds-lastDirection>=220)){lastDirection=milliseconds;actions.Add((buttons&1)!=0?ControllerAction.Up:(buttons&2)!=0?ControllerAction.Down:(buttons&4)!=0?ControllerAction.Left:ControllerAction.Right);}return actions.ToArray();
    }
}
internal static class GamepadInput
{
    [StructLayout(LayoutKind.Sequential)]private struct Pad{public ushort Buttons;public byte LeftTrigger,RightTrigger;public short LeftX,LeftY,RightX,RightY;}
    [StructLayout(LayoutKind.Sequential)]private struct State{public uint Packet;public Pad Pad;}
    [DllImport("xinput1_4.dll",EntryPoint="XInputGetState")]private static extern uint Read(uint user,out State state);
    [DllImport("xinput9_1_0.dll",EntryPoint="XInputGetState")]private static extern uint ReadLegacy(uint user,out State state);
    internal static ushort? Poll()
    {
        for(uint user=0;user<4;user++)try{State state;uint code;try{code=Read(user,out state);}catch(DllNotFoundException){code=ReadLegacy(user,out state);}if(code!=0)continue;ushort buttons=state.Pad.Buttons;
            if(state.Pad.LeftX>16000)buttons|=8;else if(state.Pad.LeftX< -16000)buttons|=4;if(state.Pad.LeftY>16000)buttons|=1;else if(state.Pad.LeftY< -16000)buttons|=2;return buttons;
        }catch(Exception ex)when(ex is DllNotFoundException or EntryPointNotFoundException){return null;}return null;
    }
}
internal sealed class ControllerLibraryForm : StudioDialog
{
    private readonly CustomCheckListBox grid=new(){Dock=DockStyle.Fill,PosterView=true,PosterMinimumWidth=240,ShowCheckBoxes=false};
    private readonly ModernSelect collection=new(){Dock=DockStyle.Fill};private readonly Label hint=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};private readonly System.Windows.Forms.Timer timer=new(){Interval=60};
    private readonly ControllerNavigation navigation=new();private readonly Func<ushort?> poll;private readonly IReadOnlyList<GameInfo> games;private readonly LibraryCollections collections;private GameInfo[] visible=Array.Empty<GameInfo>();
    internal GameInfo? LaunchRequested{get;private set;}
    internal ControllerLibraryForm(IReadOnlyList<GameInfo> games,LibraryCollections collections,Func<GameInfo,Task<Bitmap?>> poster,Func<ushort?>? poll=null,bool fullscreen=true)
    {
        this.games=games;this.collections=collections;this.poll=poll??GamepadInput.Poll;Text="PLAYSPACE / Режим контроллера";Size=new Size(1200,850);MinimumSize=new Size(900,680);if(fullscreen)WindowState=FormWindowState.Maximized;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(32)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,68));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));ContentHost.Controls.Add(layout);layout.Controls.Add(new Label{Text="Твоя библиотека на большом экране",Dock=DockStyle.Fill,Font=new Font("Segoe UI",25,FontStyle.Bold)},0,0);collection.Items.Add("Все игры");collection.Items.AddRange(collections.State.Names.Cast<object>());collection.SelectedIndex=0;layout.Controls.Add(collection,0,1);layout.Controls.Add(grid,0,2);layout.Controls.Add(hint,0,3);
        async void Refresh(){visible=games.Where(g=>collection.SelectedIndex==0||collections.Contains(g,collection.SelectedItem!.ToString()!)).ToArray();grid.Items.Clear();foreach(var game in visible)grid.Items.Add(new CustomListItem(game.Name,false,game.PlaytimeLabel));grid.ResetView();if(visible.Length>0)grid.SelectItem(0);var snapshot=visible;for(int i=0;i<snapshot.Length&&!IsDisposed&&ReferenceEquals(snapshot,visible);i++){var image=await poster(snapshot[i]);if(IsDisposed||!ReferenceEquals(snapshot,visible))return;grid.Items[i].PosterImage=image;grid.Invalidate();}}
        collection.SelectedIndexChanged+=(s,e)=>Refresh();grid.ItemDoubleClick+=(s,e)=>Choose();timer.Tick+=(s,e)=>{if(!ContainsFocus)return;ushort? buttons=this.poll();hint.Text=buttons.HasValue?"Геймпад: крестовина / стик — выбор · A / Start — запуск · B — назад · LB / RB — коллекции":"Геймпад не обнаружен. Стрелки — выбор · Enter — запуск · Escape — назад";foreach(var action in navigation.Update(buttons??0,Environment.TickCount64))Apply(action);};Shown+=(s,e)=>{Refresh();grid.Focus();timer.Start();};
    }
    internal void Apply(ControllerAction action)
    {
        if(action==ControllerAction.Back){Close();return;}if(action==ControllerAction.Launch){Choose();return;}if(action is ControllerAction.NextCollection or ControllerAction.PreviousCollection){collection.SelectedIndex=(collection.SelectedIndex+(action==ControllerAction.NextCollection?1:collection.Items.Count-1))%collection.Items.Count;return;}
        grid.Navigate(action switch{ControllerAction.Up=>Keys.Up,ControllerAction.Down=>Keys.Down,ControllerAction.Left=>Keys.Left,_=>Keys.Right});
    }
    private void Choose(){if(grid.SelectedIndex<0||grid.SelectedIndex>=visible.Length)return;LaunchRequested=visible[grid.SelectedIndex];DialogResult=DialogResult.OK;}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
