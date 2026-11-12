namespace GameLauncherGUI;

internal static class StudioMenus
{
    internal static void Show(ContextMenuStrip menu, Control anchor, bool preferAbove = false)
    {
        menu.Renderer = new StudioMenuRenderer();
        menu.BackColor = Color.FromArgb(23, 28, 42);
        menu.ForeColor = Color.FromArgb(236, 240, 247);
        menu.Padding = new Padding(5);
        foreach (ToolStripItem item in menu.Items)
            if (item is ToolStripMenuItem) item.Padding = new Padding(12, 10, 16, 10);
        menu.Show(anchor, new Point(0, preferAbove ? 0 : anchor.Height),
            preferAbove ? ToolStripDropDownDirection.AboveRight : ToolStripDropDownDirection.BelowRight);
    }

    private sealed class StudioMenuRenderer : ToolStripProfessionalRenderer
    {
        internal StudioMenuRenderer() : base(new GameMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var bounds = new Rectangle(3, 1, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 2));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = VisualTheme.Rounded(bounds, 5);
            using var fill = new SolidBrush(VisualTheme.Blend(Color.FromArgb(27, 34, 48), VisualTheme.Accent, .22f));
            e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            SmoothText.Draw(e.Graphics, e.Text ?? "", e.TextFont!, e.TextRectangle,
                e.Item.Enabled ? Color.FromArgb(236, 240, 247) : Color.FromArgb(144, 154, 174),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }
}
