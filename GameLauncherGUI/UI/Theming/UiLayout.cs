using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace GameLauncherGUI;

internal static class UiLayout
{
    private sealed class FlowState { internal bool Busy; internal readonly Dictionary<Control,Size> Sizes=new(); }
    private static readonly ConditionalWeakTable<FlowLayoutPanel,FlowState> flows=new();
    private sealed record HeadingSize(float Original);
    private static readonly ConditionalWeakTable<StudioHeading,HeadingSize> headings=new();
    internal static void Apply(Control root)
    {
        if(root is TableLayoutPanel table){while(table.ColumnStyles.Count<table.ColumnCount)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/Math.Max(1,table.ColumnCount)));if(table.RowCount==1&&table.RowStyles.Count==0)table.RowStyles.Add(new RowStyle(SizeType.Percent,100));}
        foreach(Control child in root.Controls.Cast<Control>().ToArray())Apply(child);
        if(root is StudioHeading heading&&heading.Parent is TableLayoutPanel headingTable){int row=headingTable.GetRow(heading);if(row>=0&&row<headingTable.RowStyles.Count&&headingTable.RowStyles[row].SizeType==SizeType.Absolute){var original=headings.GetValue(heading,_=>new(headingTable.RowStyles[row].Height));headingTable.RowStyles[row].Height=Math.Max(original.Original,heading.RequiredHeight+heading.Margin.Vertical);}}
        if(root is TextBox input&&input.Parent is not (InputSurface or ModernSearchField)&&input.BorderStyle!=BorderStyle.None&&input.Parent is TableLayoutPanel or FlowLayoutPanel){Wrap(input);return;}
        if(root is ComboBox combo){combo.FlatStyle=FlatStyle.Flat;combo.BackColor=Color.FromArgb(27,33,48);combo.ForeColor=Color.FromArgb(233,236,247);}
        if(root is NumericUpDown numeric){numeric.BorderStyle=BorderStyle.None;numeric.BackColor=Color.FromArgb(27,33,48);numeric.ForeColor=Color.FromArgb(233,236,247);}
        if(root is ComboBox or NumericUpDown&&root.Parent is TableLayoutPanel inputTable){int row=inputTable.GetRow(root);if(row>=0&&row<inputTable.RowStyles.Count&&inputTable.RowStyles[row].SizeType==SizeType.Absolute&&inputTable.RowStyles[row].Height>=40){int top=Math.Max(3,((int)inputTable.RowStyles[row].Height-root.PreferredSize.Height)/2);root.Margin=new Padding(root.Margin.Left,top,root.Margin.Right,3);}}
        if(root is CheckBox check&&root is not ModernToggle){check.FlatStyle=FlatStyle.Flat;check.FlatAppearance.BorderColor=Color.FromArgb(78,85,108);check.FlatAppearance.CheckedBackColor=VisualTheme.Accent;}
        if(root is ListBox list){list.BorderStyle=BorderStyle.None;list.BackColor=Color.FromArgb(23,28,42);list.ForeColor=Color.FromArgb(230,234,246);}
        if(root is FlowLayoutPanel flow&&flow.FlowDirection==FlowDirection.TopDown&&!flow.WrapContents){
            if(!flows.TryGetValue(flow,out var state)){state=new();flows.Add(flow,state);flow.SizeChanged+=(s,e)=>Arrange(flow,state);flow.ControlAdded+=(s,e)=>Arrange(flow,state);}
            Arrange(flow,state);
        }
    }
    private static void Arrange(FlowLayoutPanel flow,FlowState state)
    {
        if(state.Busy||flow.IsDisposed||flow.ClientSize.Width<50)return;state.Busy=true;flow.SuspendLayout();
        try{
            int available=Math.Max(80,flow.ClientSize.Width-flow.Padding.Horizontal-(flow.AutoScroll?SystemInformation.VerticalScrollBarWidth:0));
            foreach(Control child in flow.Controls){
                if(!state.Sizes.TryGetValue(child,out var preferred))state.Sizes[child]=preferred=child.Size;
                int width=Math.Max(60,Math.Min(preferred.Width,available-child.Margin.Horizontal));
                if(child is Label {AutoSize:true} autoLabel)autoLabel.MaximumSize=new Size(width,0);
                child.Width=width;
                if(child is Label label&&!label.AutoSize&&!label.AutoEllipsis){using var g=label.CreateGraphics();int height=(int)Math.Ceiling(g.MeasureString(label.Text,label.Font,Math.Max(1,width-4)).Height)+8;label.Height=Math.Max(preferred.Height,height);}
                if(child is FlowLayoutPanel row&&row.FlowDirection==FlowDirection.LeftToRight){row.WrapContents=true;row.AutoSize=true;row.AutoSizeMode=AutoSizeMode.GrowAndShrink;row.MinimumSize=new Size(width,preferred.Height);row.MaximumSize=new Size(width,0);}
            }
        }finally{
            flow.ResumeLayout(true);
            if(flow.AutoScroll&&flow.HorizontalScroll.Visible&&flow.Controls.Cast<Control>().Where(c=>c.Visible).All(c=>c.Right+c.Margin.Right<=flow.ClientSize.Width-flow.Padding.Right)){
                var position=flow.AutoScrollPosition;flow.AutoScroll=false;flow.AutoScroll=true;flow.AutoScrollPosition=new Point(0,-position.Y);
            }
            state.Busy=false;
        }
    }
    private static void Wrap(TextBox input)
    {
        var parent=input.Parent!;int index=parent.Controls.GetChildIndex(input);var table=parent as TableLayoutPanel;var cell=table?.GetCellPosition(input);int columns=table?.GetColumnSpan(input)??1,rows=table?.GetRowSpan(input)??1;
        var bounds=input.Bounds;var dock=input.Dock;var anchor=input.Anchor;var margin=input.Margin;int tab=input.TabIndex;
        var host=new InputSurface(input){Bounds=bounds,Dock=dock,Anchor=anchor,Margin=margin,TabIndex=tab};
        if(parent is FlowLayoutPanel&&!input.Multiline)host.Height=Math.Max(42,input.Height);
        parent.SuspendLayout();try{parent.Controls.Remove(input);host.Controls.Add(input);parent.Controls.Add(host);parent.Controls.SetChildIndex(host,index);if(table!=null&&cell.HasValue){table.SetCellPosition(host,cell.Value);table.SetColumnSpan(host,columns);table.SetRowSpan(host,rows);}host.Arrange();}finally{parent.ResumeLayout(true);}
    }
    internal static void Fit(Form form)
    {
        Rectangle work=Screen.FromControl(form).WorkingArea;work.Inflate(-12,-12);
        if(form.MinimumSize.Width>work.Width||form.MinimumSize.Height>work.Height)form.MinimumSize=new(Math.Min(form.MinimumSize.Width,work.Width),Math.Min(form.MinimumSize.Height,work.Height));
        form.Size=new(Math.Min(form.Width,work.Width),Math.Min(form.Height,work.Height));
        form.Location=new(Math.Clamp(form.Left,work.Left,Math.Max(work.Left,work.Right-form.Width)),Math.Clamp(form.Top,work.Top,Math.Max(work.Top,work.Bottom-form.Height)));
    }
}

internal sealed class InputSurface : Panel
{
    private readonly TextBox editor;
    internal InputSurface(TextBox editor)
    {
        this.editor=editor;DoubleBuffered=true;ResizeRedraw=true;TabStop=false;
        editor.BorderStyle=BorderStyle.None;editor.Dock=DockStyle.None;editor.Anchor=AnchorStyles.Top|AnchorStyles.Left;editor.BackColor=Color.FromArgb(23,28,42);editor.ForeColor=Color.FromArgb(229,234,247);editor.Margin=Padding.Empty;
        editor.GotFocus+=(s,e)=>Invalidate();editor.LostFocus+=(s,e)=>Invalidate();editor.FontChanged+=(s,e)=>Arrange();SizeChanged+=(s,e)=>Arrange();Click+=(s,e)=>editor.Focus();
    }
    internal void Arrange(){if(editor.IsDisposed)return;int height=editor.Multiline?Math.Max(1,Height-20):editor.PreferredHeight;editor.SetBounds(12,editor.Multiline?10:Math.Max(0,(Height-height)/2),Math.Max(1,Width-24),height);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.Clear(Parent?.BackColor??Color.FromArgb(14,17,27));if(Width<2||Height<2)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var path=VisualTheme.Rounded(new(0,0,Width-1,Height-1),4);using var fill=new SolidBrush(editor.BackColor);using var border=new Pen(editor.Focused?VisualTheme.Accent:Color.FromArgb(47,54,76));e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
}
