using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GameLauncherGUI;

internal static class SmoothText
{
    public static void Draw(Graphics g,string? text,Font font,Point point,Color color)=>Draw(g,text,font,new Rectangle(point,new Size(2000,100)),color,TextFormatFlags.NoPadding);
    public static void Draw(Graphics g,string? text,Font font,Rectangle bounds,Color color,TextFormatFlags flags=TextFormatFlags.Default)
    {
        if(bounds.Width<=0||bounds.Height<=0)return;
        var old=g.TextRenderingHint;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
        if(PresentationOptions.HighContrast)color=Color.White;
        using var brush=new SolidBrush(color);using var format=new StringFormat {Alignment=flags.HasFlag(TextFormatFlags.HorizontalCenter)?StringAlignment.Center:flags.HasFlag(TextFormatFlags.Right)?StringAlignment.Far:StringAlignment.Near,LineAlignment=flags.HasFlag(TextFormatFlags.VerticalCenter)?StringAlignment.Center:StringAlignment.Near,Trimming=flags.HasFlag(TextFormatFlags.EndEllipsis)?StringTrimming.EllipsisCharacter:StringTrimming.None};
        if(!flags.HasFlag(TextFormatFlags.WordBreak))format.FormatFlags|=StringFormatFlags.NoWrap;
        float scale=PresentationOptions.DrawingScale(font);using var scaled=scale==1?null:new Font(font.FontFamily,font.Size*scale,font.Style);g.DrawString(text??"",scaled??font,brush,bounds,format);g.TextRenderingHint=old;
    }
    public static void Draw(Graphics g,string? text,Font font,Rectangle bounds,Color color,Color background,TextFormatFlags flags){using var brush=new SolidBrush(background);g.FillRectangle(brush,bounds);Draw(g,text,font,bounds,color,flags);}
}
internal static class UiTypography
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> observed=new();
    public static void Apply(Control parent)
    {
        NativeTextQuality.Register(parent);
        if(string.IsNullOrEmpty(parent.AccessibleName))
        {
            if(parent is ButtonBase or LinkLabel)parent.AccessibleName=parent.Text.Replace("&","");
            else if(parent is TextBox editor&&!string.IsNullOrWhiteSpace(editor.PlaceholderText))parent.AccessibleName=editor.PlaceholderText;
        }
        if(parent is Label label)label.UseCompatibleTextRendering=true;
        if(parent is CheckBox check)check.UseCompatibleTextRendering=true;
        if(parent is RadioButton radio)radio.UseCompatibleTextRendering=true;
        foreach(Control child in parent.Controls)Apply(child);
        if(!observed.TryGetValue(parent,out _)){observed.Add(parent,new());parent.ControlAdded+=(s,e)=>Apply(e.Control!);}
    }
}
internal sealed class ModernSearchField : UserControl
{
    private readonly TextBox editor;
    private readonly ModernButton clear;
    private readonly Label placeholder;
    public ModernSearchField(TextBox editor,ModernButton? clearButton=null)
    {
        this.editor=editor;clear=clearButton??new ModernButton("×",Color.FromArgb(23,28,42));
        DoubleBuffered=true;ResizeRedraw=true;Height=42;BackColor=Color.FromArgb(23,28,42);Padding=Padding.Empty;
        editor.BorderStyle=BorderStyle.None;editor.Dock=DockStyle.None;editor.Margin=Padding.Empty;editor.BackColor=BackColor;editor.ForeColor=Color.FromArgb(235,237,247);editor.Font=new Font("Segoe UI",10);
        clear.Dock=DockStyle.None;clear.Margin=Padding.Empty;clear.AccessibleName="Очистить поиск";
        placeholder=new SmoothLabel{Text=editor.PlaceholderText,ForeColor=Color.FromArgb(147,159,188),BackColor=BackColor,Font=editor.Font,UseCompatibleTextRendering=true,TextAlign=ContentAlignment.MiddleLeft,Cursor=Cursors.IBeam};
        placeholder.Click+=(s,e)=>editor.Focus();
        void UpdateHint(){placeholder.Visible=editor.TextLength==0;placeholder.BringToFront();}
        Controls.Add(editor);Controls.Add(clear);Controls.Add(placeholder);clear.Click+=(s,e)=>{editor.Clear();editor.Focus();};editor.TextChanged+=(s,e)=>{clear.Enabled=editor.TextLength>0;UpdateHint();Invalidate();};editor.GotFocus+=(s,e)=>UpdateHint();editor.LostFocus+=(s,e)=>UpdateHint();clear.Enabled=editor.TextLength>0;
        UpdateHint();
        Resize+=(s,e)=>Arrange();editor.FontChanged+=(s,e)=>Arrange();Arrange();
    }
    private void Arrange(){int height=editor.PreferredHeight;editor.SetBounds(38,Math.Max(3,(Height-height)/2),Math.Max(1,Width-78),height);placeholder.SetBounds(editor.Left,2,editor.Width,Math.Max(1,Height-4));clear.SetBounds(Math.Max(0,Width-34),Math.Max(0,(Height-28)/2),28,28);}
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.Escape&&editor.TextLength>0){editor.Clear();return true;}return base.ProcessCmdKey(ref msg,keyData);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var path=VisualTheme.Rounded(new Rectangle(0,0,Width-1,Height-1),4);using var border=new Pen(ContainsFocus?VisualTheme.Accent:Color.FromArgb(47,54,76));e.Graphics.DrawPath(border,path);using var pen=new Pen(Color.FromArgb(156,165,190),1.7f);float y=Height/2f;e.Graphics.DrawEllipse(pen,13,y-7,10,10);e.Graphics.DrawLine(pen,22,y+2,27,y+7);}
}
internal sealed class FormattedDescription : Control
{
    protected override AccessibleObject CreateAccessibilityInstance()=>new DescriptionAccessibility(this);
    private sealed class DescriptionAccessibility(FormattedDescription owner):ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role=>AccessibleRole.Text;
        public override string? Name{get=>owner.AccessibleName??"Описание";set=>owner.AccessibleName=value;}
        public override string? Value{get=>owner.Text;set{}}
        public override AccessibleStates State=>base.State|AccessibleStates.ReadOnly|AccessibleStates.Focusable;
    }
    private string content="";private int offset;private int total;
    public FormattedDescription(){DoubleBuffered=true;ResizeRedraw=true;TabStop=true;Font=new Font("Segoe UI",11);BackColor=Color.FromArgb(23,28,42);ForeColor=Color.FromArgb(209,215,232);Padding=new Padding(8,8,18,8);}
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text{get=>content;set{content=value??"";offset=0;Invalidate();}}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);offset=Math.Clamp(offset-e.Delta,0,Math.Max(0,total-Height+Padding.Vertical));Invalidate();}
    protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Down or Keys.Up or Keys.PageDown or Keys.PageUp||base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);int delta=e.KeyCode is Keys.Down?32:e.KeyCode is Keys.Up?-32:e.KeyCode is Keys.PageDown?Height-40:e.KeyCode is Keys.PageUp?40-Height:0;if(delta!=0){offset=Math.Clamp(offset+delta,0,Math.Max(0,total-Height+Padding.Vertical));Invalidate();e.Handled=true;}}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;using var format=new StringFormat{Trimming=StringTrimming.Word};int width=Math.Max(1,Width-Padding.Horizontal);var size=e.Graphics.MeasureString(content,Font,width,format);total=(int)Math.Ceiling(size.Height);offset=Math.Clamp(offset,0,Math.Max(0,total-Height+Padding.Vertical));using var brush=new SolidBrush(ForeColor);e.Graphics.DrawString(content,Font,brush,new RectangleF(Padding.Left,Padding.Top-offset,width,total+8),format);if(total>Height){float h=Math.Max(24,Height*Height/(float)total),top=offset/(float)Math.Max(1,total-Height+Padding.Vertical)*(Height-h);using var pen=new Pen(Color.FromArgb(104,93,156),3);e.Graphics.DrawLine(pen,Width-5,top+2,Width-5,top+h-2);}}
}

internal static class NavigationSticker
{
    public static Bitmap Create(string kind)
    {
        var image=new Bitmap(32,32);using var g=Graphics.FromImage(image);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color ink=kind=="favorite"?Color.FromArgb(245,172,150):kind=="discover"?Color.FromArgb(171,209,191):kind=="recent"?Color.FromArgb(172,196,242):Color.FromArgb(199,184,244);
        using var background=new SolidBrush(Color.FromArgb(45,ink));using var shape=VisualTheme.Rounded(new Rectangle(1,1,30,30),9);g.FillPath(background,shape);using var pen=new Pen(ink,1.8f){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};
        if(kind=="favorite"){using var path=new System.Drawing.Drawing2D.GraphicsPath();path.AddBezier(16,25,2,16,6,6,12,9);path.AddBezier(12,9,15,10,16,13,16,13);path.AddBezier(16,13,23,3,32,12,16,25);g.DrawPath(pen,path);}
        else if(kind=="recent"){g.DrawArc(pen,7,7,18,18,30,295);g.DrawLine(pen,16,10,16,16);g.DrawLine(pen,16,16,21,19);}
        else if(kind=="discover"){g.DrawEllipse(pen,7,7,18,18);g.DrawPolygon(pen,new[]{new Point(20,11),new Point(18,19),new Point(11,22),new Point(14,14)});}
        else if(kind=="performance"){g.DrawLine(pen,7,24,25,24);g.DrawLine(pen,9,20,9,14);g.DrawLine(pen,16,20,16,9);g.DrawLine(pen,23,20,23,5);}
        else if(kind=="mods"){g.DrawPolygon(pen,new[]{new Point(16,5),new Point(26,11),new Point(26,22),new Point(16,28),new Point(6,22),new Point(6,11)});g.DrawLine(pen,6,11,16,17);g.DrawLine(pen,26,11,16,17);g.DrawLine(pen,16,17,16,28);}
        else if(kind=="workspace"){g.DrawRectangle(pen,7,7,7,7);g.DrawRectangle(pen,18,7,7,7);g.DrawRectangle(pen,7,18,7,7);g.DrawRectangle(pen,18,18,7,7);}
        else if(kind=="network"){g.DrawEllipse(pen,7,7,18,18);g.DrawEllipse(pen,12,7,8,18);g.DrawLine(pen,7,16,25,16);}
        else if(kind=="settings"){g.DrawEllipse(pen,10,10,12,12);g.DrawEllipse(pen,14,14,4,4);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(pen,(float)(16+8*Math.Cos(a)),(float)(16+8*Math.Sin(a)),(float)(16+11*Math.Cos(a)),(float)(16+11*Math.Sin(a)));}}
        else{g.DrawRectangle(pen,7,8,7,16);g.DrawRectangle(pen,18,8,7,16);g.DrawLine(pen,9,12,12,12);g.DrawLine(pen,20,12,23,12);}
        return image;
    }
}
