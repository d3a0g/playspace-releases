using System.Drawing.Drawing2D;

namespace GameLauncherGUI;

internal static class VisualTheme
{
    public static Bitmap CreateTelegramIcon()
    {
        var bitmap = new Bitmap(28, 28);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var blue = new SolidBrush(Color.FromArgb(42, 171, 238));
        graphics.FillEllipse(blue, 0, 0, 27, 27);
        using var white = new SolidBrush(Color.White);
        graphics.FillPolygon(white, new[] { new PointF(6, 13), new PointF(22, 7), new PointF(18, 22),
            new PointF(13, 17), new PointF(10, 20), new PointF(10, 15) });
        using var fold = new Pen(Color.FromArgb(42, 171, 238), 1.4f);
        graphics.DrawLine(fold, 10, 15, 18, 10);
        return bitmap;
    }
    public static Color Accent = Color.FromArgb(109, 157, 203);
    public static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
}

internal sealed class GameMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(23, 28, 42);
    public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
    public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
    public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    public override Color MenuBorder => Color.FromArgb(57, 69, 88);
    public override Color MenuItemSelected => VisualTheme.Blend(Color.FromArgb(27, 34, 48), VisualTheme.Accent, .22f);
    public override Color MenuItemBorder => VisualTheme.Accent;
    public override Color SeparatorDark => Color.FromArgb(49, 52, 73);
    public override Color SeparatorLight => ToolStripDropDownBackground;
}

internal sealed class LibraryHero : Control
{
    [System.ComponentModel.DefaultValue(false)]
    internal bool Compact { get; set; }
    private string headline = "Время играть.";
    private string detail = "Все твои миры — в одной библиотеке";
    private Image? icon;
    private Bitmap? artwork;
    private bool hasGame;
    private float transition = 1;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    private readonly System.Diagnostics.Stopwatch clock = new();
    public event EventHandler? PlayRequested;
    private Rectangle PlayBounds => new(Width - 132, Height - 44, 108, 30);

    public LibraryHero()
    {
        DoubleBuffered = true; ResizeRedraw = true; TabStop = false;
        animation.Tick += (s, e) => {
            transition = Math.Min(1, (float)clock.Elapsed.TotalMilliseconds / 280f);
            Invalidate();
            if (transition >= 1) animation.Stop();
        };
    }

    public void SetPresentation(string title, string subtitle, Image? gameIcon, bool selected)
    {
        bool changed = title != headline || subtitle != detail;
        headline = title; detail = subtitle; icon = gameIcon; hasGame = selected; TabStop = selected&&!Compact;
        if (changed) { transition = PresentationOptions.ReduceMotion?1:0; clock.Restart(); if(!PresentationOptions.ReduceMotion)animation.Start();else Invalidate(); }
        Invalidate();
    }

    // Artwork is owned here; icon images are borrowed from the application's icon cache.
    public void SetArtwork(Bitmap? image)
    {
        var previous = artwork; artwork = image; previous?.Dispose();
        transition = PresentationOptions.ReduceMotion?1:0; clock.Restart(); if(!PresentationOptions.ReduceMotion)animation.Start(); Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Cursor = !Compact&&hasGame && PlayBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default; }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!Compact&&hasGame && e.Button == MouseButtons.Left && PlayBounds.Contains(e.Location)) PlayRequested?.Invoke(this, EventArgs.Empty);
    }
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Compact&&hasGame && e.KeyCode == Keys.Enter) { PlayRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        if(PresentationOptions.ReduceMotion){transition=1;animation.Stop();}
        if(Compact)
        {
            e.Graphics.Clear(Color.FromArgb(20,25,37));
            if(artwork!=null)
            {
                float scale=Math.Max(Width/(float)artwork.Width,Height/(float)artwork.Height);int width=(int)Math.Ceiling(artwork.Width*scale),height=(int)Math.Ceiling(artwork.Height*scale);
                using var attributes=new System.Drawing.Imaging.ImageAttributes();attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix{Matrix33=.18f*UiMotion.Ease(transition)});
                e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.DrawImage(artwork,new Rectangle((Width-width)/2,(Height-height)/2,width,height),0,0,artwork.Width,artwork.Height,GraphicsUnit.Pixel,attributes);
            }
            using var titleFont=new Font("Segoe UI",12,FontStyle.Bold);using var smallFont=new Font("Segoe UI",9);
            SmoothText.Draw(e.Graphics,headline,titleFont,new Rectangle(14,5,Math.Max(1,Width-72),26),Color.White,TextFormatFlags.EndEllipsis);
            SmoothText.Draw(e.Graphics,detail,smallFont,new Rectangle(14,32,Math.Max(1,Width-24),22),Color.FromArgb(163,177,197),TextFormatFlags.EndEllipsis);
            if(icon!=null)e.Graphics.DrawImage(icon,new Rectangle(Width-46,6,32,32));return;
        }
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = VisualTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 20);
        using var gradient = new LinearGradientBrush(ClientRectangle, VisualTheme.Blend(Color.FromArgb(23,28,42),VisualTheme.Accent,.05f), Color.FromArgb(20,28,42), 15);
        g.FillPath(gradient, path);
        var state = g.Save();
        g.SetClip(path);
        if (artwork != null)
        {
            float scale = Math.Max((float)Width / artwork.Width, (float)Height / artwork.Height);
            float w = artwork.Width * scale, h = artwork.Height * scale;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var attributes=new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix{Matrix33=UiMotion.Ease(transition)});
            g.DrawImage(artwork,new Rectangle((int)((Width-w)/2),(int)((Height-h)/2),(int)Math.Ceiling(w),(int)Math.Ceiling(h)),0,0,artwork.Width,artwork.Height,GraphicsUnit.Pixel,attributes);
            using var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(248, 16, 17, 33), Color.FromArgb(80, 16, 17, 33), 0f);
            g.FillRectangle(shade, ClientRectangle);
        }
        g.Restore(state);
        using var border = new Pen(Color.FromArgb(48, 57, 72));
        g.DrawPath(border, path);
        using var eyebrow = new Font("Segoe UI", 8, FontStyle.Bold);
        using var subtitle = new Font("Segoe UI", 9);
        int textWidth = Math.Max(1, Width - (hasGame ? 176 : 48));
        float headingSize = 25;
        while (headingSize > 15)
        {
            using var measure = new Font("Segoe UI", headingSize, FontStyle.Bold);
            if (TextRenderer.MeasureText(headline, measure).Width <= textWidth) break;
            headingSize -= 1;
        }
        using var heading = new Font("Segoe UI", headingSize, FontStyle.Bold);
        SmoothText.Draw(g, hasGame ? "УСТАНОВЛЕННАЯ ИГРА" : "ВАША БИБЛИОТЕКА", eyebrow,
            new Rectangle(24, 16, textWidth, 18), VisualTheme.Blend(Color.White,VisualTheme.Accent,.65f), TextFormatFlags.EndEllipsis);
        int titleOffset=(int)Math.Round(5*(1-UiMotion.Ease(transition)));
        SmoothText.Draw(g, headline, heading, new Rectangle(20, 34+titleOffset, textWidth, 46), Color.FromArgb(246, 244, 255),
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        SmoothText.Draw(g, detail, subtitle, new Rectangle(24, 85, textWidth, 22), Color.FromArgb(188, 193, 214),
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (hasGame)
        {
            if (icon != null) g.DrawImage(icon, new Rectangle(Width - 85, 16, 42, 42));
            using var buttonPath = VisualTheme.Rounded(PlayBounds, 9);
            using var buttonFill = new SolidBrush(VisualTheme.Accent);
            g.FillPath(buttonFill, buttonPath);
            SmoothText.Draw(g, "▶  Играть", subtitle, PlayBounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, PlayBounds, Color.White, Color.Transparent);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { animation.Dispose(); artwork?.Dispose(); }
        base.Dispose(disposing);
    }
}
public sealed class ModernButton : Button
{
    [System.ComponentModel.DefaultValue(false)]
    public bool IconOnly { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsNavigation {get;set;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsSectionNavigation {get;set;}
    private bool active;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsActive { get => active; set { if(active==value)return;active = value;animation.Start();Invalidate(); } }
    private readonly Color baseColor;
    private readonly bool primary;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    private bool hovered;
    private bool pressed;
    private float hover;
    private float selection;
    private float ripple = 1;
    private Point rippleOrigin;

    public ModernButton(string text, Color color)
    {
        Text = text;
        baseColor = color;
        primary=color.ToArgb()==VisualTheme.Accent.ToArgb()||(color.R>75&&color.B>170&&color.B>color.R*1.3&&color.G<160);
        BackColor = color;
        ForeColor = Color.FromArgb(242, 242, 252);
        Font = new Font("Segoe UI", 10, FontStyle.Regular);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        ResizeRedraw = true;
        animation.Tick += (s, e) =>
        {
            float target = hovered && Enabled ? 1 : 0;
            hover += (target - hover) * .32f;
            float selected=active?1:0;selection+=(selected-selection)*.32f;
            ripple = Math.Min(1, ripple + .055f);
            if (Math.Abs(hover - target) < .01f && Math.Abs(selection-selected)<.01f && ripple >= 1) { hover = target;selection=selected;animation.Stop(); }
            Invalidate();
        };
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; animation.Start(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; animation.Start(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pressed = true; rippleOrigin = e.Location; ripple = 0; animation.Start();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (animation != null) animation.Start(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Color.FromArgb(14, 17, 27));
        if(PresentationOptions.ReduceMotion){animation.Stop();hover=hovered&&Enabled?1:0;selection=active?1:0;ripple=1;}
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, pressed ? 2 : 1, Width - 3, Height - 3);
        using var path = VisualTheme.Rounded(bounds, 7);
        Color neutral=baseColor.R>70&&baseColor.R>baseColor.B*1.25?baseColor:Color.FromArgb(31,36,53);
        Color basis=primary?VisualTheme.Accent:VisualTheme.Blend(neutral,VisualTheme.Accent,selection*.28f);
        Color fill = Enabled ? VisualTheme.Blend(basis, VisualTheme.Accent, hover * .16f) : Color.FromArgb(34, 37, 51);
        using var gradient = new SolidBrush(fill);
        if(IsSectionNavigation||!IsNavigation||selection>.01f||hover>.01f)g.FillPath(gradient, path);
        using var border = new Pen(VisualTheme.Blend(Color.FromArgb(49,56,76),VisualTheme.Accent,Math.Max(selection*.65f,hover*.55f)));
        if(IsSectionNavigation||!IsNavigation)g.DrawPath(border, path);
        if((IsNavigation||IsSectionNavigation)&&selection>.01f){using var marker=new Pen(Color.FromArgb((int)(255*selection),VisualTheme.Accent),2);g.DrawLine(marker,5,12,5,Height-12);}
        if (ripple < 1)
        {
            var state = g.Save();
            g.SetClip(path);
            float size = Math.Max(Width, Height) * ripple * 2;
            using var brush = new SolidBrush(Color.FromArgb((int)(28 * (1 - ripple)), 255, 255, 255));
            g.FillEllipse(brush, rippleOrigin.X - size / 2, rippleOrigin.Y - size / 2, size, size);
            g.Restore(state);
        }
        var textBounds = bounds;
        if(IsSectionNavigation){textBounds.X+=14;textBounds.Width=Math.Max(1,textBounds.Width-24);}
        if (Image != null)
        {
            int size = Math.Min(28, Math.Max(1, Height - 16));
            g.DrawImage(Image, new Rectangle(IconOnly?(Width-size)/2:14, (Height - size) / 2, size, size));
            textBounds.X += 43;
            textBounds.Width = Math.Max(1, textBounds.Width - 51);
        }
        if(!IconOnly)SmoothText.Draw(g, Text, Font, textBounds, Enabled ? ForeColor : Color.FromArgb(122, 129, 154),
            (IsNavigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(VisualTheme.Accent) { DashStyle = DashStyle.Dot };
            var inset = bounds; inset.Inflate(-4, -4);
            using var outline = VisualTheme.Rounded(inset, 9);
            g.DrawPath(focus, outline);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}

public sealed class CustomListItem
{
    public Image? PosterImage { get; set; }
    public string Text { get; set; }
    public bool Checked { get; set; }
    public string Subtitle { get; set; }
    public Image? IconImage { get; set; }
    public bool IsPinned { get; set; }
    public CustomListItem(string text, bool isChecked, string subtitle = "Готова к запуску")
    { Text = text; Checked = isChecked; Subtitle = subtitle; }
}

public sealed class CustomCheckListBox : Panel
{
    internal int ViewportOffset=>scrollOffset;
    internal void RestoreViewport(int selected,int offset)
    {
        SelectedIndex=selected>=0&&selected<Items.Count?selected:-1;scrollOffset=Math.Clamp(offset,0,MaxOffset);SelectionChanged?.Invoke(this,EventArgs.Empty);Invalidate();
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new GameListAccessibility(this);
    private sealed class GameListAccessibility(CustomCheckListBox owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.List;
        public override string? Name { get => owner.AccessibleName ?? "Список игр"; set => owner.AccessibleName=value; }
        public override int GetChildCount() => owner.Items.Count;
        public override AccessibleObject? GetChild(int index) => index>=0 && index<owner.Items.Count ? new GameAccessibility(owner,index,this) : null;
        public override AccessibleObject? GetSelected() => GetChild(owner.SelectedIndex);
    }
    private sealed class GameAccessibility(CustomCheckListBox owner,int index,AccessibleObject parent) : AccessibleObject
    {
        public override AccessibleRole Role => AccessibleRole.ListItem;
        public override AccessibleObject? Parent => parent;
        public override string? Name { get => index<owner.Items.Count ? owner.Items[index].Text : ""; set { } }
        public override string? Description => index<owner.Items.Count ? owner.Items[index].Subtitle + (owner.Items[index].IsPinned?" · Закреплено":"") : "";
        public override string? DefaultAction => "Выбрать";
        public override Rectangle Bounds => index<owner.Items.Count ? owner.RectangleToScreen(owner.CardBounds(index)) : Rectangle.Empty;
        public override AccessibleStates State => AccessibleStates.Selectable | AccessibleStates.Focusable | (owner.SelectedIndex==index?AccessibleStates.Selected:0) | (owner.Focused&&owner.SelectedIndex==index?AccessibleStates.Focused:0);
        public override void DoDefaultAction() { owner.Focus(); owner.SelectItem(index); }
        public override void Select(AccessibleSelection flags) => DoDefaultAction();
    }
    [System.ComponentModel.DefaultValue(false)]
    public bool PosterView { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool LandscapePosters { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool CompactRows { get; set; }
    [System.ComponentModel.DefaultValue(0)]
    public int PosterMinimumWidth { get; set; }
    internal void Navigate(Keys key)=>OnKeyDown(new KeyEventArgs(key));
    [System.ComponentModel.DefaultValue(true)]
    public bool ShowCheckBoxes { get; set; } = true;
    private int Columns => PosterView ? Math.Max(1, (Width - 22) / (PosterMinimumWidth>0?PosterMinimumWidth:PresentationOptions.PosterWidth)) : 1;
    private int RowHeight => PosterView ? (int)(((Width - 22) / Columns - 12) * (LandscapePosters?215f/460:1.5f)) + 71 : ItemHeight;
    private int ContentHeight => ((Items.Count + Columns - 1) / Columns) * RowHeight + Padding.Vertical;
    private Rectangle CardBounds(int index) => PosterView
        ? new Rectangle(4 + index % Columns * ((Width - 22) / Columns), Padding.Top + index / Columns * RowHeight - scrollOffset, (Width - 22) / Columns - 10, RowHeight - 10)
        : new Rectangle(4, Padding.Top + index * ItemHeight - scrollOffset, Width - 23, ItemHeight - 8);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public List<CustomListItem> Items { get; set; } = new();
    public int SelectedIndex { get; private set; } = -1;
    public event EventHandler? ItemDoubleClick;
    public event EventHandler? ItemClick;
    public event EventHandler? SelectionChanged;
    public event Action<int>? PinToggleRequested;
    public event Action<int>? ProfileRequested;
    public event Action<Point>? ContextMenuRequested;
    [System.ComponentModel.DefaultValue(false)]
    public bool ShowPinButtons { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool ShowProfileButtons { get; set; }
    [System.ComponentModel.DefaultValue("Здесь начинается твоя коллекция")]
    public string EmptyTitle { get; set; } = "Здесь начинается твоя коллекция";
    [System.ComponentModel.DefaultValue("Нажми «Найти игры» или добавь игру вручную")]
    public string EmptyHint { get; set; } = "Нажми «Найти игры» или добавь игру вручную";
    private readonly ToolTip pinTip = new() { InitialDelay = 350, ReshowDelay = 100 };
    private int pinHoverIndex = -1;
    private int profileHoverIndex = -1;
    private int ItemHeight => CompactRows?44:78;
    private int scrollOffset;
    private int hoveredIndex = -1;
    private bool dragging;
    private int dragY;
    private int dragOffset;
    private float hover;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    private int MaxOffset => Math.Max(0, ContentHeight - ClientSize.Height);
    private Rectangle Track => new(Width - 9, 8, 4, Math.Max(1, Height - 16));
    private Rectangle Thumb
    {
        get
        {
            int height = Math.Min(Track.Height, Math.Max(32, (int)((double)ClientSize.Height / Math.Max(1, ContentHeight) * Track.Height)));
            return new Rectangle(Track.X, Track.Y + (MaxOffset == 0 ? 0 : (int)((double)scrollOffset / MaxOffset * (Track.Height - height))), Track.Width, height);
        }
    }

    public CustomCheckListBox()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoScroll = false;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(14, 17, 27);
        Padding = new Padding(4, 8, 4, 8);
        animation.Tick += (s, e) => { hover = Math.Min(1, hover + .11f); Invalidate(); if (hover >= 1) animation.Stop(); };
    }

    public void ResetView() { SelectedIndex = -1; hoveredIndex = -1; pinHoverIndex = -1; pinTip.SetToolTip(this, null); scrollOffset = 0; Invalidate(); }
    public void SelectItem(int index)
    {
        SelectedIndex = index >= 0 && index < Items.Count ? index : -1;
        if (SelectedIndex >= 0)
        {
            int top = Padding.Top + SelectedIndex / Columns * RowHeight;
            if (top < scrollOffset) scrollOffset = top;
            if (top + RowHeight > scrollOffset + Height) scrollOffset = top + RowHeight - Height;
            scrollOffset = Math.Clamp(scrollOffset, 0, MaxOffset);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if(IsHandleCreated&&SelectedIndex>=0)AccessibilityNotifyClients(AccessibleEvents.Selection,SelectedIndex);
        Invalidate();
    }
    private Rectangle PinBounds(int index) => PosterView ? new(CardBounds(index).Right - 35,CardBounds(index).Top+5,30,30) : new(Width - 65, Padding.Top + index * ItemHeight - scrollOffset + (CompactRows?3:18), 32, 34);
    private Rectangle ProfileBounds(int index) => PosterView ? new(CardBounds(index).Right - 70,CardBounds(index).Top+5,30,30) : new(Width - 104, Padding.Top + index * ItemHeight - scrollOffset + (CompactRows?3:18), 32, 34);
    public List<int> GetCheckedIndices() => Enumerable.Range(0, Items.Count).Where(i => Items[i].Checked).ToList();
    private void PaintPosters(Graphics g)
    {
        scrollOffset=Math.Clamp(scrollOffset,0,MaxOffset);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        using var title=new Font("Segoe UI",10,FontStyle.Bold);using var small=new Font("Segoe UI",8);
        for(int i=0;i<Items.Count;i++) {
            var r=CardBounds(i);if(r.Bottom<0||r.Top>Height)continue;
            float highlight=i==hoveredIndex?UiMotion.Ease(hover):0;
            using var shape=VisualTheme.Rounded(r,3);
            using var fill=new SolidBrush(VisualTheme.Blend(Color.FromArgb(20,25,35),VisualTheme.Accent,(i==SelectedIndex?.12f:0)+highlight*.04f));g.FillPath(fill,shape);
            var state=g.Save();g.SetClip(shape,CombineMode.Intersect);
            var art=new Rectangle(r.X+1,r.Y+1,r.Width-2,r.Height-61);
            if(Items[i].PosterImage is Image image)
            {
                var imageState=g.Save();g.SetClip(art,CombineMode.Intersect);
                float scale=Math.Max(art.Width/(float)image.Width,art.Height/(float)image.Height)*(1+(PresentationOptions.ReduceMotion?0:highlight*.025f));
                float width=image.Width*scale,height=image.Height*scale;
                g.DrawImage(image,art.X+(art.Width-width)/2,art.Y+(art.Height-height)/2,width,height);g.Restore(imageState);
            }
            else if(Items[i].IconImage is Image icon)g.DrawImage(icon,new Rectangle(art.X+(art.Width-56)/2,art.Y+Math.Max(4,(art.Height-56)/2),56,56));
            SmoothText.Draw(g,Items[i].Text,title,new Rectangle(r.X+9,r.Bottom-57,r.Width-18,26),Color.White,TextFormatFlags.EndEllipsis);
            SmoothText.Draw(g,Items[i].Subtitle,small,new Rectangle(r.X+9,r.Bottom-29,r.Width-18,22),Color.FromArgb(160,171,198),TextFormatFlags.EndEllipsis);
            if(ShowPinButtons)SmoothText.Draw(g,Items[i].IsPinned?"★":"☆",title,PinBounds(i),Color.White,Color.FromArgb(36,32,52),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(ShowProfileButtons)SmoothText.Draw(g,"⚙",title,ProfileBounds(i),Color.White,Color.FromArgb(36,32,52),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            using var check=new SolidBrush(Items[i].Checked?VisualTheme.Accent:Color.FromArgb(65,68,90));if(ShowCheckBoxes)g.FillRectangle(check,r.X+8,r.Y+9,17,17);
            g.Restore(state);
            if(i==SelectedIndex||highlight>.01f){using var border=new Pen(i==SelectedIndex?VisualTheme.Accent:VisualTheme.Blend(Color.FromArgb(46,55,70),VisualTheme.Accent,highlight*.35f),i==SelectedIndex?2:1);g.DrawPath(border,shape);}
        }
    }
    private void PaintCompactRow(Graphics g,int index,Rectangle row,bool selected,float highlight)
    {
        using var fill=new SolidBrush(selected?VisualTheme.Blend(Color.FromArgb(24,32,47),VisualTheme.Accent,.22f):VisualTheme.Blend(BackColor,Color.FromArgb(31,40,56),highlight));g.FillRectangle(fill,row);
        var item=Items[index];int y=row.Y;
        if(ShowCheckBoxes){var box=new Rectangle(23,y+12,15,15);using var pen=new Pen(item.Checked?VisualTheme.Accent:Color.FromArgb(65,80,99));g.DrawRectangle(pen,box);if(item.Checked){using var ink=new SolidBrush(VisualTheme.Accent);g.FillRectangle(ink,box);}}
        if(item.IconImage!=null)g.DrawImage(item.IconImage,new Rectangle(49,y+6,24,24));
        int nameWidth=Math.Max(80,(int)(Width*.43)-80);using var title=new Font("Segoe UI",9.5f);using var detail=new Font("Segoe UI",9);
        SmoothText.Draw(g,item.Text,title,new Rectangle(84,y+4,nameWidth,30),Color.FromArgb(229,235,244),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        int start=84+nameWidth+10;SmoothText.Draw(g,item.Subtitle,detail,new Rectangle(start,y+4,Math.Max(1,Width-start-76),30),Color.FromArgb(152,171,193),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        if(ShowPinButtons)SmoothText.Draw(g,item.IsPinned?"★":"☆",detail,PinBounds(index),item.IsPinned?VisualTheme.Accent:Color.FromArgb(133,151,176),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
    private int HitTest(Point point)
    {
        if (PosterView) {
            int row = (point.Y + scrollOffset - Padding.Top) / RowHeight;
            int col = Math.Clamp((point.X - 4) / Math.Max(1,(Width-22)/Columns),0,Columns-1);
            int hit = row * Columns + col;
            return hit >= 0 && hit < Items.Count && CardBounds(hit).Contains(point) ? hit : -1;
        }
        int y = point.Y + scrollOffset - Padding.Top;
        int index = y / ItemHeight;
        return point.X >= 4 && point.X < Width - 18 && y >= 0 && y % ItemHeight < ItemHeight - 8 && index < Items.Count ? index : -1;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            scrollOffset = Math.Clamp(dragOffset + (int)((double)(e.Y - dragY) * MaxOffset / Math.Max(1, Track.Height - Thumb.Height)), 0, MaxOffset);
            Invalidate(); return;
        }
        int index = HitTest(e.Location);
        if (index != hoveredIndex) { hoveredIndex = index; hover = 0; animation.Start(); }
        int pinIndex = ShowPinButtons && index >= 0 && PinBounds(index).Contains(e.Location) ? index : -1;
        int profileIndex = ShowProfileButtons && index >= 0 && ProfileBounds(index).Contains(e.Location) ? index : -1;
        if (pinIndex != pinHoverIndex || profileIndex != profileHoverIndex)
        {
            pinHoverIndex = pinIndex;
            profileHoverIndex = profileIndex;
            pinTip.SetToolTip(this, profileIndex >= 0 ? "Профиль производительности" :
                pinIndex >= 0 ? (Items[pinIndex].IsPinned ? "Открепить игру" : "Закрепить игру") + " · P" : null);
            Invalidate();
        }
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoveredIndex = -1; pinHoverIndex = -1; pinTip.SetToolTip(this, null); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        if (e.X >= Width - 16 && MaxOffset > 0)
        {
            if (!Thumb.Contains(e.Location))
                scrollOffset = Math.Clamp((int)((double)(e.Y - Track.Y - Thumb.Height / 2) / Math.Max(1, Track.Height - Thumb.Height) * MaxOffset), 0, MaxOffset);
            dragging = true; dragY = e.Y; dragOffset = scrollOffset; Capture = true; Invalidate(); return;
        }
        int index = HitTest(e.Location);
        if (index < 0) return;
        bool pinClicked = ShowPinButtons && PinBounds(index).Contains(e.Location);
        bool profileClicked = ShowProfileButtons && ProfileBounds(index).Contains(e.Location);
        SelectItem(index);
        if (profileClicked)
        {
            if (e.Clicks == 1) ProfileRequested?.Invoke(index);
            return;
        }
        if (pinClicked)
        {
            if (e.Clicks == 1) PinToggleRequested?.Invoke(index);
            return;
        }
        if (ShowCheckBoxes && (PosterView ? new Rectangle(CardBounds(index).X+8,CardBounds(index).Y+9,17,17).Contains(e.Location) : e.X >= 20 && e.X <= 44)) Items[index].Checked = !Items[index].Checked;
        Invalidate();
        if (e.Clicks == 1 && !(ShowCheckBoxes && (PosterView ? new Rectangle(CardBounds(index).X+8,CardBounds(index).Y+9,17,17).Contains(e.Location) : e.X >= 20 && e.X <= 44)))
            ItemClick?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); dragging = false; Capture = false;
        if (e.Button != MouseButtons.Right) return;
        int index = HitTest(e.Location);
        if (index < 0) return;
        SelectItem(index);
        ContextMenuRequested?.Invoke(e.Location);
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        int index = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && e.X > 44 && index >= 0 &&
            !(ShowPinButtons && PinBounds(index).Contains(e.Location)) &&
            !(ShowProfileButtons && ProfileBounds(index).Contains(e.Location))) ItemDoubleClick?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        scrollOffset = Math.Clamp(scrollOffset - e.Delta, 0, MaxOffset);
        hoveredIndex = -1; Invalidate();
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter or Keys.Space or Keys.P || base.IsInputKey(keyData);
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData == Keys.Apps || keyData == (Keys.Shift | Keys.F10)) && SelectedIndex >= 0)
        {
            ContextMenuRequested?.Invoke(new Point(CardBounds(SelectedIndex).X+30,CardBounds(SelectedIndex).Y+42));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Items.Count == 0) return;
        if(e.KeyCode is Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
        {
            int step=Math.Max(1,Height/Math.Max(1,RowHeight))*Columns;
            SelectItem(e.KeyCode==Keys.Home?0:e.KeyCode==Keys.End?Items.Count-1:Math.Clamp(SelectedIndex+(e.KeyCode==Keys.PageUp?-step:step),0,Items.Count-1));
            e.Handled=true;e.SuppressKeyPress=true;return;
        }
        if(e.Control&&e.KeyCode==Keys.D&&SelectedIndex>=0&&ShowPinButtons){PinToggleRequested?.Invoke(SelectedIndex);e.Handled=true;e.SuppressKeyPress=true;return;}
        if(e.Alt&&e.KeyCode==Keys.Enter&&SelectedIndex>=0&&ProfileRequested!=null){ProfileRequested.Invoke(SelectedIndex);e.Handled=true;e.SuppressKeyPress=true;return;}
        if (SelectedIndex >= 0 && (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift)))
        {
            var point = new Point(CardBounds(SelectedIndex).X+30,CardBounds(SelectedIndex).Y+42);
            ContextMenuRequested?.Invoke(point);
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.KeyCode is Keys.Up or Keys.Down || PosterView && e.KeyCode is Keys.Left or Keys.Right)
        {
            SelectedIndex = Math.Clamp(SelectedIndex < 0 ? 0 : SelectedIndex + (e.KeyCode == Keys.Down ? Columns : e.KeyCode == Keys.Up ? -Columns : e.KeyCode == Keys.Right ? 1 : -1), 0, Items.Count - 1);
            int top = Padding.Top + SelectedIndex / Columns * RowHeight;
            if (top < scrollOffset) scrollOffset = top;
            if (top + RowHeight > scrollOffset + Height) scrollOffset = top + RowHeight - Height;
            scrollOffset = Math.Clamp(scrollOffset, 0, MaxOffset);
        }
        else if (SelectedIndex >= 0 && e.KeyCode == Keys.Space) Items[SelectedIndex].Checked = !Items[SelectedIndex].Checked;
        else if (SelectedIndex >= 0 && ShowPinButtons && e.KeyCode == Keys.P && e.Modifiers == Keys.None) PinToggleRequested?.Invoke(SelectedIndex);
        else if (SelectedIndex >= 0 && e.KeyCode == Keys.Enter) ItemDoubleClick?.Invoke(this, EventArgs.Empty);
        else return;
        e.Handled = true; e.SuppressKeyPress = true; Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if(IsHandleCreated&&SelectedIndex>=0)AccessibilityNotifyClients(AccessibleEvents.Selection,SelectedIndex);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(PresentationOptions.ReduceMotion){animation.Stop();hover=1;}
        if (PosterView && Items.Count > 0) { PaintPosters(e.Graphics); return; }
        if (Width < 70 || Height < 2) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        scrollOffset = Math.Clamp(scrollOffset, 0, MaxOffset);
        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var subFont = new Font("Segoe UI", 9);
        using var iconFont = new Font("Segoe UI", 14, FontStyle.Bold);
        if (Items.Count == 0)
        {
            SmoothText.Draw(g, EmptyTitle, titleFont, ClientRectangle, Color.FromArgb(194, 190, 220),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var hint = ClientRectangle; hint.Y += 32;
            SmoothText.Draw(g, EmptyHint, subFont, hint, Color.FromArgb(125, 136, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        for (int i = Math.Max(0, scrollOffset / ItemHeight - 1); i < Items.Count; i++)
        {
            int y = Padding.Top + i * ItemHeight - scrollOffset;
            if (y > Height) break;
            var rect = new Rectangle(4, y, Width - 23, ItemHeight - 8);
            bool selected = i == SelectedIndex;
            float highlight = i == hoveredIndex ? UiMotion.Ease(hover) : 0;
            if(CompactRows){PaintCompactRow(g,i,rect,selected,highlight);continue;}
            using var card = VisualTheme.Rounded(rect, 13);
            Color fill = selected ? VisualTheme.Blend(Color.FromArgb(23,28,42),VisualTheme.Accent,.10f) : VisualTheme.Blend(Color.FromArgb(18, 23, 33), Color.FromArgb(31, 39, 52), highlight);
            using var background = new SolidBrush(fill);
            g.FillPath(background, card);
            using var border = new Pen(selected ? VisualTheme.Accent : VisualTheme.Blend(Color.FromArgb(37, 43, 61), Color.FromArgb(99, 88, 143), highlight));
            if(selected||highlight>.01f)g.DrawPath(border, card);
            if (selected)
            {
                using var line = new Pen(VisualTheme.Accent, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(line, 5, y + 23, 5, y + 47);
            }
            if(ShowCheckBoxes)
            {
            var check = new Rectangle(23, y + 26, 18, 18);
            using var checkPath = VisualTheme.Rounded(check, 5);
            using var checkBorder = new Pen(Items[i].Checked ? VisualTheme.Accent : Color.FromArgb(86, 94, 120));
            g.DrawPath(checkBorder, checkPath);
            if (Items[i].Checked)
            {
                using var checkFill = new SolidBrush(VisualTheme.Accent);
                g.FillPath(checkFill, checkPath);
                using var tick = new Pen(Color.White, 2);
                g.DrawLines(tick, new[] { new Point(27, y + 35), new Point(31, y + 39), new Point(38, y + 31) });
            }
            }
            int hash = Items[i].Text.Aggregate(0, (h, c) => (h + c) % 1000);
            Color tint = new[] { Color.FromArgb(127, 104, 219), Color.FromArgb(53, 151, 158), Color.FromArgb(182, 112, 136), Color.FromArgb(74, 121, 196) }[hash % 4];
            int inset=ShowCheckBoxes?0:35;
            var iconRect = new Rectangle(58-inset, y + 12, 46, 46);
            using var iconPath = VisualTheme.Rounded(iconRect, 12);
            using var iconFill = new LinearGradientBrush(iconRect, tint, VisualTheme.Blend(tint, fill, .65f), 45);
            g.FillPath(iconFill, iconPath);
            if (Items[i].IconImage is Image image)
            {
                var iconState = g.Save();
                g.SetClip(iconPath, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Min(38f / image.Width, 38f / image.Height);
                float width = image.Width * scale, height = image.Height * scale;
                g.DrawImage(image, iconRect.X + (iconRect.Width - width) / 2, iconRect.Y + (iconRect.Height - height) / 2, width, height);
                g.Restore(iconState);
            }
            else
            {
                string initial = string.IsNullOrWhiteSpace(Items[i].Text) ? "◈" : Items[i].Text.Trim()[..1].ToUpperInvariant();
                SmoothText.Draw(g, initial, iconFont, iconRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            int textWidth = Math.Max(1, Width - (ShowProfileButtons ? 237 : 197)+inset);
            SmoothText.Draw(g, Items[i].Text, titleFont, new Rectangle(119-inset, y + 12, textWidth, 26), Color.FromArgb(236, 236, 248),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            SmoothText.Draw(g, Items[i].Subtitle, subFont, new Rectangle(120-inset, y + 39, textWidth - 1, 20), Color.FromArgb(139, 150, 179),
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (ShowProfileButtons)
            {
                var profileRect = ProfileBounds(i);
                using var profilePath = VisualTheme.Rounded(profileRect, 9);
                using var profileBackground = new SolidBrush(i == profileHoverIndex ? Color.FromArgb(47, 44, 70) : Color.FromArgb(29, 33, 49));
                g.FillPath(profileBackground, profilePath);
                SmoothText.Draw(g, "⚙", iconFont, profileRect, Color.FromArgb(155, 163, 195),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (ShowPinButtons)
            {
                var pinRect = PinBounds(i);
                using var pinPath = VisualTheme.Rounded(pinRect, 9);
                using var pinBackground = new SolidBrush(Items[i].IsPinned ? Color.FromArgb(63, 49, 101) :
                    i == pinHoverIndex ? Color.FromArgb(47, 44, 70) : Color.FromArgb(29, 33, 49));
                g.FillPath(pinBackground, pinPath);
                SmoothText.Draw(g, Items[i].IsPinned ? "★" : "☆", iconFont, pinRect,
                    Items[i].IsPinned ? Color.FromArgb(190, 172, 255) : Color.FromArgb(139, 144, 174),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else SmoothText.Draw(g, "›", iconFont, new Rectangle(Width - 53, y + 22, 24, 30), selected ? VisualTheme.Accent : Color.FromArgb(90, 100, 130));
        }
        if (MaxOffset > 0)
        {
            using var track = new SolidBrush(Color.FromArgb(28, 33, 48));
            g.FillRectangle(track, Track);
            using var thumb = new SolidBrush(dragging ? VisualTheme.Accent : Color.FromArgb(82, 83, 115));
            using var thumbPath = VisualTheme.Rounded(Thumb, 2);
            g.FillPath(thumb, thumbPath);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); pinTip.Dispose(); } base.Dispose(disposing); }
}
