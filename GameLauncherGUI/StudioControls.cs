using System.Drawing.Drawing2D;

namespace GameLauncherGUI;

internal class StudioDialog : Form
{
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if(keyData==Keys.Escape){Close();return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void OnShown(EventArgs e){UiTypography.Apply(this);PresentationOptions.Apply(this);base.OnShown(e);}
    protected Panel ContentHost { get; } = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 17, 27) };
    private readonly Label caption;
    public StudioDialog()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(14, 17, 27); ForeColor = Color.FromArgb(236, 236, 248);
        Font = new Font("Segoe UI", 10); DoubleBuffered = true; Padding = new Padding(1);
        Size = new Size(1080, 800); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var bar = new Panel { Dock = DockStyle.Top, Height = 48, Width = ClientSize.Width };
        MouseEventHandler drag = (s, e) => { if (e.Button == MouseButtons.Left) { Form1.ReleaseCapture(); Form1.SendMessage(Handle, 0xA1, 2, 0); } };
        bar.MouseDown += drag;
        caption = new Label { Text = "PLAYSPACE", AutoSize = true, Location = new Point(24, 15),
            Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(171, 159, 212) };
        caption.MouseDown += drag;
        var close = new ModernButton("×", Color.FromArgb(27, 29, 44)) { Size = new Size(32, 30),
            Location = new Point(bar.Width - 48, 8), Anchor = AnchorStyles.Top | AnchorStyles.Right, AccessibleName = "Закрыть" };
        close.Click += (s, e) => Close(); bar.Controls.Add(caption); bar.Controls.Add(close);
        Controls.Add(ContentHost); Controls.Add(bar);
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (caption != null) caption.Text = Text; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var pen = new Pen(Color.FromArgb(57, 52, 82));
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84) return;
        long value = m.LParam.ToInt64();
        Point p = PointToClient(new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff)));
        bool left = p.X < 7, right = p.X >= ClientSize.Width - 7, top = p.Y < 7, bottom = p.Y >= ClientSize.Height - 7;
        int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
        if (hit != 0) m.Result = (IntPtr)hit;
    }
}

internal sealed class RoundedPanel : Panel
{
    public RoundedPanel() { DoubleBuffered = true; ResizeRedraw = true; Padding = new Padding(18); BackColor = Color.FromArgb(14, 17, 27); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = VisualTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 16);
        using var fill = new SolidBrush(Color.FromArgb(23, 28, 42));
        using var border = new Pen(Color.FromArgb(43, 46, 68));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
}

internal sealed class StudioHeading : Control
{
    private readonly string title, subtitle, tag;
    public StudioHeading(string title, string subtitle, string tag) { this.title = title; this.subtitle = subtitle; this.tag = tag; DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var small = new Font("Segoe UI", 8, FontStyle.Bold);
        using var heading = new Font("Segoe UI", 23, FontStyle.Bold);
        SmoothText.Draw(e.Graphics, tag, small, new Point(1, 0), Color.FromArgb(164, 143, 248));
        SmoothText.Draw(e.Graphics, title, heading, new Rectangle(-3, 16, Width, 42), Color.FromArgb(242, 240, 253), TextFormatFlags.EndEllipsis);
        SmoothText.Draw(e.Graphics, subtitle, Font, new Rectangle(1, 62, Width - 2, 25), Color.FromArgb(147, 159, 188), TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ModCardList : ListBox
{
    private int hover = -1;
    public ModCardList()
    {
        DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 88; IntegralHeight = false; BorderStyle = BorderStyle.None;
        BackColor = Color.FromArgb(14, 17, 27); ForeColor = Color.White; HorizontalScrollbar = false;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); int index = IndexFromPoint(e.Location); if (hover != index) { hover = index; Invalidate(); } Cursor = index >= 0 ? Cursors.Hand : Cursors.Default; }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || e.Bounds.Width < 10) return;
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var rect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 4, e.Bounds.Width - 6, e.Bounds.Height - 8);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = VisualTheme.Rounded(rect, 13);
        using var fill = new SolidBrush(selected ? Color.FromArgb(43, 35, 70) : hover == e.Index ? Color.FromArgb(31, 36, 53) : Color.FromArgb(23, 28, 42));
        using var border = new Pen(selected ? VisualTheme.Accent : Color.FromArgb(41, 47, 66));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        var item = Items[e.Index];
        string title = item is ModPackage mod ? mod.Name : item is InstalledMod installed ? installed.Id : item.ToString() ?? "";
        string meta = item is ModPackage package ? package.Owner + "  ·  v" + package.Latest?.Version : "Установлено в этой игре";
        var icon = new Rectangle(rect.X + 14, rect.Y + 15, 46, 46);
        using var iconPath = VisualTheme.Rounded(icon, 12);
        using var gradient = new LinearGradientBrush(icon, Color.FromArgb(105, 84, 191), Color.FromArgb(39, 66, 96), 45f);
        e.Graphics.FillPath(gradient, iconPath);
        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var metaFont = new Font("Segoe UI", 9);
        SmoothText.Draw(e.Graphics, title.Length > 0 ? title[..1].ToUpperInvariant() : "◇", titleFont, icon, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        SmoothText.Draw(e.Graphics, title.Replace('_', ' '), titleFont, new Rectangle(rect.X + 74, rect.Y + 16, rect.Width - 94, 26), Color.FromArgb(236, 234, 249),
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        SmoothText.Draw(e.Graphics, meta, metaFont, new Rectangle(rect.X + 75, rect.Y + 46, rect.Width - 94, 20), Color.FromArgb(149, 159, 190), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected && Focused) e.DrawFocusRectangle();
    }
}

internal sealed class ModernToggle : CheckBox
{
    public ModernToggle() { AutoSize=false; DoubleBuffered=true; ResizeRedraw=true; Cursor=Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor??Color.FromArgb(23,28,42));
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        int y=Math.Max(0,(Height-22)/2);
        using var track=VisualTheme.Rounded(new Rectangle(0,y,42,22),11);
        using var fill=new SolidBrush(Enabled&&Checked?Color.FromArgb(115,94,244):Color.FromArgb(53,59,80));
        e.Graphics.FillPath(fill,track);
        using var knob=new SolidBrush(Enabled?Color.FromArgb(242,239,255):Color.FromArgb(124,130,152));
        e.Graphics.FillEllipse(knob,Checked?23:3,y+3,16,16);
        SmoothText.Draw(e.Graphics,Text,Font,new Rectangle(51,0,Math.Max(1,Width-51),Height),Enabled?ForeColor:Color.FromArgb(112,121,145),
            TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle);
    }
}
