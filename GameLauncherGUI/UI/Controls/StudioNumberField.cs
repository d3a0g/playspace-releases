namespace GameLauncherGUI;

internal sealed class StudioNumberField : Panel
{
    private readonly NumericUpDown input;
    private readonly Panel editor = new();
    internal StudioNumberField(NumericUpDown input)
    {
        this.input = input;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(27, 33, 48);
        input.BorderStyle = BorderStyle.None;
        input.Dock = DockStyle.None;
        input.BackColor = BackColor;
        input.ForeColor = Color.FromArgb(236, 240, 247);
        input.TextAlign = HorizontalAlignment.Left;
        editor.BackColor = BackColor;
        editor.Controls.Add(input);
        Controls.Add(editor);
        var minus = new ModernButton("−", BackColor) { AccessibleName = "Уменьшить значение", TabStop = false };
        var plus = new ModernButton("+", BackColor) { AccessibleName = "Увеличить значение", TabStop = false };
        Controls.Add(minus); Controls.Add(plus);
        minus.Click += (_, _) => { input.Focus(); input.DownButton(); };
        plus.Click += (_, _) => { input.Focus(); input.UpButton(); };
        Layout += (_, _) =>
        {
            int buttonWidth = Math.Min(32, Width / 4);
            plus.Bounds = new Rectangle(Width - buttonWidth - 3, 3, buttonWidth, Math.Max(1, Height - 6));
            minus.Bounds = new Rectangle(plus.Left - buttonWidth - 2, 3, buttonWidth, Math.Max(1, Height - 6));
            editor.SetBounds(10, Math.Max(3, (Height - input.PreferredSize.Height) / 2), Math.Max(1, minus.Left - 14), input.PreferredSize.Height);
            // Keep the native editor and accessibility, clip its tiny system spin buttons.
            input.SetBounds(0, 0, editor.Width + SystemInformation.VerticalScrollBarWidth + 6, input.PreferredSize.Height);
        };
        input.EnabledChanged += (_, _) => { minus.Enabled = plus.Enabled = input.Enabled; Invalidate(); };
        input.GotFocus += (_, _) => Invalidate(); input.LostFocus += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = VisualTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 6);
        using var border = new Pen(input.ContainsFocus ? VisualTheme.Accent : Color.FromArgb(49, 59, 78));
        e.Graphics.DrawPath(border, path);
    }
}
