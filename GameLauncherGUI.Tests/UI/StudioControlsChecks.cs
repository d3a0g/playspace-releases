using System.Reflection;
using GameLauncherGUI;

internal static class StudioControlsChecks
{
    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        Application.EnableVisualStyles();
        using (var form = new Form { ClientSize = new Size(620, 360) })
        {
            var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            for (int i = 0; i < 30; i++) flow.Controls.Add(new Label { Text = "Параметр " + i, Height = 50, Width = 500 });
            var lastInput = new TextBox { Text = "Последний параметр", Width = 500 };
            flow.Controls.Add(lastInput);
            var view = new StudioScrollView(flow) { Dock = DockStyle.Fill };
            form.Controls.Add(view); form.Show(); Application.DoEvents(); view.ArrangeContent();
            Check(view.MaximumOffset > 1000, "Long content did not create scroll range");
            view.ScrollWheel(-120); Check(view.Offset > 0, "Wheel does not scroll down");
            view.ScrollWheel(120); Check(view.Offset == 0, "Wheel does not scroll up");
            var point = view.PointToScreen(new Point(40, 40));
            var wheel = Message.Create(view.Handle, 0x020A, new IntPtr(-120 << 16), new IntPtr((point.Y << 16) | (point.X & 0xffff)));
            Check(view.PreFilterMessage(ref wheel) && view.Offset > 0, "Wheel message over nested page not handled");
            view.SetOffset(0);
            var rail = view.Controls.Cast<Control>().Single(c => c.AccessibleRole == AccessibleRole.ScrollBar);
            void Mouse(string method, int y) => rail.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(rail, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 8, y, 0) });
            int start = view.ThumbBounds.Top + 5;
            Mouse("OnMouseDown", start); Mouse("OnMouseMove", rail.Height + 100); Mouse("OnMouseUp", rail.Height + 100);
            Check(view.Offset == view.MaximumOffset, "Dragging thumb does not reach bottom");
            Check(flow.Bottom == view.Height, "Last row is clipped at end");
            start = view.ThumbBounds.Top + 5;
            Mouse("OnMouseDown", start); Mouse("OnMouseMove", -100); Mouse("OnMouseUp", -100);
            Check(view.Offset == 0, "Dragging thumb does not reach top");
            lastInput.Focus(); Application.DoEvents();
            Check(view.Offset > 1000 && view.RectangleToClient(lastInput.RectangleToScreen(lastInput.ClientRectangle)).Bottom <= view.Height, "Keyboard focus did not reveal final editor");
            view.SetOffset(400); int previous = view.Offset; view.ArrangeContent();
            Check(view.Offset == previous, "Relayout resets scroll position");
            form.Height += 100; Application.DoEvents();
            Check(view.Offset == previous, "Resize resets valid scroll position");
            foreach (Control child in flow.Controls.Cast<Control>().ToArray()) child.Dispose(); view.ArrangeContent();
            Check(view.Offset == 0 && view.MaximumOffset == 0, "Short content retains stale offset");
            using var menu = new ContextMenuStrip(); menu.Items.Add("Убрать выбранные из библиотеки"); menu.Items.Add("Очистить библиотеку…");
            var anchor = new Button { Text = "Ещё", Bounds = new Rectangle(0, form.ClientSize.Height - 35, 120, 32) }; form.Controls.Add(anchor);
            StudioMenus.Show(menu, anchor, true); Application.DoEvents();
            Check(menu.Bottom <= anchor.PointToScreen(Point.Empty).Y + 1, "Footer menu did not open upwards");
            Check(Screen.FromControl(form).WorkingArea.Contains(menu.Bounds), "Menu outside working area"); menu.Close();
        }
        var game = new GameInfo { Name = "Проверка интерфейса", ExePath = "fixture.exe" };
        using (var form = new PerformanceForm(new[] { game }, game, (_, _) => true))
        {
            form.Show(); Application.DoEvents();
            string output = Path.GetFullPath("artifacts/controls-refresh-previews"); Directory.CreateDirectory(output);
            foreach (var size in new[] { new Size(1120, 840), new Size(960, 720) })
            {
                form.Size = size; Application.DoEvents();
                var scroll = Children(form).OfType<StudioScrollView>().Single(c => c.Visible);
                scroll.SetOffset(int.MaxValue); form.Refresh(); Application.DoEvents();
                Check(!Children(form).OfType<ScrollableControl>().Any(c => c.Visible && c.VerticalScroll.Visible), "Native white scrollbar remains");
                Check(Children(form).OfType<ModernButton>().Single(b => b.Text == "Сохранить").Visible, "Footer unavailable");
                using var bitmap = new Bitmap(form.Width, form.Height); using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(form.Location,Point.Empty,form.Size); bitmap.Save(Path.Combine(output, "performance-" + size.Width + ".png"));
            }
            Children(form).OfType<ModernButton>().Single(b => b.Text == "Графика").PerformClick(); Application.DoEvents();
            Check(Children(form).OfType<StudioScrollView>().Single(c => c.Visible).Name == "GraphicsSettingsScroll", "Graphics page not using scroll owner");
        }
        Console.WriteLine($"PASS: {checks} studio control checks: wheel, thumb drag both directions, last row, relayout/resize, short content, menu placement, performance at two sizes.");
    }
}
