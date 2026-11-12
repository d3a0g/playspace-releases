using System.Drawing.Drawing2D;

namespace GameLauncherGUI;

// One scroll owner: content never uses native AutoScroll or resets its offset on layout.
internal sealed class StudioScrollView : Panel, IMessageFilter
{
    private readonly Panel viewport = new() { Dock = DockStyle.Fill };
    private readonly ScrollRail rail;
    private readonly Control content;
    private bool arranging;
    private int offset;
    internal int Offset => offset;
    internal int MaximumOffset => Math.Max(0, content.Height - viewport.Height);
    internal Rectangle ThumbBounds => rail.ThumbBounds;

    internal StudioScrollView(Control content)
    {
        this.content = content;
        BackColor = Color.FromArgb(23, 28, 42);
        viewport.BackColor = BackColor;
        rail = new ScrollRail(this) { Dock = DockStyle.Right, Width = 16 };
        Controls.Add(viewport);
        Controls.Add(rail);
        content.Dock = DockStyle.None;
        if (content is ScrollableControl scroll) scroll.AutoScroll = false;
        viewport.Controls.Add(content);
        WatchFocus(content);
        content.SizeChanged += (_, _) => ArrangeContent();
        viewport.SizeChanged += (_, _) => ArrangeContent();
        Application.AddMessageFilter(this);
    }

    private void WatchFocus(Control control)
    {
        control.Enter += (_, _) =>
        {
            if (!control.Focused || !control.Visible) return;
            var bounds = viewport.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            if (bounds.Top < 0) SetOffset(offset + bounds.Top - 8);
            else if (bounds.Bottom > viewport.Height) SetOffset(offset + bounds.Bottom - viewport.Height + 8);
        };
        control.ControlAdded += (_, e) => { if (e.Control != null) WatchFocus(e.Control); };
        foreach (Control child in control.Controls) WatchFocus(child);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (content != null) ArrangeContent();
    }

    internal void ArrangeContent()
    {
        if (arranging || viewport.Width < 1 || IsDisposed) return;
        arranging = true;
        try
        {
            int width = Math.Max(1, viewport.Width);
            content.MaximumSize = new Size(width, 0);
            content.MinimumSize = new Size(width, 0);
            content.Width = width;
            if (content is FlowLayoutPanel flow)
            {
                foreach (Control child in flow.Controls)
                    child.Width = Math.Max(1, width - flow.Padding.Horizontal - child.Margin.Horizontal);
            }
            content.PerformLayout();
            content.Height = Math.Max(1, content.GetPreferredSize(new Size(width, 0)).Height);
            SetOffset(offset);
        }
        finally { arranging = false; }
    }

    internal void SetOffset(int value)
    {
        offset = Math.Clamp(value, 0, MaximumOffset);
        content.Location = new Point(0, -offset);
        rail.Invalidate();
    }

    internal void ScrollWheel(int delta)
    {
        int lines = SystemInformation.MouseWheelScrollLines;
        int step = lines < 0 ? viewport.Height : Math.Max(1, lines) * Font.Height;
        SetOffset(offset - (int)Math.Round(delta / 120d * step));
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg != 0x020A || !Visible || !IsHandleCreated || IsDisposed) return false;
        long position = message.LParam.ToInt64();
        var point = new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff));
        if (!RectangleToScreen(ClientRectangle).Contains(point)) return false;
        var target = Control.FromChildHandle(message.HWnd);
        if (target?.FindForm() != FindForm()) return false;
        // An open combo owns its popup; otherwise the wheel scrolls the page, never changes a setting accidentally.
        if (Descendants(content).OfType<ComboBox>().Any(c => c.DroppedDown)) return false;
        ScrollWheel((short)((message.WParam.ToInt64() >> 16) & 0xffff));
        return true;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Application.RemoveMessageFilter(this);
        base.Dispose(disposing);
    }

    private sealed class ScrollRail : Control
    {
        private readonly StudioScrollView owner;
        private int dragY, dragOffset;
        private bool dragging, hovered;
        internal Rectangle ThumbBounds
        {
            get
            {
                int track = Math.Max(1, Height - 8);
                int length = Math.Min(track, Math.Max(36, (int)(track * (double)owner.viewport.Height / Math.Max(1, owner.content.Height))));
                int top = 4 + (owner.MaximumOffset == 0 ? 0 : (int)Math.Round((track - length) * (double)owner.offset / owner.MaximumOffset));
                return new Rectangle(4, top, Math.Max(1, Width - 8), length);
            }
        }
        internal ScrollRail(StudioScrollView owner)
        {
            this.owner = owner;
            DoubleBuffered = true;
            TabStop = true;
            AccessibleName = "Прокрутка настроек";
            AccessibleRole = AccessibleRole.ScrollBar;
            Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(owner.BackColor);
            if (owner.MaximumOffset == 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = VisualTheme.Rounded(ThumbBounds, 4);
            using var fill = new SolidBrush(dragging || hovered || Focused ? VisualTheme.Accent : Color.FromArgb(91, 106, 128));
            e.Graphics.FillPath(fill, path);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || owner.MaximumOffset == 0) return;
            Focus();
            if (!ThumbBounds.Contains(e.Location)) owner.SetOffset(owner.offset + (e.Y < ThumbBounds.Top ? -1 : 1) * owner.viewport.Height);
            else { dragging = true; dragY = e.Y; dragOffset = owner.offset; Capture = true; }
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                int travel = Math.Max(1, Height - 8 - ThumbBounds.Height);
                owner.SetOffset(dragOffset + (int)Math.Round((e.Y - dragY) * (double)owner.MaximumOffset / travel));
            }
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is not (Keys.Home or Keys.End or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)) return;
            int next = e.KeyCode switch { Keys.Home => 0, Keys.End => owner.MaximumOffset, Keys.Up => owner.offset - 40, Keys.Down => owner.offset + 40, Keys.PageUp => owner.offset - owner.viewport.Height, Keys.PageDown => owner.offset + owner.viewport.Height, _ => owner.offset };
            owner.SetOffset(next);
            e.Handled = e.SuppressKeyPress = true;
        }
    }
}
