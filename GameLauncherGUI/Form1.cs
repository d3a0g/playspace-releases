using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace GameLauncherGUI
{
    public class GameInfo
    {
        public string Name { get; set; }
        public string ExePath { get; set; }
        public string SteamAppId { get; set; }
        public string LaunchArguments { get; set; } = "";
        public bool IsPinned { get; set; }
        public double PlayedSeconds { get; set; }
        public long? SteamPlaytimeMinutes { get; set; }
        public string? SteamPlaytimeUser { get; set; }
        public DateTimeOffset? SteamPlaytimeUpdatedUtc { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public double DisplayPlayedSeconds => SteamPlaytimeMinutes.HasValue ? SteamPlaytimeMinutes.Value * 60.0 : PlayedSeconds;
        [Newtonsoft.Json.JsonIgnore]
        public string PlaytimeLabel => SteamPlaytimeMinutes.HasValue ? $"Steam · {SteamPlaytimeMinutes.Value / 60.0:N1} ч" :
            !string.IsNullOrEmpty(SteamAppId) && SteamAppId.All(char.IsAsciiDigit) ? "Steam · время не загружено" : $"Локально · {PlayedSeconds / 3600:0.0} ч";
        public string? CustomIconPath { get; set; }
        public string TrackingExecutable { get; set; } = "";
        public string? AchievementSummary { get; set; }
        public PerformanceProfile? Performance { get; set; }
        public DateTimeOffset? LastLaunchUtc { get; set; }
        public string? InstallDirectory { get; set; }
    }

    public partial class Form1 : Form
    {
        private string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncherPRO");
        private string dataFile;
        private List<GameInfo> myGames = new List<GameInfo>();

        // Цвета
        private Color bgColor = Color.FromArgb(14, 17, 27);
        private Color panelColor = Color.FromArgb(23, 28, 42);
        private Color accentColor = Color.FromArgb(115, 94, 244); 
        private Color dangerColor = Color.FromArgb(230, 50, 60); 
        private Color successColor = Color.FromArgb(45, 160, 90); 
        private Color manualColor = Color.FromArgb(140, 80, 230);
        private Color textColor = Color.FromArgb(235, 235, 235);

        private CustomCheckListBox listBoxGames;
        private ModernButton btnLaunch;
        private ModernButton btnDeleteChecked;
        private ModernButton btnClearAll;
        private ModernButton btnAddManual;
        private ModernButton btnScan;
        private Label lblStatus;
        private Label btnClose;
        private Label libraryCount;
        private ProgressBar scanProgress;
        private bool launchingGame;
        private readonly GameIconCache iconCache = new();
        private int iconLoadVersion;
        private Icon? appIcon;
        private Bitmap? brandImage;
        private Bitmap? supportIcon;
        private readonly List<Bitmap> navigationStickers = new();
        private List<GameInfo> visibleGames = new();
        private bool pinnedOnly;
        private ModernButton btnLibrary;
        private ModernButton btnPinned;
        private ModernButton btnPin;
        private TextBox searchGames;
        private ModernButton btnClearSearch;
        private ModernSelect sortGames;
        private bool recentOnly;
        private ModernButton btnRecent;
        private string? libraryNotice;
        private ContextMenuStrip? gameMenu;
        private LibraryHero hero;
        private ModernButton btnSurprise;
        private readonly GameShuffle shuffle = new();
        private readonly System.Threading.SemaphoreSlim artworkGate = new(1, 1);
        private System.Threading.CancellationTokenSource? artworkCancellation;
        private GameInfo? spotlightGame;

        // Переменные для растягивания окна
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 0x1;
        private const int HTLEFT = 0xA;
        private const int HTRIGHT = 0xB;
        private const int HTTOP = 0xC;
        private const int HTTOPLEFT = 0xD;
        private const int HTTOPRIGHT = 0xE;
        private const int HTBOTTOM = 0xF;
        private const int HTBOTTOMLEFT = 0x10;
        private const int HTBOTTOMRIGHT = 0x11;
        private const int BORDER_SIZE = 10;

        public Form1() : this(null, true) { }

        internal Form1(string? storageFolder, bool enableUpdateChecks = false, (List<GameInfo> Games, string? Notice)? startupLibrary = null)
        {
            updateChecksEnabled = enableUpdateChecks;
            if (storageFolder != null) appDataFolder = storageFolder;
            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
            }
            dataFile = Path.Combine(appDataFolder, "games.json");

            // Полностью убираем стандартную рамку
            this.FormBorderStyle = FormBorderStyle.None;
            this.MinimumSize = new Size(960, 720); 
            this.Size = new Size(1120, 800); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = bgColor;
            this.ForeColor = textColor;
            this.DoubleBuffered = true;
            using (var iconStream = typeof(Form1).Assembly.GetManifestResourceStream("GameLauncherGUI.AppIcon"))
            {
                if (iconStream != null)
                {
                    using var loadedIcon = new Icon(iconStream, new Size(48, 48));
                    appIcon = (Icon)loadedIcon.Clone();
                    Icon = appIcon;
                    brandImage = appIcon.ToBitmap();
                }
            }

            preloadedLibrary=startupLibrary;
            LoadData();
            InitializeUI();
            foreach(var item in new[]{(btnLibrary,"library"),(btnPinned,"favorite"),(btnRecent,"recent"),(btnSurprise,"discover")}){var sticker=NavigationSticker.Create(item.Item2);navigationStickers.Add(sticker);item.Item1.Image=sticker;}
            RefreshList();
            InitializeLibraryExtras();
            UiTypography.Apply(this);
            if (libraryNotice != null) lblStatus.Text = libraryNotice;
            Shown += async (s, e) => await LoadGameIconsAsync(listBoxGames, visibleGames.ToArray(), iconLoadVersion);

            // Обработчик изменения размера, чтобы крестик всегда оставался в углу

        }

        private void InitializeUI()
        {
            SuspendLayout();
            Text = "LauncherPlaySpace";
            Font = new Font("Segoe UI", 10);
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 56, Width = ClientSize.Width, BackColor = bgColor };
            MouseEventHandler drag = (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
            titleBar.MouseDown += drag;
            var brandIcon = new PictureBox { Image = brandImage, SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(26, 26), Location = new Point(25, 15) };
            brandIcon.MouseDown += drag;
            titleBar.Controls.Add(brandIcon);
            var brand = new Label { Text = "PLAYSPACE", Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = textColor, AutoSize = true, Location = new Point(58, 17) };
            brand.MouseDown += drag;
            titleBar.Controls.Add(brand);
            var windowActions=new FlowLayoutPanel {Dock=DockStyle.Right,Width=150,Padding=new Padding(6,10,10,10),WrapContents=false,FlowDirection=FlowDirection.LeftToRight};
            Label WindowAction(string text,string name)=>new(){Text=text,AccessibleName=name,Font=new Font("Segoe UI",15),ForeColor=Color.FromArgb(179,186,210),Size=new Size(40,34),Margin=new Padding(1,0,1,0),Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter,BackColor=bgColor};
            var minimize=WindowAction("−","Свернуть");var maximize=WindowAction("□","Развернуть");btnClose=WindowAction("×","Закрыть");
            minimize.Click+=(s,e)=>WindowState=FormWindowState.Minimized;maximize.Click+=(s,e)=>WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;btnClose.Click+=(s,e)=>Close();
            foreach(var button in new[]{minimize,maximize,btnClose}){button.MouseEnter+=(s,e)=>button.BackColor=Color.FromArgb(39,43,62);button.MouseLeave+=(s,e)=>button.BackColor=bgColor;windowActions.Controls.Add(button);}
            titleBar.Controls.Add(windowActions);MaximizedBounds=Screen.FromControl(this).WorkingArea;
            Controls.Add(titleBar);

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(20, 8, 20, 18), BackColor = bgColor };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11,
                BackColor = bgColor, Padding = new Padding(4, 10, 20, 0), Margin = Padding.Empty };
            foreach (float height in new[] { 34F, 52F, 52F, 28F, 56F, 56F, 56F, 56F }) sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            sidebar.Controls.Add(new Label { Text = "ТВОЁ ПРОСТРАНСТВО", Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(113, 120, 149) }, 0, 0);
            btnLibrary = new ModernButton("Мои игры", Color.FromArgb(31, 35, 51)) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            btnLibrary.Click += (s, e) => ShowLibrary(false);
            sidebar.Controls.Add(btnLibrary, 0, 1);
            btnRecent = new ModernButton("Недавно играл", Color.FromArgb(31, 35, 51)) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            btnRecent.Click += (s, e) => ShowRecentLibrary();
            sidebar.Controls.Add(btnRecent, 0, 2);
            sidebar.Controls.Add(new Label { Text = "КОЛЛЕКЦИЯ", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.FromArgb(113, 120, 149), TextAlign = ContentAlignment.BottomLeft }, 0, 3);
            btnScan = new ModernButton("Найти игры", Color.FromArgb(35, 40, 63)) { Dock = DockStyle.Fill, Margin = new Padding(8, 4, 0, 6) };
            btnScan.Click += (s, e) => AutoScanGamesAsync();
            btnPinned = new ModernButton("Любимые игры", Color.FromArgb(31, 35, 51)) { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            btnPinned.Click += (s, e) => ShowLibrary(true);
            sidebar.Controls.Add(btnPinned, 0, 4);
            btnAddManual = new ModernButton("+  Добавить игру", accentColor) { Dock = DockStyle.Fill, Margin = new Padding(8, 4, 0, 6) };
            btnAddManual.Click += (s, e) => AddGameManually();
            var performance = new ModernButton("⚡ Производительность", Color.FromArgb(35, 40, 63)) {
                Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            performance.Click += (s, e) => OpenPerformance(SelectedGame);
            sidebar.Controls.Add(performance, 0, 5);
            var mods = new ModernButton("◇  Моды", Color.FromArgb(35, 40, 63)) {
                Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8) };
            mods.Click += (s, e) => OpenMods();
            sidebar.Controls.Add(mods, 0, 6);
            btnSurprise = new ModernButton("Открыть новое", Color.FromArgb(49, 38, 77)) {
                Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8) };
            btnSurprise.Click += (s, e) => OpenRecommendations();
            sidebar.Controls.Add(btnSurprise, 0, 7);
            supportIcon = VisualTheme.CreateTelegramIcon();
            var support = new ModernButton("Поддержка\nНаписать боту", Color.FromArgb(26, 43, 65)) {
                Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Image = supportIcon,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AccessibleName = "Поддержка в Telegram @PlaySpaceSupport_bot",
                AccessibleDescription = "Открыть https://t.me/PlaySpaceSupport_bot" };
            support.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://t.me/PlaySpaceSupport_bot", UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
                { lblStatus.Text = "Не удалось открыть Telegram. Напиши @PlaySpaceSupport_bot вручную."; }
            };
            sidebar.Controls.Add(support, 0, 9);
            var settings = new ModernButton("⚙  Настройки", Color.FromArgb(31,35,51)) { Dock = DockStyle.Top, Height = 44, Margin = new Padding(0,8,0,0) }; settings.Click += (s,e) => OpenSettings(); sidebar.Controls.Add(settings,0,8);
            sidebar.Controls.Add(new Label { Text = "●  ВСЁ В ОДНОМ МЕСТЕ", Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(113, 218, 193), Font = new Font("Segoe UI", 8, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft }, 0, 10);
            shell.Controls.Add(sidebar, 0, 0);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            hero = new LibraryHero { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            hero.PlayRequested += (s, e) => LaunchSelectedGame();
            content.Controls.Add(hero, 0, 0);
            libraryCount = new Label { Text = "МОЯ БИБЛИОТЕКА", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(161, 168, 193),
                Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            var libraryHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            libraryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            libraryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            libraryHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            libraryHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sortGames = new ModernSelect {
                BackColor = panelColor, ForeColor = textColor, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4), Font = new Font("Segoe UI", 9) };
            sortGames.Items.AddRange(new object[] { "Порядок добавления", "Название: А–Я", "Последние запуски", "Больше времени в игре" });
            sortGames.SelectedIndex = 0;
            sortGames.SelectedIndexChanged += (s, e) => RefreshList();
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
            searchGames=new TextBox {PlaceholderText="Найти игру · Ctrl+F"};
            btnClearSearch=new ModernButton("×",panelColor){Enabled=false};
            var searchPanel=new ModernSearchField(searchGames,btnClearSearch){Dock=DockStyle.Fill,Margin=new Padding(0,4,0,6)};
            searchGames.TextChanged+=(s,e)=>{btnClearSearch.Enabled=searchGames.TextLength>0;RefreshList();};
            tools.Controls.Add(searchPanel, 0, 0);
            tools.Controls.Add(btnScan, 1, 0);
            tools.Controls.Add(btnAddManual, 2, 0);
            var headingTools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var viewToggle = new ModernButton("▦ / ☰", panelColor) { Width = 72, Height = 30 };
            viewToggle.Click += (s,e) => { int selected = listBoxGames.SelectedIndex; listBoxGames.PosterView = !listBoxGames.PosterView; listBoxGames.ResetView(); listBoxGames.SelectItem(selected); };
            headingTools.Controls.Add(viewToggle); libraryCount.AutoSize = true; headingTools.Controls.Add(libraryCount);
            libraryHeader.Controls.Add(headingTools, 0, 0);
            libraryHeader.Controls.Add(sortGames, 1, 0);
            libraryHeader.Controls.Add(tools, 0, 1);
            libraryHeader.SetColumnSpan(tools, 2);
            content.Controls.Add(libraryHeader, 0, 1);
            listBoxGames = new CustomCheckListBox { Dock = DockStyle.Fill, BackColor = bgColor,
                ForeColor = textColor, Margin = Padding.Empty, Padding = new Padding(4, 8, 4, 8) };
            listBoxGames.ItemDoubleClick += (s, e) => LaunchSelectedGame();
            listBoxGames.ShowPinButtons = true;
            listBoxGames.ShowProfileButtons = true;
            listBoxGames.PinToggleRequested += TogglePinnedGame;
            listBoxGames.ProfileRequested += index => { if (index >= 0 && index < visibleGames.Count) OpenPerformance(visibleGames[index]); };
            listBoxGames.SelectionChanged += (s, e) => UpdateGameActions();
            InitializeGameMenu();
            content.Controls.Add(listBoxGames, 0, 2);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
            btnLaunch = new ModernButton("▶  Играть", accentColor) { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 8, 4) };
            btnLaunch.Click += (s, e) => LaunchSelectedGame();
            btnPin = new ModernButton("☆  Закрепить", Color.FromArgb(39, 34, 61)) { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 8, 4) };
            btnPin.Click += (s, e) => TogglePinnedGame(listBoxGames.SelectedIndex);
            btnDeleteChecked = new ModernButton("Убрать выбранные", Color.FromArgb(45, 35, 52)) { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 8, 4) };
            btnDeleteChecked.Click += (s, e) => DeleteCheckedGames();
            btnClearAll = new ModernButton("Очистить", Color.FromArgb(31, 35, 51)) { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 4) };
            btnClearAll.Click += (s, e) => ClearAllGames();
            actions.Controls.Add(btnLaunch, 0, 0);
            actions.Controls.Add(btnPin, 1, 0);
            actions.Controls.Add(btnDeleteChecked, 2, 0);
            actions.Controls.Add(btnClearAll, 3, 0);
            content.Controls.Add(actions, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            lblStatus = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(133, 143, 171), Font = new Font("Segoe UI", 9),
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            scanProgress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24,
                Visible = false, Margin = new Padding(0, 12, 0, 10) };
            footer.Controls.Add(lblStatus, 0, 0);
            footer.Controls.Add(scanProgress, 1, 0);
            content.Controls.Add(footer, 0, 4);
            shell.Controls.Add(content, 1, 0);
            Controls.Add(shell);
            shell.BringToFront();
            ResumeLayout(true);
        }
        // --- МАГИЯ ДЛЯ РАСТЯГИВАНИЯ ОКНА ---
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST)
            {
                Point pos = this.PointToClient(new Point(m.LParam.ToInt32()));
                if (pos.X <= BORDER_SIZE && pos.Y <= BORDER_SIZE) m.Result = (IntPtr)HTTOPLEFT;
                else if (pos.X >= ClientSize.Width - BORDER_SIZE && pos.Y <= BORDER_SIZE) m.Result = (IntPtr)HTTOPRIGHT;
                else if (pos.X <= BORDER_SIZE && pos.Y >= ClientSize.Height - BORDER_SIZE) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (pos.X >= ClientSize.Width - BORDER_SIZE && pos.Y >= ClientSize.Height - BORDER_SIZE) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (pos.X <= BORDER_SIZE) m.Result = (IntPtr)HTLEFT;
                else if (pos.X >= ClientSize.Width - BORDER_SIZE) m.Result = (IntPtr)HTRIGHT;
                else if (pos.Y <= BORDER_SIZE) m.Result = (IntPtr)HTTOP;
                else if (pos.Y >= ClientSize.Height - BORDER_SIZE) m.Result = (IntPtr)HTBOTTOM;
                else m.Result = (IntPtr)HTCLIENT;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (GraphicsPath path = new GraphicsPath())
            {
                int radius = 20;
                path.AddArc(0, 0, radius, radius, 180, 90);
                path.AddArc(Width - radius, 0, radius, radius, 270, 90);
                path.AddArc(Width - radius, Height - radius, radius, radius, 0, 90);
                path.AddArc(0, Height - radius, radius, radius, 90, 90);
                path.CloseFigure();
                
                e.Graphics.Clear(bgColor); // Убираем артефакты
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var border = new Pen(Color.FromArgb(49, 54, 77), 2);
                e.Graphics.DrawPath(border, path);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0) return;
            using var path = VisualTheme.Rounded(new Rectangle(0, 0, Width, Height), 18);
            var previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        // === ЛОГИКА ===
        private (List<GameInfo> Games,string? Notice)? preloadedLibrary;
        private void LoadData()
        {
                    (myGames, libraryNotice) = preloadedLibrary ?? GameLibraryStore.Load(dataFile);
                    preloadedLibrary=null;
                    int removed = myGames.RemoveAll(g => IsServiceExecutable(g.ExePath));
                    foreach (var game in myGames.Where(g => string.IsNullOrEmpty(g.SteamAppId)))
                    {
                        if (new[] { "game", "live", "content", "binaries", "bin", "win64", "win32" }
                            .Contains(game.Name, StringComparer.OrdinalIgnoreCase))
                            game.Name = GetLocalGameName(game.ExePath);
                        ConfigureValorant(game);
                    }
                    if (removed > 0)
                    {
                        try { SaveData(); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { libraryNotice = "Список очищен, но не удалось сохранить изменения: " + ex.Message; }
                    }
        }

        private void SaveData()
        {
            GameLibraryStore.Save(dataFile, myGames);
        }

        private void RefreshList()
        {
            var selected = SelectedGame;
            var checkedGames = listBoxGames.GetCheckedIndices().Where(i => i < visibleGames.Count)
                .Select(i => visibleGames[i]).ToHashSet();
            var oldIcons = visibleGames.Zip(listBoxGames.Items).ToDictionary(pair => pair.First, pair => pair.Second.IconImage);
            int version = ++iconLoadVersion;
            listBoxGames.Items.Clear();
            listBoxGames.ResetView();
            string query = searchGames.Text.Trim();
            var filtered = myGames.Where(g => (!pinnedOnly || g.IsPinned) && (!recentOnly || g.LastLaunchUtc.HasValue) &&
                g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            var ordered = filtered.OrderByDescending(g => g.IsPinned);
            visibleGames = recentOnly ? filtered.OrderByDescending(g => g.LastLaunchUtc).ToList() : sortGames.SelectedIndex switch {
                1 => ordered.ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                2 => ordered.ThenByDescending(g => g.LastLaunchUtc).ToList(),
                3 => ordered.ThenByDescending(g => g.DisplayPlayedSeconds).ToList(),
                _ => ordered.ToList()
            };
            sortGames.Enabled = !recentOnly;
            foreach (var game in visibleGames)
            {
                string source = !string.IsNullOrEmpty(game.SteamAppId) ? "Steam" :
                    game.Name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase) ? "Riot Games" : "Локальная игра";
                if (game.Performance != null) source += $"   ·   Цель {game.Performance.Copy().TargetFps} FPS";
                source += " · " + game.PlaytimeLabel;
                if (game.AchievementSummary != null) source += " · " + game.AchievementSummary;
                if (game.LastLaunchUtc.HasValue) source += $"   ·   Запуск {game.LastLaunchUtc.Value.ToLocalTime():dd.MM HH:mm}";
                listBoxGames.Items.Add(new CustomListItem(game.Name, checkedGames.Contains(game), source) {
                    IsPinned = game.IsPinned, IconImage = oldIcons.GetValueOrDefault(game) ?? iconCache.Peek(game) });
            }
            int pinnedCount = myGames.Count(g => g.IsPinned);
            btnLibrary.IsActive = !pinnedOnly && !recentOnly;
            btnPinned.IsActive = pinnedOnly;
            btnRecent.IsActive = recentOnly;
            btnPinned.Text = $"Любимые игры · {pinnedCount}";
            listBoxGames.EmptyTitle = pinnedOnly ? "Любимые игры всегда под рукой" : "Здесь начинается твоя коллекция";
            listBoxGames.EmptyHint = pinnedOnly ? "Нажми ☆ на карточке игры в библиотеке" : "Нажми «Найти игры» или добавь игру вручную";
            if (recentOnly) { listBoxGames.EmptyTitle = "Возвращайся к любимым мирам"; listBoxGames.EmptyHint = "Здесь появятся игры, запущенные через PLAYSPACE"; }
            if (query.Length > 0) { listBoxGames.EmptyTitle = "Ничего не найдено"; listBoxGames.EmptyHint = "Попробуй другое название или очисти поиск"; }
            lblStatus.Text = $"Игр: {myGames.Count}   ·   Закреплено: {pinnedCount}";
            libraryCount.Text = $"{(recentOnly ? "НЕДАВНИЕ" : pinnedOnly ? "ЗАКРЕПЛЁННЫЕ" : "МОЯ БИБЛИОТЕКА")}   /   {visibleGames.Count}";
            listBoxGames.SelectItem(selected == null ? -1 : visibleGames.IndexOf(selected));
            UpdateGameActions();
            if (IsHandleCreated) _ = LoadGameIconsAsync(listBoxGames, visibleGames.ToArray(), version);
        }

        private GameInfo? SelectedGame => listBoxGames.SelectedIndex >= 0 && listBoxGames.SelectedIndex < visibleGames.Count
            ? visibleGames[listBoxGames.SelectedIndex] : null;

        private void ShowLibrary(bool onlyPinned)
        {
            pinnedOnly = onlyPinned;
            recentOnly = false;
            RefreshList();
        }

        private void ShowRecentLibrary()
        {
            pinnedOnly = false;
            recentOnly = true;
            RefreshList();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { searchGames.Focus(); searchGames.SelectAll(); return true; }
            if (keyData == Keys.Escape) { if(searchGames.TextLength>0)searchGames.Clear();else ShowLibrary(false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void InitializeGameMenu()
        {
            gameMenu = new ContextMenuStrip {
                BackColor = panelColor, ForeColor = textColor, ShowImageMargin = false,
                Font = new Font("Segoe UI", 10), Padding = new Padding(6),
                Renderer = new ToolStripProfessionalRenderer(new GameMenuColors()) };
            var launch = new ToolStripMenuItem("▶  Играть");
            var pin = new ToolStripMenuItem("☆  Закрепить");
            var profile = new ToolStripMenuItem("⚙  Профиль производительности");
            var folder = new ToolStripMenuItem("Открыть папку игры");
            var copy = new ToolStripMenuItem("Скопировать путь запуска");
            foreach (var item in new[] { launch, pin, profile, folder, copy }) item.Padding = new Padding(10, 7, 10, 7);
            gameMenu.Items.AddRange(new ToolStripItem[] { launch, pin, profile, new ToolStripSeparator(), folder, copy });
            gameMenu.Opening += (s, e) =>
            {
                var game = SelectedGame;
                if (game == null) { e.Cancel = true; return; }
                launch.Enabled = !launchingGame;
                pin.Text = game.IsPinned ? "★  Открепить" : "☆  Закрепить";
                folder.Enabled = ResolveGameFolder(game) != null;
            };
            launch.Click += (s, e) => LaunchSelectedGame();
            pin.Click += (s, e) => TogglePinnedGame(listBoxGames.SelectedIndex);
            profile.Click += (s, e) => { if (SelectedGame is GameInfo game) OpenPerformance(game); };
            folder.Click += (s, e) =>
            {
                if (SelectedGame is not GameInfo game || ResolveGameFolder(game) is not string path) return;
                try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
                catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
                { lblStatus.Text = "Не удалось открыть папку: " + ex.Message; }
            };
            copy.Click += (s, e) =>
            {
                if (SelectedGame is not GameInfo game) return;
                try { Clipboard.SetText(game.ExePath); lblStatus.Text = "Путь запуска скопирован"; }
                catch (System.Runtime.InteropServices.ExternalException) { lblStatus.Text = "Буфер обмена занят. Попробуй ещё раз."; }
            };
            listBoxGames.ContextMenuRequested += location => gameMenu.Show(listBoxGames, location);
        }

        private static string? ResolveGameFolder(GameInfo game)
        {
            try
            {
                if (game.Name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase))
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "RiotClientInstalls.json");
                    if (File.Exists(path))
                    {
                        var clients = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path))["associated_client"] as Newtonsoft.Json.Linq.JObject;
                        string? location = clients?.Properties().FirstOrDefault(p => p.Name.Replace('\\', '/').TrimEnd('/')
                            .EndsWith("/VALORANT/live", StringComparison.OrdinalIgnoreCase))?.Name;
                        if (location != null && Directory.Exists(location)) return location;
                    }
                }
                if (!File.Exists(game.ExePath)) return null;
                string? folder = Path.GetDirectoryName(game.ExePath);
                return Directory.Exists(folder) ? folder : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException) { return null; }
        }

        private void OpenPerformance(GameInfo? game)
        {
            using var dialog = new PerformanceForm(myGames.ToArray(), game, SavePerformanceProfile);
            dialog.Icon = appIcon;
            dialog.ShowDialog(this);
            if (dialog.LaunchRequested is GameInfo gameToLaunch) LaunchGame(gameToLaunch);
        }

        private void OpenMods()
        {
            var library = myGames.ToArray();
            using var dialog = new ModsForm(() => Task.Run(() => FindSteamGames().Concat(FindEpicGames())
                .Concat(library.Where(g => File.Exists(g.ExePath))).ToList()));
            dialog.Icon = appIcon;
            dialog.ShowDialog(this);
        }

        private bool SavePerformanceProfile(GameInfo game, PerformanceProfile profile)
        {
            var previous = game.Performance;
            game.Performance = profile.Copy();
            try { SaveData(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                game.Performance = previous;
                MessageBox.Show(this, $"Не удалось сохранить профиль:\n{ex.Message}", "Профиль игры", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            RefreshList();
            return true;
        }

        private void TogglePinnedGame(int index)
        {
            if (index < 0 || index >= visibleGames.Count) return;
            var game = visibleGames[index];
            bool previous = game.IsPinned;
            game.IsPinned = !previous;
            try { SaveData(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                game.IsPinned = previous;
                lblStatus.Text = $"Не удалось сохранить закрепление: {ex.Message}";
                return;
            }
            RefreshList();
            lblStatus.Text = game.IsPinned ? $"Закреплено: {game.Name}" : $"Откреплено: {game.Name}";
        }

        private void UpdateGameActions()
        {
            var selected = SelectedGame;
            btnPin.Enabled = selected != null;
            btnPin.Text = selected?.IsPinned == true ? "★  Открепить" : "☆  Закрепить";
            btnPin.IsActive = selected?.IsPinned == true;
            btnLaunch.Enabled = selected != null && !launchingGame;
            btnSurprise.Enabled = true;
            UpdateSpotlight(selected);
        }

        private void SuggestGame()
        {
            var chosen = shuffle.Next(visibleGames, SelectedGame);
            if (chosen == null) return;
            listBoxGames.SelectItem(visibleGames.IndexOf(chosen));
            lblStatus.Text = $"Как насчёт «{chosen.Name}»? Нажми «Играть», когда будешь готов.";
        }

        private void UpdateSpotlight(GameInfo? game)
        {
            string detail = game == null ? "Все твои миры — в одной библиотеке" :
                !string.IsNullOrEmpty(game.SteamAppId) ? "Steam" : game.Name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase) ? "Riot Games" : "Локальная игра";
            if (game != null) detail += " · " + game.PlaytimeLabel + (game.AchievementSummary == null ? "" : " · " + game.AchievementSummary);
            if (game?.Performance != null) detail += $"   ·   Цель {game.Performance.Copy().TargetFps} FPS";
            if (game?.IsPinned == true) detail += "   ·   ★ Закреплено";
            hero.SetPresentation(game?.Name ?? "Время играть.", detail, game == null ? null :
                listBoxGames.Items.ElementAtOrDefault(listBoxGames.SelectedIndex)?.IconImage ?? iconCache.Peek(game), game != null);
            if (ReferenceEquals(spotlightGame, game)) return;
            spotlightGame = game;
            artworkCancellation?.Cancel();
            artworkCancellation?.Dispose();
            artworkCancellation = null;
            hero.SetArtwork(null);
            if (game == null || string.IsNullOrEmpty(game.SteamAppId) || !IsHandleCreated) return;
            artworkCancellation = new System.Threading.CancellationTokenSource();
            _ = LoadSpotlightArtworkAsync(game, artworkCancellation.Token);
        }

        private async Task LoadSpotlightArtworkAsync(GameInfo game, System.Threading.CancellationToken cancellation)
        {
            try
            {
                var image = await Task.Run(async () =>
                {
                    await artworkGate.WaitAsync(cancellation);
                    try { cancellation.ThrowIfCancellationRequested(); return GameArtworkLoader.Load(game); }
                    finally { artworkGate.Release(); }
                }, cancellation);
                if (IsDisposed || cancellation.IsCancellationRequested || !ReferenceEquals(spotlightGame, game)) image?.Dispose();
                else hero.SetArtwork(image);
            }
            catch (OperationCanceledException) { }
        }

        private async Task LoadGameIconsAsync(CustomCheckListBox list, GameInfo[] games, int? version = null)
        {
            var items = list.Items.ToArray();
            for (int i = 0; i < Math.Min(games.Length, items.Length); i++)
            {
                if (IsDisposed || list.IsDisposed || (version.HasValue && version != iconLoadVersion)) return;
                var image = await Task.Run(() => iconCache.Get(games[i]));
                if (IsDisposed || list.IsDisposed || (version.HasValue && version != iconLoadVersion)) return;
                items[i].IconImage = image;

                if (IsDisposed || list.IsDisposed) return;
                list.Invalidate();
                if (ReferenceEquals(list, listBoxGames) && list.SelectedIndex == i) UpdateSpotlight(SelectedGame);
            }
            if (ReferenceEquals(list,listBoxGames))
            for (int i=0;i<Math.Min(games.Length,items.Length);i++) {
                if (IsDisposed || list.IsDisposed || (version.HasValue && version != iconLoadVersion))return;
                items[i].PosterImage=await LoadPosterAsync(games[i]);
                if (IsDisposed || list.IsDisposed)return;
                list.Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ++iconLoadVersion;
                DisposeLibraryExtras();
                iconCache.Dispose();
                gameMenu?.Dispose();
                artworkCancellation?.Cancel();
                artworkCancellation?.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                appIcon?.Dispose();
                brandImage?.Dispose();
                supportIcon?.Dispose();
                foreach(var sticker in navigationStickers)sticker.Dispose();navigationStickers.Clear();
            }
        }

        private void AddGameManually()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Выберите .exe файл игры";
                ofd.Filter = "Исполняемые файлы (*.exe)|*.exe";
                
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string gameName = new DirectoryInfo(Path.GetDirectoryName(ofd.FileName)).Name;
                    if (myGames.Any(g => g.ExePath.Equals(ofd.FileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("Эта игра уже есть в списке!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    myGames.Add(new GameInfo { Name = gameName, ExePath = ofd.FileName });
                    SaveData();
                    RefreshList();
                    lblStatus.Text = $"Игра '{gameName}' добавлена вручную.";
                }
            }
        }

        private void LaunchSelectedGame()
        {
            if (SelectedGame is GameInfo game) LaunchGame(game);
        }

        private async void LaunchGame(GameInfo game)
        {
            if (launchingGame) return;
            launchingGame = true;
            btnLaunch.Enabled = false;
            
            try
            {
                string[] modArguments = UnrealModLayout.LaunchArguments(game).Concat(GamePerformanceController.Prepare(game)).ToArray();
                if (!string.IsNullOrEmpty(game.SteamAppId))
                {
                    lblStatus.Text = "Проверка клиента Steam...";

                    if (!IsSteamRunning())
                    {
                        string steamPath = GetSteamPath();
                        if (!string.IsNullOrEmpty(steamPath))
                            Process.Start(new ProcessStartInfo { FileName = steamPath, UseShellExecute = true });
                        else
                            Process.Start(new ProcessStartInfo { FileName = "steam://open/console", UseShellExecute = true });

                        lblStatus.Text = "Ожидание загрузки Steam...";
                        await Task.Delay(25000);
                        if (IsDisposed) return;
                    }
                    else
                    {
                        await Task.Delay(1000);
                        if (IsDisposed) return;
                    }

                    string? strategyExecutable = StrategyModsManager.DirectExecutable(game);
                    if (strategyExecutable != null)
                    {
                        Process.Start(new ProcessStartInfo { FileName = strategyExecutable,
                            WorkingDirectory = Path.GetDirectoryName(strategyExecutable), Arguments = game.LaunchArguments, UseShellExecute = true });
                    }
                    else if (modArguments.Length > 0)
                    {
                        string steam = GetSteamPath() ?? throw new FileNotFoundException("Для запуска UE4SS-модов нужен установленный Steam.exe.");
                        Process.Start(UnrealModLayout.SteamStart(steam, game.SteamAppId, modArguments));
                    }
                    else Process.Start(new ProcessStartInfo { FileName = $"steam://rungameid/{game.SteamAppId}", UseShellExecute = true });
                }
                else
                {
                    string executable = StrategyModsManager.DirectExecutable(game) ?? game.ExePath;
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = (game.LaunchArguments + " " + string.Join(" ", modArguments.Select(argument => "\"" + argument + "\""))).Trim(),
                        WorkingDirectory = Path.GetDirectoryName(executable),
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
                ArmTracking(game);
                if (RecordLaunch(game, DateTimeOffset.UtcNow)) lblStatus.Text = $"Запуск передан: {game.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при запуске:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                launchingGame = false;
                if (!IsDisposed) UpdateGameActions();
            }
        }

        private bool RecordLaunch(GameInfo game, DateTimeOffset time)
        {
            var previous = game.LastLaunchUtc;
            game.LastLaunchUtc = time.ToUniversalTime();
            try { SaveData(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                game.LastLaunchUtc = previous;
                lblStatus.Text = $"Запуск передан, но историю сохранить не удалось: {ex.Message}";
                return false;
            }
            RefreshList();
            return true;
        }

        private bool IsSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

        private string? GetSteamPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (key?.GetValue("SteamExe") is string executable && File.Exists(executable)) return executable;
            }
            catch { }
            foreach (string folder in GameIconCache.SteamFolders())
            {
                string path = Path.Combine(folder, "steam.exe");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private void DeleteCheckedGames()
        {
            var indices = listBoxGames.GetCheckedIndices();
            if (indices.Count == 0)
            {
                lblStatus.Text = "Отметьте галочками игры для удаления.";
                return;
            }

            var result = MessageBox.Show($"Удалить выбранные игры ({indices.Count} шт.) из списка?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                var gamesToRemove = indices.Select(i => visibleGames[i]).ToHashSet();
                myGames.RemoveAll(gamesToRemove.Contains);
                SaveData();
                RefreshList();
                lblStatus.Text = $"Удалено игр: {indices.Count}";
            }
        }

        private void ClearAllGames()
        {
            if (myGames.Count == 0) return;
            var result = MessageBox.Show("Удалить ВСЕ игры из списка?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                myGames.Clear();
                SaveData();
                RefreshList();
            }
        }

        private async void AutoScanGamesAsync()
        {
            btnScan.Enabled = false;
            scanProgress.Visible = true;
            btnScan.Text = "Поиск…";
            lblStatus.Text = "Сканирую... Это может занять до минуты.";
            Application.DoEvents();

            try
            {
            var foundGames = await Task.Run(() =>
            {
                var games = new List<GameInfo>();
                games.AddRange(FindSteamGames());
                games.AddRange(FindEpicGames());
                games.AddRange(FindRiotGames());
                games.AddRange(FindPirateGamesDeep());
                return games;
            });

            var uniqueGames = foundGames.GroupBy(g => g.ExePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Where(g => !myGames.Any(existing => string.Equals(existing.ExePath, g.ExePath, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(g.SteamAppId) && existing.SteamAppId == g.SteamAppId)))
                .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            btnScan.Enabled = true;

            if (uniqueGames.Count == 0) 
            { 
                lblStatus.Text = "Новых игр не найдено."; 
                return; 
            }
            
            ShowSelectionDialog(uniqueGames);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Ошибка поиска: {ex.Message}";
            }
            finally
            {
                btnScan.Enabled = true;
                btnScan.Text = "Найти игры";
                scanProgress.Visible = false;
            }
        }

        private List<GameInfo> FindEpicGames()
        {
            var games = new List<GameInfo>();
            string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            foreach (string file in EnumerateFilesSafe(manifests, "*.item", 0))
            {
                try
                {
                    var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                    if (manifest.Value<bool?>("bIsIncompleteInstall") == true) continue;
                    string? location = manifest.Value<string>("InstallLocation");
                    string? executable = manifest.Value<string>("LaunchExecutable");
                    if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(executable)) continue;
                    string path = Path.Combine(location, executable);
                    if (File.Exists(path)) games.Add(new GameInfo {
                        Name = manifest.Value<string>("DisplayName") ?? Path.GetFileName(location), ExePath = path, InstallDirectory = location });
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (JsonException) { }
            }
            return games;
        }

        private static string GetLocalGameName(string executable)
        {
            string name = Path.GetFileNameWithoutExtension(executable);
            if (name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("VALORANT-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) return "VALORANT";
            var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "game", "games", "live", "content", "binaries", "bin", "win32", "win64", "x64", "x86", "launcher" };
            var folder = Directory.GetParent(executable);
            if (folder == null) return name;
            if (!generic.Contains(folder.Name)) return folder.Name;
            if (!generic.Contains(name)) return Regex.Replace(name, "-Win(32|64)-Shipping$", "", RegexOptions.IgnoreCase);
            while (folder != null && generic.Contains(folder.Name)) folder = folder.Parent;
            return folder?.Name ?? name;
        }

        private static bool ConfigureValorant(GameInfo game)
        {
            if (!game.Name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase)) return false;
            string manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games", "RiotClientInstalls.json");
            try
            {
                if (!File.Exists(manifest)) return false;
                var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));
                var clients = data["associated_client"] as Newtonsoft.Json.Linq.JObject;
                string? client = clients?.Properties().FirstOrDefault(p =>
                    p.Name.Replace('\\', '/').TrimEnd('/').EndsWith("/VALORANT/live", StringComparison.OrdinalIgnoreCase))?.Value.ToString();
                client ??= data.Value<string>("rc_live") ?? data.Value<string>("rc_default");
                if (string.IsNullOrWhiteSpace(client) || !File.Exists(client)) return false;
                game.ExePath = client;
                game.LaunchArguments = "--launch-product=valorant --launch-patchline=live";
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (JsonException) { return false; }
        }

        private static List<GameInfo> FindRiotGames()
        {
            var games = new List<GameInfo>();
            string manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games", "RiotClientInstalls.json");
            try
            {
                if (!File.Exists(manifest)) return games;
                var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));
                var clients = data["associated_client"] as Newtonsoft.Json.Linq.JObject;
                foreach (var install in clients?.Properties() ?? Enumerable.Empty<Newtonsoft.Json.Linq.JProperty>())
                {
                    if (!install.Name.Replace('\\', '/').TrimEnd('/').EndsWith("/VALORANT/live", StringComparison.OrdinalIgnoreCase)) continue;
                    string executable = Path.Combine(install.Name, "VALORANT.exe");
                    if (!File.Exists(executable)) continue;
                    var game = new GameInfo { Name = "VALORANT", ExePath = executable };
                    if (ConfigureValorant(game)) games.Add(game);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
            return games;
        }

        private static IEnumerable<string> EnumerateFilesSafe(string folder, string pattern, int depth)
        {
            var options = new EnumerationOptions {
                RecurseSubdirectories = depth > 0, MaxRecursionDepth = depth,
                IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            try { return Directory.GetFiles(folder, pattern, options); }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        private List<GameInfo> FindSteamGames()
        {
            var steamGames = new List<GameInfo>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key == null) return steamGames;
                    string steamPath = key.GetValue("SteamPath")?.ToString();
                    if (string.IsNullOrEmpty(steamPath)) return steamGames;

                    var libraryFoldersFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    var steamAppsFolders = new List<string> { Path.Combine(steamPath, "steamapps") };

                    if (File.Exists(libraryFoldersFile))
                    {
                        string content = File.ReadAllText(libraryFoldersFile);
                        foreach (Match match in Regex.Matches(content, @"""path""\s+""([^""]+)"""))
                            steamAppsFolders.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
                    }

                    foreach (var appsPath in steamAppsFolders)
                    {
                        if (!Directory.Exists(appsPath)) continue;
                        foreach (var manifest in Directory.GetFiles(appsPath, "appmanifest_*.acf"))
                        {
                            try
                            {
                                string acfContent = File.ReadAllText(manifest);
                                var nameMatch = Regex.Match(acfContent, @"""name""\s+""([^""]+)""");
                                var dirMatch = Regex.Match(acfContent, @"""installdir""\s+""([^""]+)""");
                                var appIdMatch = Regex.Match(acfContent, @"""appid""\s+""([^""]+)""");

                                if (nameMatch.Success && dirMatch.Success && appIdMatch.Success)
                                {
                                    string gameName = nameMatch.Groups[1].Value;
                                    string appId = appIdMatch.Groups[1].Value;
                                    string installDirName = dirMatch.Groups[1].Value;
                                    
                                    if (steamGames.Any(g => g.SteamAppId == appId)) continue;
                                    if (gameName.Contains("Redistributables") || gameName.Contains("Steamworks")) continue;

                                    string gamePath = Path.Combine(appsPath, "common", installDirName);
                                    string mainExe = FindSteamExe(gamePath, installDirName);
                                    
                                    if (Directory.Exists(gamePath))
                                        steamGames.Add(new GameInfo { Name = gameName, ExePath = mainExe ?? $"steam://rungameid/{appId}", SteamAppId = appId, InstallDirectory = gamePath });
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return steamGames;
        }

        private string FindSteamExe(string gameFolder, string installDirName)
        {
            if (!Directory.Exists(gameFolder)) return null;
            try
            {
                var exes = EnumerateFilesSafe(gameFolder, "*.exe", 12);
                var validExes = exes.Where(e => 
                {
                    string filename = Path.GetFileNameWithoutExtension(e).ToLower();
                    return !filename.Contains("unins") && !filename.Contains("setup") && 
                           !filename.Contains("vcredist") && !filename.Contains("crashhandler") &&
                           !filename.Contains("dxsetup");
                }).ToList();

                if (validExes.Count == 0) return null;
                
                string cleanDirName = installDirName.Replace(" ", "").ToLower();
                var exactMatch = validExes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Replace(" ", "").ToLower().Contains(cleanDirName));
                if (exactMatch != null) return exactMatch;

                var rootExe = validExes.FirstOrDefault(e => Path.GetDirectoryName(e).Equals(gameFolder, StringComparison.OrdinalIgnoreCase));
                if (rootExe != null) return rootExe;

                return validExes.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            }
            catch { return null; }
        }

        private List<GameInfo> FindPirateGamesDeep()
        {
            var pirateGames = new List<GameInfo>();
            
            var priorityPaths = new List<string>();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                priorityPaths.Add(Path.Combine(drive.Name, "Games"));
                priorityPaths.Add(Path.Combine(drive.Name, "Игры"));
                priorityPaths.Add(drive.Name);
            }

            var ignoreFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "windows", "users", "programdata", "appdata", "$recycle.bin", 
                "system volume information", "perflogs", "msocache", 
                "steamapps",
                "nvidia", "amd", "intel", "microsoft", "windowsapps",
                "drivers", "common files",
                "redist", "support", "Riot Client", "VALORANT", "shareplay",
                
                "dism++", "qbittorrent", "hoyoplay", "ds4windows", "virtualbox",
                "$windows.~tmp", "$windows.~bt", "$windows.~ws"
            };

            foreach (var path in priorityPaths.Distinct())
            {
                if (Directory.Exists(path))
                {
                    SearchDirectoryRecursive(path, 0, 7, pirateGames, ignoreFolders);
                }
            }

            return pirateGames;
        }

        private void SearchDirectoryRecursive(string currentPath, int currentDepth, int maxDepth, List<GameInfo> results, HashSet<string> ignoreFolders)
        {
            if (currentDepth > maxDepth || !Directory.Exists(currentPath)) return;

            try
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0) return;
                string dirName = new DirectoryInfo(currentPath).Name;
                
                if (dirName.StartsWith("$")) return;
                if (currentDepth > 0 && ignoreFolders.Contains(dirName)) return;

                bool container = new[] { "Games", "Игры", "Program Files", "Program Files (x86)", "Epic Games", "Riot Games", "SteamLibrary", "Ubisoft", "Ubisoft Game Launcher", "Origin Games", "EA Games", "Battle.net" }.Contains(dirName, StringComparer.OrdinalIgnoreCase);
                
                bool gameLibrary = IsInsideGameLibrary(currentPath);
                bool unrealGame = Directory.Exists(Path.Combine(currentPath, "Engine")) &&
                    Directory.Exists(Path.Combine(currentPath, "Content"));
                if (!container && currentDepth > 0 && unrealGame)
                {
                    string potentialExe = FindPirateMainExe(currentPath, true);
                    if (potentialExe != null)
                    {
                        if (!results.Any(g => g.ExePath == potentialExe))
                        {
                            results.Add(new GameInfo { Name = GetLocalGameName(potentialExe), ExePath = potentialExe });
                        }
                        return; 
                    }
                }

                if (currentDepth > 0 && !container)
                {
                    string rootExe = FindPirateMainExe(currentPath, false);
                    if (rootExe != null && (gameLibrary || HasGameFiles(currentPath, rootExe)))
                    {
                        if (!results.Any(g => g.ExePath == rootExe))
                        {
                            results.Add(new GameInfo { Name = GetLocalGameName(rootExe), ExePath = rootExe });
                        }
                        return;
                    }
                }

                var subDirs = Directory.GetDirectories(currentPath);
                foreach (var dir in subDirs)
                {
                    string subDirName = new DirectoryInfo(dir).Name;
                    if (ignoreFolders.Contains(subDirName)) continue;
                    if (subDirName.StartsWith(".")) continue;
                    if (new[] { "Engine", "Binaries", "node_modules", "obj", "bin" }
                        .Contains(subDirName, StringComparer.OrdinalIgnoreCase)) continue;

                    SearchDirectoryRecursive(dir, currentDepth + 1, maxDepth, results, ignoreFolders);
                }
            }
            catch { }
        }

        private static bool IsInsideGameLibrary(string path)
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Games", "Игры", "GOG Games", "Epic Games", "Origin Games", "EA Games",
                "Riot Games", "Ubisoft Game Launcher", "Battle.net"
            };
            for (DirectoryInfo? folder = Directory.GetParent(path); folder != null; folder = folder.Parent)
                if (libraries.Contains(folder.Name)) return true;
            return false;
        }

        private static bool HasGameFiles(string folder, string executable)
        {
            string name = Path.GetFileNameWithoutExtension(executable);
            // Use engine-specific evidence rather than generic Content/Binaries folders or exe size.
            bool unity = File.Exists(Path.Combine(folder, "UnityPlayer.dll")) &&
                Directory.Exists(Path.Combine(folder, name + "_Data"));
            bool godot = File.Exists(Path.ChangeExtension(executable, ".pck"));
            bool rpgMaker = File.Exists(Path.Combine(folder, "Game.ini")) &&
                (Directory.Exists(Path.Combine(folder, "Data")) ||
                 File.Exists(Path.Combine(folder, "Game.rgss3a")) ||
                 File.Exists(Path.Combine(folder, "Game.rgss2a")));
            bool unreal = EnumerateFilesSafe(folder, "*.pak", 4)
                .Any(file => file.Contains(Path.DirectorySeparatorChar + "Content" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
            return unity || godot || rpgMaker || unreal;
        }

        private string FindPirateMainExe(string folderPath, bool deepSearch)
        {
            if (!Directory.Exists(folderPath)) return null;
            
            try
            {
                var exes = EnumerateFilesSafe(folderPath, "*.exe", deepSearch ? 8 : 0);
                
                var badWords = new[] { 
                    "unins", "setup", "install", "vcredist", "dxsetup", "crash", 
                    "updater", "report", "helper", "config",
                    "ds4", "virtualbox", "vbox", "driver", "service", "portable",
                    "uninstall", "readme",
                    "ubisoft", "uplay", "origin", "ea desktop", "battle.net",
                    "dism++", "qbittorrent", "launcher", "update", "editor", "ue4editor", "unrealeditor"
                };

                var validExes = exes.Where(e => 
                {
                    if (IsServiceExecutable(e)) return false;
                    string fn = Path.GetFileNameWithoutExtension(e).ToLower();
                    
                    if (badWords.Any(b => fn.Contains(b))) 
                        return false;
                    if (!IsInsideGameLibrary(folderPath) && !HasGameFiles(folderPath, e)) return false;
                        
                    return true;
                }).ToList();

                if (validExes.Count == 0) return null;
                
                return validExes.OrderByDescending(f => new FileInfo(f).Length).First();
            }
            catch { return null; }
        }

        private static bool IsServiceExecutable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return Path.GetFileName(path).Equals("SharePlayClient.exe", StringComparison.OrdinalIgnoreCase) ||
                (path.Replace('\\', '/').Contains("/Ubisoft Game Launcher/shareplay/", StringComparison.OrdinalIgnoreCase));
        }

        private void ShowSelectionDialog(List<GameInfo> foundGames)
        {
            using (var form = new Form())
            {
                form.Text = $"Найдено игр: {foundGames.Count}";
                form.Size = new Size(850, 600);
                form.StartPosition = FormStartPosition.CenterParent;
                form.BackColor = bgColor;
                form.ForeColor = textColor;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;

                var checkedList = new CustomCheckListBox
                {
                    Location = new Point(20, 20),
                    Size = new Size(800, 450),
                    BackColor = panelColor,
                    ForeColor = textColor
                };

                foreach (var game in foundGames)
                    checkedList.Items.Add(new CustomListItem($"[{(string.IsNullOrEmpty(game.SteamAppId) ? "Локальная" : "Steam")}] {game.Name}", true));

                var btnAdd = new ModernButton("Добавить в лаунчер", accentColor);
                btnAdd.Location = new Point(20, 490);
                btnAdd.Size = new Size(800, 50);
                btnAdd.Click += (s, e) =>
                {
                    var checkedIndices = checkedList.GetCheckedIndices();
                    foreach (var idx in checkedIndices)
                        myGames.Add(foundGames[idx]);
                    
                    SaveData();
                    RefreshList();
                    form.Close();
                };

                form.Controls.Add(checkedList);
                form.Controls.Add(btnAdd);
                form.Shown += async (s, e) => await LoadGameIconsAsync(checkedList, foundGames.ToArray());
                form.ShowDialog(this);
            }
        }
    }

}
