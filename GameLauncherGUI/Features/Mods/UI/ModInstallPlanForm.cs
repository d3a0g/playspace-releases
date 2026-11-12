namespace GameLauncherGUI;

internal sealed class ModInstallPlanForm : StudioDialog
{
    internal static string Describe(string root, IReadOnlyList<ModSelection> plan)
    {
        long bytes = plan.Sum(p => Math.Max(0, p.Version.FileSize));
        bool unknown = plan.Any(p => p.Version.FileSize <= 0);
        return "Папка игры\n" + root + "\n\nБудут установлены мод и необходимые зависимости:\n\n" +
            string.Join("\n\n", plan.Select(p => p.Package.Name + " · " + p.Version.Version + "\nАвтор: " + p.Package.Owner +
                "\nИсточник: " + p.Version.DownloadUrl + "\nАрхив: " + (p.Version.FileSize > 0 ? $"{p.Version.FileSize / 1048576.0:N1} МБ" : "размер не указан источником") +
                "\nЗависимости: " + (p.Version.Dependencies.Length == 0 ? "нет" : string.Join(", ", p.Version.Dependencies)))) +
            $"\n\nИзвестный объём загрузки: {bytes / 1048576.0:N1} МБ" + (unknown ? ". Размер части архивов неизвестен." : ".") +
            "\n\nТочный состав файлов определяется после загрузки и проверки архивов. Менеджер проверит пути и конфликты перед записью. Размер после распаковки может быть больше." +
            "\n\nМоды — сторонний код. Они могут влиять на стабильность игры. Удаление управляемых файлов доступно в списке установленных модов; исходные файлы игры не должны заменяться без проверки менеджера.";
    }
    internal ModInstallPlanForm(string root, IReadOnlyList<ModSelection> plan) : this(Describe(root,plan),"План установки модов") { }
    internal ModInstallPlanForm(string text,string title)
    {
        Text = "PLAYSPACE / "+title; Size = new(840, 690); MinimumSize = new(700, 540);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(24) };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 52));
        var description = new FormattedDescription { Dock = DockStyle.Fill, Text = text, AccessibleName = "План установки модов" };
        layout.Controls.Add(description, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var install = new ModernButton("Установить по этому плану", VisualTheme.Accent) { Width = 280, Height = 42, DialogResult = DialogResult.OK };
        var cancel = new ModernButton("Отмена", Color.FromArgb(35,40,63)) { Width = 130, Height = 42, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(install); actions.Controls.Add(cancel); layout.Controls.Add(actions, 0, 1);
        ContentHost.Controls.Add(layout); CancelButton = cancel;
    }
}
