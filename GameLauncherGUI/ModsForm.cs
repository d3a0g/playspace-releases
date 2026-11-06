using System.Diagnostics;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed class ModsForm : StudioDialog
{
    private sealed record GameChoice(GameInfo Game)
    {
        public override string ToString() => Game.Name + (StrategyGameAdapter.For(Game) != null ? " · Paradox / Workshop" : ModGameAdapter.For(Game) != null ? " · Thunderstore" : NativeModAdapter.For(Game)!=null ? " · ZIP моды" : " · нет адаптера");
    }
    private readonly Func<Task<List<GameInfo>>> discover;
    private readonly ModCatalog service = new();
    private readonly Dictionary<string, List<ModPackage>> cache = new();
    private readonly ComboBox gamePicker = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 35, 51), ForeColor = Color.White };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Название или автор мода…",
        BackColor = Color.FromArgb(30, 35, 51), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private readonly ModCardList packages = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(23, 28, 42),
        ForeColor = Color.FromArgb(231, 231, 247), BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = false };
    private readonly Label modTitle = new() { Text = "Выбери мод", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 19, FontStyle.Bold),
        ForeColor = Color.FromArgb(241, 237, 255), AutoEllipsis = true };
    private readonly Label modBadge = new() { Text = "ДЕТАЛИ ПАКЕТА", Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(170, 148, 247) };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(23, 28, 42), ForeColor = Color.FromArgb(196, 201, 223), BorderStyle = BorderStyle.None };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(160, 169, 199), AutoEllipsis = true };
    private readonly ModernButton catalogTab = new("Каталог", Color.FromArgb(38, 33, 65));
    private readonly ModernButton installedTab = new("Установленные", Color.FromArgb(38, 33, 65));
    private readonly ModernButton action = new("Установить", Color.FromArgb(115, 94, 244));
    private readonly ModernButton refresh = new("Обновить", Color.FromArgb(35, 40, 63));
    private CancellationTokenSource cancellation = new();
    private List<ModPackage> catalog = new();
    private ModState state = new();
    private ModGameAdapter? adapter;
    private ModInstaller? installer;
    private string? gameRoot;
    private bool busy, installing, installedOnly;
    private Control toolbarArea = null!, bodyArea = null!;
    private readonly Panel strategyHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = false };
    private Panel modesHost = null!;
    private StrategyModsPanel? strategyView;
    private NativeModsPanel? nativeView;
    private readonly Label sourceHint = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI",8), ForeColor = Color.FromArgb(113,128,157), AutoEllipsis = true };

    public ModsForm(Func<Task<List<GameInfo>>> discover)
    {
        this.discover = discover;
        Text = "PLAYSPACE · Моды";
        BackColor = Color.FromArgb(14, 17, 27); ForeColor = Color.FromArgb(236, 236, 248);
        Font = new Font("Segoe UI", 10); Size = new Size(1080, 800); MinimumSize = new Size(960, 720);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 18), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(new StudioHeading("Сделай игру своей.", "Новые механики, возможности и любимые моды — в одном месте.", "PLAYSPACE / МАСТЕРСКАЯ") { Dock = DockStyle.Fill }, 0, 0);
        var gameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        gameRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gameRow.Controls.Add(new Label { Text = "ИГРА НА ЭТОМ ПК", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(143, 152, 183) }, 0, 0);
        gameRow.ColumnCount=3;gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,142));
        var dlssButton=new ModernButton("✦  DLSS / RTX",Color.FromArgb(35,48,54)){Dock=DockStyle.Fill,Margin=new Padding(8,6,0,6)};
        dlssButton.Click+=(s,e)=>{if(gamePicker.SelectedItem is GameChoice selected){using var dialog=new DlssForm(selected.Game);dialog.ShowDialog(this);}};gameRow.Controls.Add(dlssButton,2,0);
        gamePicker.Margin = new Padding(0, 8, 0, 6); gameRow.Controls.Add(gamePicker, 1, 0); layout.Controls.Add(gameRow, 0, 1);
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        foreach (var button in new[] { catalogTab, installedTab, refresh }) { button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 4, 8, 6); }
        search.Margin = new Padding(0, 10, 12, 6);
        toolbar.Controls.Add(catalogTab, 0, 0); toolbar.Controls.Add(installedTab, 1, 0); toolbar.Controls.Add(search, 2, 0); toolbar.Controls.Add(refresh, 3, 0);
        toolbarArea = toolbar;
        layout.Controls.Add(toolbar, 0, 2);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        packages.Margin = new Padding(0, 2, 16, 0); body.Controls.Add(packages, 0, 0);
        var card = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        var detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, BackColor = Color.FromArgb(23, 28, 42) };
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        detailLayout.Controls.Add(modBadge, 0, 0); detailLayout.Controls.Add(modTitle, 0, 1); detailLayout.Controls.Add(details, 0, 2);
        action.Dock = DockStyle.Fill; action.Margin = new Padding(0, 10, 0, 0); detailLayout.Controls.Add(action, 0, 3);
        card.Controls.Add(detailLayout); body.Controls.Add(card, 1, 0); layout.Controls.Add(body, 0, 3);
        bodyArea = body;
        status.Margin = new Padding(0, 10, 0, 0); layout.Controls.Add(status, 0, 4);
        sourceHint.Text = "THUNDERSTORE  ·  Установка при закрытой игре  ·  Зависимости проверяются до применения файлов";
        layout.Controls.Add(sourceHint, 0, 5);
        layout.Controls.Remove(toolbar); layout.Controls.Remove(body); layout.Controls.Remove(status);
        var normalHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        normalHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        normalHost.RowStyles.Add(new RowStyle(SizeType.Absolute,48)); normalHost.RowStyles.Add(new RowStyle(SizeType.Percent,100)); normalHost.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        normalHost.Controls.Add(toolbar,0,0); normalHost.Controls.Add(body,0,1); normalHost.Controls.Add(status,0,2);
        modesHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; modesHost.Controls.Add(normalHost);
        layout.Controls.Add(modesHost, 0, 2); layout.SetRowSpan(modesHost,3);
        ContentHost.Controls.Add(layout);
        gamePicker.SelectedIndexChanged += async (s, e) => { if (!busy) await SelectGameAsync(false); };
        search.TextChanged += (s, e) => FillList();
        packages.SelectedIndexChanged += (s, e) => ShowDetails();
        catalogTab.Click += (s, e) => { installedOnly = false; FillList(); };
        installedTab.Click += (s, e) => { installedOnly = true; FillList(); };
        refresh.Click += async (s, e) => { if (busy) cancellation.Cancel(); else await SelectGameAsync(true); };
        action.Click += async (s, e) => await ApplyActionAsync();
        Shown += async (s, e) => await DiscoverAsync();
        FormClosing += (s, e) => { cancellation.Cancel(); if (installing) { e.Cancel = true; status.Text = "Завершаем установку или откат. Затем окно можно закрыть."; } };
    }

    private void Busy(bool value)
    {
        busy = value; gamePicker.Enabled = !value; catalogTab.Enabled = installedTab.Enabled = search.Enabled = !value;
        refresh.Text = value ? "Отменить" : "Обновить"; ShowDetails();
    }
    private async Task DiscoverAsync()
    {
        Busy(true); status.Text = "Определяем установленные игры на этом компьютере…";
        try
        {
            var games = await discover();
            if (IsDisposed) return;
            foreach (var game in games.GroupBy(g => !string.IsNullOrEmpty(g.SteamAppId) ? "steam:" + g.SteamAppId : g.ExePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).OrderByDescending(g => ModGameAdapter.For(g) != null || StrategyGameAdapter.For(g) != null).ThenBy(g => g.Name)) gamePicker.Items.Add(new GameChoice(game));
            if (gamePicker.Items.Count > 0) gamePicker.SelectedIndex = 0;
            else status.Text = "Установленные игры не найдены. Добавь игру в библиотеку или установи её через игровой клиент.";
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = "Ошибка поиска игр: " + ex.Message; }
        finally { if (!IsDisposed) Busy(false); }
        if (!IsDisposed && gamePicker.SelectedIndex >= 0) await SelectGameAsync(false);
    }
    private async Task SelectGameAsync(bool reload)
    {
        if (gamePicker.SelectedItem is not GameChoice choice) return;
        cancellation.Dispose(); cancellation = new();
        strategyView?.Dispose(); strategyView = null; strategyHost.Controls.Clear();
        nativeView?.Dispose();nativeView=null;
        var native=NativeModAdapter.For(choice.Game);
        if(native!=null){
            if(strategyHost.Parent==null)modesHost.Controls.Add(strategyHost);strategyHost.Visible=true;toolbarArea.Visible=bodyArea.Visible=status.Visible=false;
            nativeView=new NativeModsPanel(native,choice.Game);nativeView.BusyChanged+=value=>{busy=installing=value;gamePicker.Enabled=!value;};strategyHost.Controls.Add(nativeView);strategyHost.BringToFront();sourceHint.Text="ZIP МОДЫ · Оригинальные файлы не перезаписываются · Активация зависит от игры";return;
        }
        var strategy = StrategyGameAdapter.For(choice.Game);
        if (strategy == null) strategyHost.Parent?.Controls.Remove(strategyHost);
        else if (strategyHost.Parent == null) modesHost.Controls.Add(strategyHost);
        strategyHost.Visible = strategy != null;
        toolbarArea.Visible = bodyArea.Visible = status.Visible = strategy == null;
        sourceHint.Text = strategy == null ? "THUNDERSTORE  ·  Установка при закрытой игре  ·  Зависимости проверяются до применения файлов" :
            "PARADOX  ·  Локальные ZIP и скачанные Workshop-моды  ·  Наборы Paradox Launcher не изменяются";
        if (strategy != null)
        {
            strategyView = new StrategyModsPanel(strategy, choice.Game);
            strategyView.BusyChanged += value => { busy = installing = value; gamePicker.Enabled = !value; };
            strategyHost.Controls.Add(strategyView); strategyHost.BringToFront();
            await strategyView.ReloadAsync();
            return;
        }
        adapter = ModGameAdapter.For(choice.Game); catalog = new(); state = new(); installer = null; gameRoot = null;
        FillList();
        if (adapter == null)
        {
            status.Text = "Игра найдена на этом ПК, но установщик для неё пока не подключён.\nПоддерживаются: " + string.Join(", ", ModGameAdapter.All.Select(a => a.Name));
            return;
        }
        Busy(true);
        try
        {
            gameRoot = adapter.FindRoot(choice.Game);
            if (gameRoot != null) { installer = new ModInstaller(gameRoot); state = await Task.Run(installer.ReadState); }
            status.Text = "Загрузка каталога " + adapter.Name + "…";
            if (!reload && cache.TryGetValue(adapter.Community, out var stored)) catalog = stored;
            else
            {
                catalog = await service.LoadAsync(adapter, cancellation.Token);
                if (cache.Count >= 2 && !cache.ContainsKey(adapter.Community)) cache.Clear();
                cache[adapter.Community] = catalog;
            }
            if (!IsDisposed) status.Text = $"{adapter.Name}: {catalog.Count} пакетов. Показаны первые 300 подходящих результатов; уточни поиск." +
                (gameRoot == null ? "\nПапка игры не подтверждена. Найди/добавь её exe в библиотеке для установки." : "\nПапка: " + gameRoot);
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Загрузка отменена. Нажми «Обновить», чтобы повторить."; }
        catch (Exception ex) { if (!IsDisposed) status.Text = "Каталог недоступен: " + ex.Message; }
        finally { if (!IsDisposed) { Busy(false); FillList(); } }
    }
    private void FillList()
    {
        packages.BeginUpdate(); packages.Items.Clear();
        string query = search.Text.Trim();
        if (installedOnly)
            foreach (var mod in state.Packages.Values.Where(p => p.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Id)) packages.Items.Add(mod);
        else
            foreach (var package in catalog.Where(p => !p.Deprecated && !p.Nsfw && p.Latest != null &&
                !p.Categories.Contains("Modpacks", StringComparer.OrdinalIgnoreCase) && p.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Latest!.Downloads).Take(300)) packages.Items.Add(package);
        packages.EndUpdate();
        catalogTab.IsActive = !installedOnly; installedTab.IsActive = installedOnly;
        installedTab.Text = $"Установленные · {state.Packages.Count}";
        if (packages.Items.Count > 0) packages.SelectedIndex = 0;
        ShowDetails();
    }
    private void ShowDetails()
    {
        if (packages.SelectedItem is ModPackage package)
        {
            modTitle.Text = package.Name.Replace('_', ' ');
            bool exists = state.Packages.ContainsKey(package.Id);
            modBadge.Text = exists ? "● УСТАНОВЛЕН" : "КАТАЛОГ / THUNDERSTORE";
            details.Text = "Автор: " + package.Owner + "\r\nВерсия: " + package.Latest?.Version + "\r\n\r\n" + package.Latest?.Description +
                "\r\n\r\nЗависимости:\r\n" + string.Join("\r\n", package.Latest?.Dependencies ?? Array.Empty<string>()) +
                (exists ? "\r\n\r\nУже установлен. Для смены версии сначала удали пакет и зависящие от него моды." : "\r\n\r\nПеред установкой будет показан полный список пакетов.");
            action.Text = exists ? "Уже установлен" : "Установить мод и зависимости";
            action.Enabled = !busy && !exists && installer != null;
        }
        else if (packages.SelectedItem is InstalledMod installed)
        {
            modTitle.Text = installed.Id.Replace('_', ' '); modBadge.Text = "● УСТАНОВЛЕН В ЭТОЙ ИГРЕ";
            details.Text = installed.Id + "\r\nВерсия: " + installed.Version + "\r\nФайлов: " + installed.Files.Count +
                "\r\n\r\nУдаляются только файлы, добавленные PLAYSPACE. Изменённые файлы сохраняются: их нужно проверить вручную.";
            action.Text = "Удалить выбранный пакет"; action.Enabled = !busy && installer != null;
        }
        else { modTitle.Text = busy ? "Готовим каталог…" : "Твой следующий мод"; modBadge.Text = "PLAYSPACE / МАСТЕРСКАЯ";
            action.Text = installedOnly ? "Выбери установленный мод" : "Выбери мод"; action.Enabled = false; details.Text = "Выбери игру и мод.\r\n\r\nОригинальные файлы игры не перезаписываются. Модпаки и пакеты с нестандартными установщиками не поддерживаются."; }
    }
    private async Task ApplyActionAsync()
    {
        if (busy || installer == null || adapter == null || gameRoot == null) return;
        string processName = Path.GetFileNameWithoutExtension(adapter.Executable);
        var processNames = adapter.IsUnreal ? new[] { processName, processName + "-Win64-Shipping" } : new[] { processName };
        var processes = processNames.SelectMany(Process.GetProcessesByName).ToArray();
        bool running = processes.Length > 0; foreach (var process in processes) process.Dispose();
        if (running) { status.Text = "Сначала закрой игру, затем повтори установку/удаление."; return; }
        try
        {
            List<ModSelection>? plan = null; string? uninstall = null;
            if (packages.SelectedItem is ModPackage package)
            {
                plan = ModInstaller.Resolve(package, catalog, adapter, state);
                if (plan.Count == 0) return;
                string list = string.Join("\n", plan.Select(p => p.FullId));
                if (MessageBox.Show(this, $"Установить в {gameRoot}:\n\n{list}\n\nМоды содержат сторонний код и могут влиять на игру. Продолжить?", "Установка модов", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            }
            else if (packages.SelectedItem is InstalledMod installed)
            {
                uninstall = installed.Id;
                if (MessageBox.Show(this, "Удалить " + uninstall + "? Зависимости останутся для других модов.", "Удаление мода", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            }
            else return;
            cancellation.Dispose(); cancellation = new(); installing = true; Busy(true);
            var progress = new Progress<string>(message => { if (!IsDisposed) status.Text = message; });
            if (plan != null) await installer.InstallAsync(plan, adapter, service, progress, cancellation.Token);
            else await Task.Run(() => installer.Uninstall(uninstall!));
            state = await Task.Run(installer.ReadState);
            status.Text = adapter.IsUnreal ? "Готово. UE4SS-моды запускай кнопкой «Играть» в PLAYSPACE: она передаёт пути профиля загрузчику." :
                "Готово. Запусти игру обычной кнопкой «Играть». Совместимость зависит от версии игры и модов.";
        }
        catch (OperationCanceledException) { status.Text = "Установка отменена до применения файлов."; }
        catch (Exception ex) { status.Text = "Операция не выполнена: " + ex.Message; }
        finally { installing = false; Busy(false); FillList(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { cancellation.Cancel(); cancellation.Dispose(); service.Dispose(); strategyHost.Dispose(); }
        base.Dispose(disposing);
    }
}
