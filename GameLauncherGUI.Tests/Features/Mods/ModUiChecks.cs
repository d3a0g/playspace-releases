using System.Reflection;
using GameLauncherGUI;

internal static class ModUiChecks
{
    public static void Run(string root, List<ModPackage> catalog)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var dialog = new ModsForm(() => Task.FromResult(new List<GameInfo>()));
        var type = typeof(ModsForm);
        type.GetField("busy", flags)!.SetValue(dialog, true);
        var picker = (ComboBox)type.GetField("gamePicker", flags)!.GetValue(dialog)!;
        var choice = type.GetNestedType("GameChoice", BindingFlags.NonPublic)!;
        var game = new GameInfo { Name = "R.E.P.O.", SteamAppId = "3241660", ExePath = Path.Combine(root, "REPO.exe") };
        picker.Items.Add(Activator.CreateInstance(choice, game)!); picker.SelectedIndex = 0;
        type.GetField("adapter", flags)!.SetValue(dialog, ModGameAdapter.All[0]);
        type.GetField("installer", flags)!.SetValue(dialog, new ModInstaller(root));
        type.GetField("gameRoot", flags)!.SetValue(dialog, root);
        type.GetField("catalog", flags)!.SetValue(dialog, catalog);
        type.GetField("busy", flags)!.SetValue(dialog, false);
        type.GetMethod("FillList", flags)!.Invoke(dialog, null);
        var list = (ListBox)type.GetField("packages", flags)!.GetValue(dialog)!;
        var search = (TextBox)type.GetField("search", flags)!.GetValue(dialog)!;
        if (list.Items.Count == 0 || list.Items.Count > 300) throw new Exception("Catalog UI list bounds failed");
        search.Text = "no-such-mod-xyz";
        var action = (ModernButton)type.GetField("action", flags)!.GetValue(dialog)!;
        if (list.Items.Count != 0 || action.Enabled) throw new Exception("Catalog search empty state failed");
        search.Clear();
        if (!action.Enabled) throw new Exception("Compatible game installation action disabled");
        type.GetField("installer", flags)!.SetValue(dialog, null);
        type.GetMethod("ShowDetails", flags)!.Invoke(dialog, null);
        if (action.Enabled) throw new Exception("Missing root enables installation");
        type.GetField("installer", flags)!.SetValue(dialog, new ModInstaller(root));
        type.GetMethod("ShowDetails", flags)!.Invoke(dialog, null);
        string artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts"));
        Directory.CreateDirectory(artifacts);
        foreach (var size in new[] { new Size(1080, 800), new Size(960, 720) })
        {
            dialog.Size = size; dialog.PerformLayout();
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            using (var graphics = Graphics.FromImage(bitmap)) Render(dialog, graphics);
            bitmap.Save(Path.Combine(artifacts, "mods-" + size.Width + ".png"));
            if (list.Height < 200 || action.Height < 35) throw new Exception("Mod window layout clipped");
        }
        Console.WriteLine("PASS: mod catalog UI search, 300-result limit, valid/missing roots and two window sizes.");
    }

    internal static void Render(Control control, Graphics graphics)
    {
        if (control.Width < 1 || control.Height < 1) return;
        graphics.Clear(control.BackColor);
        if (control is ModCardList cards)
        {
            for (int i = 0; i < cards.Items.Count && i * cards.ItemHeight < cards.Height; i++)
            {
                var args = new DrawItemEventArgs(graphics, cards.Font, new Rectangle(0, i * cards.ItemHeight, cards.Width, cards.ItemHeight), i,
                    i == cards.SelectedIndex ? DrawItemState.Selected : DrawItemState.None);
                typeof(ModCardList).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cards, new object[] { args });
            }
            return;
        }
        if (control is TextBoxBase || control is ComboBox || control is ListBox || control is NumericUpDown)
        {
            string text = control.Text;
            if (control is NumericUpDown number) text = number.Value.ToString();
            if (control is ListBox list) text = string.Join("\r\n\r\n", list.Items.Cast<object>().Take(30));
            if (control is TextBox { TextLength: 0 } textbox) text = textbox.PlaceholderText;
            TextRenderer.DrawText(graphics, text, control.Font, new Rectangle(6, 4, Math.Max(1, control.Width - 12), Math.Max(1, control.Height - 8)),
                control.ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }
        var paint = new PaintEventArgs(graphics, control.ClientRectangle);
        typeof(Control).GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { paint });
        typeof(Control).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { paint });
        for (int i = control.Controls.Count - 1; i >= 0; i--)
        {
            var child = control.Controls[i];
            using var childBitmap = new Bitmap(Math.Max(1, child.Width), Math.Max(1, child.Height));
            using (var childGraphics = Graphics.FromImage(childBitmap)) Render(child, childGraphics);
            graphics.DrawImageUnscaled(childBitmap, child.Location);
        }
    }
}
