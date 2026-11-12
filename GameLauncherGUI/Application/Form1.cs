using GameLauncherGUI.Features.Library.Services;
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
    public partial class Form1 : Form
    {
        private string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncherPRO");
        private string dataFile;
        private List<GameInfo> myGames = new List<GameInfo>();

        // Цвета
        private Color bgColor = Color.FromArgb(13, 21, 40);
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
            Workspace.Use(appDataFolder);
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
            Shown+=(s,e)=>{UiLayout.Apply(this);UiLayout.Fit(this);UiMotion.Enter(this);};
            if (libraryNotice != null) lblStatus.Text = libraryNotice;
            Shown += async (s, e) => await Tasks.RunAsync("Загрузка иконок",_=>LoadGameIconsAsync(listBoxGames,visibleGames.ToArray(),iconLoadVersion));
            Shown += async (s,e)=>await Tasks.RunAsync("Расчёт размеров игр",_=>LoadDiskSizesAsync(iconLoadVersion));
            Disposed += (s,e)=>{if(!diskSizingStopped){diskSizingStopped=true;diskCancellation.Cancel();diskCancellation.Dispose();}};

            // Обработчик изменения размера, чтобы крестик всегда оставался в углу

        }

        private void InitializeUI()
        {
            SuspendLayout();
            Text = "LauncherPlaySpace";
            Font = new Font("Segoe UI", 10);
            var titleBar = new Panel { Name="WindowChrome",Dock = DockStyle.Top, Height = 56, Width = ClientSize.Width, BackColor = bgColor };
            MouseEventHandler drag = (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
            titleBar.MouseDown += drag;
            var brandIcon = new PictureBox { Image = brandImage, SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(26, 26), Location = new Point(25, 15) };
            brandIcon.MouseDown += drag;
            titleBar.Controls.Add(brandIcon);
            var brand = new SmoothLabel { Text = "PLAYSPACE", Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = textColor, AutoSize = true, Location = new Point(58, 17) };
            brand.MouseDown += drag;
            titleBar.Controls.Add(brand);
            var windowActions=new FlowLayoutPanel {Dock=DockStyle.Right,Width=150,Padding=new Padding(6,10,10,10),WrapContents=false,FlowDirection=FlowDirection.LeftToRight};
            SmoothLabel WindowAction(string text,string name)=>new(){Text=text,AccessibleName=name,Font=new Font("Segoe UI",15),ForeColor=Color.FromArgb(179,186,210),Size=new Size(40,34),Margin=new Padding(1,0,1,0),Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter,BackColor=bgColor};
            var minimize=WindowAction("−","Свернуть");var maximize=WindowAction("□","Развернуть");btnClose=WindowAction("×","Закрыть");
            minimize.Click+=(s,e)=>WindowState=FormWindowState.Minimized;maximize.Click+=(s,e)=>WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;btnClose.Click+=(s,e)=>Close();
            foreach(var button in new[]{minimize,maximize,btnClose}){button.MouseEnter+=(s,e)=>button.BackColor=Color.FromArgb(39,43,62);button.MouseLeave+=(s,e)=>button.BackColor=bgColor;windowActions.Controls.Add(button);}
            titleBar.Controls.Add(windowActions);MaximizedBounds=Screen.FromControl(this).WorkingArea;
            Controls.Add(titleBar);

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(20, 8, 20, 18), BackColor = bgColor };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 14,
                BackColor = bgColor, Padding = new Padding(4, 10, 20, 0), Margin = Padding.Empty };
            foreach (float height in new[] { 24F,42F,42F,42F,42F,30F,42F,42F,42F,42F }) sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            sidebar.Controls.Add(new SmoothLabel { Text = "Игры", Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(113, 120, 149) }, 0, 0);
            btnLibrary = new ModernButton("Мои игры", Color.FromArgb(31, 35, 51)) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            btnLibrary.Click += (s, e) => ShowLibrary(false);
            sidebar.Controls.Add(btnLibrary, 0, 1);
            btnRecent = new ModernButton("Недавно играл", Color.FromArgb(31, 35, 51)) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            btnRecent.Click += (s, e) => ShowRecentLibrary();
            sidebar.Controls.Add(btnRecent, 0, 2);
            sidebar.Controls.Add(new SmoothLabel { Text = "Управление", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.FromArgb(113, 120, 149), TextAlign = ContentAlignment.BottomLeft }, 0, 5);
            btnScan = new ModernButton("Найти игры", Color.FromArgb(35, 40, 63)) { Dock = DockStyle.Fill, Margin = new Padding(8, 4, 0, 6) };
            btnScan.Click += (s, e) => AutoScanGamesAsync();
            btnPinned = new ModernButton("Любимые игры", Color.FromArgb(31, 35, 51)) { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            btnPinned.Click += (s, e) => ShowLibrary(true);
            sidebar.Controls.Add(btnPinned, 0, 3);
            btnAddManual = new ModernButton("+  Добавить игру", panelColor) { Dock = DockStyle.Fill, Margin = new Padding(8, 4, 0, 6) };
            var addMenu=new ContextMenuStrip{BackColor=panelColor,ForeColor=textColor,ShowImageMargin=false,Renderer=new ToolStripProfessionalRenderer(new GameMenuColors()),Font=new Font("Segoe UI",10)};addMenu.Items.Add("Найти установленные игры",null,(s,e)=>AutoScanGamesAsync());addMenu.Items.Add("Добавить EXE вручную…",null,(s,e)=>AddGameManually());btnAddManual.Text="Добавить ▾";btnAddManual.Click+=(s,e)=>StudioMenus.Show(addMenu,btnAddManual);Disposed+=(s,e)=>addMenu.Dispose();
            var performance = new ModernButton("Графика и FPS", Color.FromArgb(35, 40, 63)) {
                Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            performance.Click += (s, e) => OpenPerformance(SelectedGame);
            sidebar.Controls.Add(performance, 0, 6);
            var mods = new ModernButton("Моды", Color.FromArgb(35, 40, 63)) {
                Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8) };
            mods.Click += (s, e) => OpenMods();
            sidebar.Controls.Add(mods, 0, 7);
            btnSurprise = new ModernButton("Открыть новое", Color.FromArgb(49, 38, 77)) {
                Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8) };
            btnSurprise.Click += (s, e) => OpenRecommendations();
            sidebar.Controls.Add(btnSurprise, 0, 4);
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
            var network=new ModernButton("Сеть / WARP",Color.FromArgb(28,44,55)){Dock=DockStyle.Fill,Margin=new Padding(0,4,0,8),AccessibleName="Сеть и подключение WARP"};
            var workspaceButton=new ModernButton("Моё пространство",Color.FromArgb(39,37,64)){Dock=DockStyle.Fill,Margin=new Padding(0,4,0,8),AccessibleName="Моё пространство · загрузки, коллекции и оформление"};workspaceButton.Click+=(s,e)=>OpenWorkspace();sidebar.Controls.Add(workspaceButton,0,8);
            network.Click+=(s,e)=>OpenNetwork();sidebar.Controls.Add(network,0,9);
            sidebar.Controls.Add(support, 0, 12);
            var settings = new ModernButton("Настройки", Color.FromArgb(31,35,51)) { Dock = DockStyle.Fill, Margin = new Padding(0,4,0,8) }; settings.Click += (s,e) => OpenSettings(); sidebar.Controls.Add(settings,0,11);
            foreach(var item in new[]{(performance,"performance"),(mods,"mods"),(workspaceButton,"workspace"),(network,"network"),(settings,"settings")}){var image=NavigationSticker.Create(item.Item2);navigationStickers.Add(image);item.Item1.Image=image;item.Item1.Font=new Font("Segoe UI",9,FontStyle.Bold);}
            sidebar.Controls.Add(new SmoothLabel { Text = "Ctrl+K · поиск и действия", Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(113, 218, 193), Font = new Font("Segoe UI", 8, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft }, 0, 13);
            foreach(var nav in sidebar.Controls.OfType<ModernButton>()){nav.IsNavigation=true;nav.Margin=new Padding(0,2,0,2);nav.Font=new Font("Segoe UI",10); }
            shell.Controls.Add(sidebar, 0, 0);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            hero = new LibraryHero { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            hero.PlayRequested += (s, e) => LaunchSelectedGame();
            content.Controls.Add(hero, 0, 0);
            libraryCount = new SmoothLabel { Text = "МОЯ БИБЛИОТЕКА", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(161, 168, 193),
                Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            var libraryHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            libraryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            libraryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            libraryHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            libraryHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sortGames = new ModernSelect {
                BackColor = panelColor, ForeColor = textColor, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4), Font = new Font("Segoe UI", 9) };
            sortGames.Items.AddRange(new object[] { "Порядок добавления", "Название: А–Я", "Последние запуски", "Больше времени в игре" });
            sortGames.SelectedIndex = 0;
            sortGames.SelectedIndexChanged += (s, e) => RefreshList();
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
            searchGames=new TextBox {PlaceholderText="Найти игру · Ctrl+F"};
            btnClearSearch=new ModernButton("×",panelColor){Enabled=false};
            var searchPanel=new ModernSearchField(searchGames,btnClearSearch){Dock=DockStyle.Fill,Margin=new Padding(0,4,0,6)};
            searchGames.TextChanged+=(s,e)=>{btnClearSearch.Enabled=searchGames.TextLength>0;RefreshList();};
            tools.Controls.Add(searchPanel, 0, 0);
            tools.Controls.Add(btnScan, 1, 0);
            tools.Controls.Add(btnAddManual, 2, 0);
            var headingTools = new TableLayoutPanel { Dock = DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty };
            headingTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,76));headingTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));headingTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));headingTools.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var viewToggle = new ModernButton("▦ / ☰", panelColor) { Dock=DockStyle.Fill,Margin=new Padding(0,4,8,4),AccessibleName="Переключить список и постеры" };
            viewToggle.Click += (s,e) => { int selected = listBoxGames.SelectedIndex; listBoxGames.PosterView = !listBoxGames.PosterView; RefreshList(); };
            headingTools.Controls.Add(viewToggle,0,0);libraryCount.AutoSize=false;libraryCount.AutoEllipsis=true;libraryCount.Margin=new Padding(4,0,8,0);headingTools.Controls.Add(libraryCount,1,0);collectionFilter.Dock=DockStyle.Fill;collectionFilter.Margin=new Padding(0,4,8,4);headingTools.Controls.Add(collectionFilter,2,0);
            libraryHeader.Controls.Add(headingTools, 0, 0);
            libraryHeader.Controls.Add(sortGames, 1, 0);
            libraryHeader.Controls.Add(tools, 0, 1);
            libraryHeader.SetColumnSpan(tools, 2);
            content.Controls.Add(libraryHeader, 0, 1);
            listBoxGames = new CustomCheckListBox { Dock = DockStyle.Fill, BackColor = bgColor,
                ForeColor = textColor, Margin = Padding.Empty, Padding = new Padding(4, 8, 4, 8) };
            listBoxGames.ItemDoubleClick += (s, e) => LaunchSelectedGame();
            listBoxGames.ShowPinButtons = true;
            listBoxGames.ShowProfileButtons = false;
            listBoxGames.PinToggleRequested += TogglePinnedGame;
            listBoxGames.ProfileRequested += index => { if (index >= 0 && index < visibleGames.Count) OpenGameActions(visibleGames[index]); };
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
            actions.ColumnStyles[0].SizeType=SizeType.Absolute;actions.ColumnStyles[0].Width=180;actions.ColumnStyles[1].SizeType=SizeType.Absolute;actions.ColumnStyles[1].Width=150;actions.ColumnStyles[2].SizeType=SizeType.Absolute;actions.ColumnStyles[2].Width=110;actions.ColumnStyles[3].SizeType=SizeType.Percent;actions.ColumnStyles[3].Width=100;actions.Controls.Remove(btnDeleteChecked);actions.Controls.Remove(btnClearAll);btnDeleteChecked.Visible=false;btnClearAll.Visible=false;Controls.Add(btnDeleteChecked);Controls.Add(btnClearAll);
            var more=new ModernButton("Ещё ▾",panelColor){Dock=DockStyle.Fill,Margin=new Padding(0,12,0,4)};var moreMenu=new ContextMenuStrip{BackColor=panelColor,ForeColor=textColor,ShowImageMargin=false,Renderer=new ToolStripProfessionalRenderer(new GameMenuColors()),Font=new Font("Segoe UI",10)};moreMenu.Items.Add("Убрать выбранные из библиотеки",null,(s,e)=>DeleteCheckedGames());moreMenu.Items.Add("Очистить библиотеку…",null,(s,e)=>ClearAllGames());more.Click+=(s,e)=>StudioMenus.Show(moreMenu,more,true);Disposed+=(s,e)=>moreMenu.Dispose();actions.Controls.Add(more,2,0);
            content.Controls.Add(actions, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            lblStatus = new SmoothLabel { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(133, 143, 171), Font = new Font("Segoe UI", 9),
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
                    int removed = myGames.RemoveAll(g => GameDiscovery.IsServiceExecutable(g.ExePath));
                    foreach (var game in myGames.Where(g => string.IsNullOrEmpty(g.SteamAppId)))
                    {
                        if (new[] { "game", "live", "content", "binaries", "bin", "win64", "win32" }
                            .Contains(game.Name, StringComparer.OrdinalIgnoreCase))
                            game.Name = GameDiscovery.GetLocalGameName(game.ExePath);
                        GameDiscovery.ConfigureValorant(game);
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
            if(recoveryApplied)return;
            GameLibraryStore.Save(dataFile, myGames);
        }

        private void RefreshList()
        {
            var selected = SelectedGame;
            if(lastLibraryView!=null)libraryBrowse.Save(lastLibraryView,selected?.LibraryId,listBoxGames.ViewportOffset);
            string viewKey=LibraryViewKey;var position=libraryBrowse.Read(viewKey);lastLibraryView=viewKey;
            var checkedGames = listBoxGames.GetCheckedIndices().Where(i => i < visibleGames.Count)
                .Select(i => visibleGames[i]).ToHashSet();
            var oldIcons = visibleGames.Zip(listBoxGames.Items).ToDictionary(pair => pair.First, pair => pair.Second.IconImage);
            int version = ++iconLoadVersion;
            listBoxGames.Items.Clear();
            listBoxGames.ResetView();
            string query = searchGames.Text.Trim();
            var filtered = myGames.Where(g => (!pinnedOnly || g.IsPinned) && (!recentOnly || g.LastLaunchUtc.HasValue) &&
                (librarySource.SelectedIndex<=0||GameUpdates.Client(g)==librarySource.SelectedItem?.ToString())&&
                g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) && (collectionFilter.SelectedIndex<=0||Workspace.For(appDataFolder).Collections.Contains(g,collectionFilter.SelectedItem!.ToString()!)));
            var ordered = filtered.OrderByDescending(g => g.IsPinned);
            visibleGames = recentOnly ? filtered.OrderByDescending(g => g.LastLaunchUtc).ToList() : sortGames.SelectedIndex switch {
                1 => ordered.ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                2 => ordered.ThenByDescending(g => g.LastLaunchUtc).ToList(),
                3 => ordered.ThenByDescending(g => g.DisplayPlayedSeconds).ToList(),
                _ => ordered.ToList()
            };
            sortGames.Enabled = !recentOnly;
            if(desktopGridButton!=null)desktopGridButton.IsActive=listBoxGames.PosterView;
            if(desktopListButton!=null)desktopListButton.IsActive=!listBoxGames.PosterView;
            foreach (var game in visibleGames)
            {
                string source = CardSubtitle(game);
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
            libraryCount.Text = $"{(recentOnly ? "Недавние" : pinnedOnly ? "Избранное" : "Библиотека")} · {visibleGames.Count}";
            listBoxGames.SelectItem(selected == null ? -1 : visibleGames.IndexOf(selected));
            if(position!=null)listBoxGames.RestoreViewport(visibleGames.FindIndex(g=>g.LibraryId==position.SelectedId),position.Offset);
            UpdateGameActions();
            if (IsHandleCreated) Tasks.Start("Загрузка иконок",_=>LoadGameIconsAsync(listBoxGames, visibleGames.ToArray(), version));
            if (IsHandleCreated&&!IsDisposed) Tasks.Start("Расчёт размеров игр",_=>LoadDiskSizesAsync(version));
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
            if(keyData==(Keys.Control|Keys.K)){OpenPalette();return true;}
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
            gameMenu.Items.Insert(1,new ToolStripMenuItem("Управление игрой · копии и запуск…",null,(s,e)=>{if(SelectedGame is GameInfo game)OpenGameCare(game);}));
            gameMenu.Items.Insert(2,new ToolStripMenuItem("Моды и наборы этой игры…",null,(s,e)=>{if(SelectedGame is GameInfo game)OpenModsFor(game);}));
            gameMenu.Items.Insert(3,new ToolStripMenuItem("Проверить готовность к запуску…",null,(s,e)=>{if(SelectedGame is GameInfo game)CheckGameReady(game);}));
            var ubisoftCheck = new ToolStripMenuItem("Ubisoft Connect · проверять вход перед запуском") { CheckOnClick = true };
            gameMenu.Items.Add(ubisoftCheck);
            ubisoftCheck.Click += (s,e) => { if(SelectedGame is GameInfo game){game.CheckUbisoftBeforeLaunch=ubisoftCheck.Checked;SaveData();} };
            gameMenu.Opening += (s, e) =>
            {
                var game = SelectedGame;
                if (game == null) { e.Cancel = true; return; }
                launch.Enabled = !launchingGame;
                ubisoftCheck.Checked = UbisoftLaunch.NeedsCheck(game);
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
            if(LauncherSession.SafeMode){lblStatus.Text="Моды недоступны в безопасном режиме.";return;}
            var library = myGames.ToArray();
            using var dialog = new ModsForm(() => Task.Run(() => GameDiscovery.FindSteamGames().Concat(GameDiscovery.FindEpicGames())
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
            RefreshList(); Workspace.For(appDataFolder).History.Add(game,"Настройки",$"Профиль: цель {game.Performance.TargetFps} FPS; {game.Performance.WindowMode}; качество {game.Performance.Quality}; применение при запуске: {game.Performance.ApplyOnLaunch}");
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
            string detail = game == null ? "Все твои миры — в одной библиотеке" : GameCardText.Summary(game,SizeLabel(game));
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
            var artworkToken=artworkCancellation.Token;
            Tasks.Start("Загрузка фона",_=>LoadSpotlightArtworkAsync(game,artworkToken));
        }

        private async Task LoadSpotlightArtworkAsync(GameInfo game, System.Threading.CancellationToken cancellation)
        {
            string artworkId=game.SteamAppId??"";bool offline=Workspace.For(appDataFolder).Offline;
            try
            {
                var image = await Task.Run(async () =>
                {
                    await artworkGate.WaitAsync(cancellation);
                    try { cancellation.ThrowIfCancellationRequested(); return await GameArtworkLoader.LoadAsync(artworkId,appDataFolder,offline,cancellation).ConfigureAwait(false); }
                    finally { artworkGate.Release(); }
                }, cancellation);
                if (IsDisposed || cancellation.IsCancellationRequested || !ReferenceEquals(spotlightGame, game)) image?.Dispose();
                else hero.SetArtwork(image);
            }
            catch (OperationCanceledException) { }
        }

        private async Task LoadGameIconsAsync(CustomCheckListBox list, GameInfo[] games, int? version = null)
        {
            if(LauncherSession.SafeMode)return;
            var items = list.Items.ToArray();
            for (int i = 0; i < Math.Min(games.Length, items.Length); i++)
            {
                if (IsDisposed || list.IsDisposed || (version.HasValue && version != iconLoadVersion)) return;
                var source=games[i];var snapshot=new GameInfo{Name=source.Name,SteamAppId=source.SteamAppId,ExePath=source.ExePath,CustomIconPath=source.CustomIconPath};
                Bitmap? image=null;
                try{image=await Task.Run(()=>iconCache.Get(snapshot));}
                catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException){/* An unreadable icon must not stop the remaining artwork queue. */}
                if (IsDisposed || list.IsDisposed || (version.HasValue && version != iconLoadVersion)) return;
                items[i].IconImage = image;

                if (IsDisposed || list.IsDisposed) return;
                list.Invalidate();
                if (ReferenceEquals(list, listBoxGames) && list.SelectedIndex == i) UpdateSpotlight(SelectedGame);
            }
            if (ReferenceEquals(list,listBoxGames))
            for (int i=0;i<Math.Min(games.Length,items.Length);i++) {
                if (IsDisposed || list.IsDisposed || (version.HasValue && version != iconLoadVersion))return;
                var poster=await (list.LandscapePosters?LoadWidePosterAsync(games[i]):LoadPosterAsync(games[i]));
                if (IsDisposed || list.IsDisposed || (version.HasValue && version != iconLoadVersion))return;
                items[i].PosterImage=poster;
                list.Invalidate();
            }
        }

        private bool resourcesDisposed;
        protected override void Dispose(bool disposing)
        {
            bool release=disposing&&!resourcesDisposed;
            if (release)
            {
                resourcesDisposed=true;
                uiTasks?.Dispose();
                Workspace.For(appDataFolder).Transfers.CancelAll();
                ++iconLoadVersion;
                DisposeLibraryExtras();
                iconCache.Dispose();
                gameMenu?.Dispose();
                artworkCancellation?.Cancel();
                artworkCancellation?.Dispose();
            }
            base.Dispose(disposing);
            if (release)
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

        private void LaunchGame(GameInfo game)=>Tasks.Start("Запуск игры",_=>LaunchGameAsync(game));
        private async Task LaunchGameAsync(GameInfo game,bool withoutPreparation=false)
        {
            if(recoveryApplied)return;
            if (launchingGame) return;
            launchingGame = true;
            btnLaunch.Enabled = false;
            StartupSplash? launchProgress = null;
            bool directProcessObserved=false;
            
            try
            {
                LauncherSession.Record(appDataFolder,"Проверка перед запуском игры");
                var readiness=await Task.Run(()=>GameReadiness.Inspect(game,()=>IsGameRunning(game),appDataFolder,prepare:!withoutPreparation));
                if(IsDisposed)return;
                var blockers=readiness.Where(item=>item.Blocking).ToArray();if(blockers.Length>0){MessageBox.Show(this,string.Join("\n\n",blockers.Select(item=>item.Name+": "+item.Detail)),"Перед запуском нужно исправить",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
                string[] customArguments=LauncherSession.SafeMode?Array.Empty<string>():GameReadiness.Arguments(game.LaunchArguments);
                if(!LauncherSession.SafeMode&&UbisoftLaunch.NeedsCheck(game))
                {
                    using var login=new UbisoftLaunchForm(game.Name);
                    if(login.ShowDialog(this)!=DialogResult.OK)return;
                }
                if(!LauncherSession.SafeMode&&game.Care?.BackupBeforeLaunch==true){lblStatus.Text="Создаём резервную копию перед запуском…";await Task.Run(()=>new GameBackupStore(game.Care,appDataFolder,()=>IsGameRunning(game)).Create("Перед запуском"));Workspace.For(appDataFolder).History.Add(game,"Копии","Создана резервная копия перед запуском");if(IsDisposed)return;}
                string[] modArguments = LaunchPreparation.Arguments(game,customArguments,withoutPreparation,LauncherSession.SafeMode);
                if (!string.IsNullOrEmpty(game.SteamAppId))
                {
                    lblStatus.Text = "Проверка клиента Steam...";
                    launchProgress = new StartupSplash("Подготавливаем запуск") { ShowInTaskbar=false, StartPosition=FormStartPosition.Manual };
                    launchProgress.Location = new Point(Left+(Width-launchProgress.Width)/2,Top+(Height-launchProgress.Height)/2);
                    launchProgress.Report("Проверяем Steam · "+game.Name);
                    launchProgress.Show(this);

                    if (!IsSteamRunning())
                    {
                        string steamPath = GetSteamPath();
                        if (!string.IsNullOrEmpty(steamPath))
                            Process.Start(new ProcessStartInfo { FileName = steamPath, UseShellExecute = true });
                        else
                            Process.Start(new ProcessStartInfo { FileName = "steam://open/console", UseShellExecute = true });

                        lblStatus.Text = "Ожидание загрузки Steam...";
                        launchProgress.Report("Ожидание загрузки Steam…");
                        await Task.Delay(25000,Tasks.Token);
                        if (IsDisposed) return;
                    }
                    else
                    {
                        await Task.Delay(1000,Tasks.Token);
                        if (IsDisposed) return;
                    }

                    string? strategyExecutable = LauncherSession.SafeMode||withoutPreparation?null:StrategyModsManager.DirectExecutable(game);
                    launchProgress.Close();launchProgress.Dispose();launchProgress=null;
                    if (strategyExecutable != null)
                    {
                        var strategyStart=new ProcessStartInfo { FileName = strategyExecutable,WorkingDirectory = Path.GetDirectoryName(strategyExecutable),UseShellExecute = true };
                        foreach(string argument in modArguments)strategyStart.ArgumentList.Add(argument);
                        using var process=Process.Start(strategyStart);directProcessObserved=process!=null&&MatchesGame(game,new[]{strategyExecutable});
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
                    string executable = (LauncherSession.SafeMode||withoutPreparation?null:StrategyModsManager.DirectExecutable(game)) ?? game.ExePath;
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        WorkingDirectory = Path.GetDirectoryName(executable),
                        UseShellExecute = true
                    };
                    foreach(string argument in modArguments)startInfo.ArgumentList.Add(argument);
                    using var process=Process.Start(startInfo);directProcessObserved=process!=null&&MatchesGame(game,new[]{executable});
                }
                ArmTracking(game);
                launchAttempts.Arm(game,DateTimeOffset.UtcNow,directProcessObserved);
                EnterGameMode(game);
                if (RecordLaunch(game, DateTimeOffset.UtcNow)) lblStatus.Text = $"Запуск передан: {game.Name}";
            }
            catch(OperationCanceledException)when(Tasks.Token.IsCancellationRequested){}
            catch (Exception ex)
            {
                if(IsDisposed||resourcesDisposed)return;
                LauncherSession.Record(appDataFolder,"Ошибка запуска игры",ex);
                RecordLaunchIssue(new(game.LibraryId,game.Name,DateTimeOffset.UtcNow,LaunchIssueKind.StartError,"Не удалось передать запуск игре или клиенту. Откройте клиент и проверьте установку. Подробности записаны в локальном журнале лаунчера."));
            }
            finally
            {
                launchProgress?.Close();launchProgress?.Dispose();
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
            Workspace.For(appDataFolder).History.Add(game,"Запуски","Запуск передан игровому клиенту или EXE"); return true;
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

        private void AutoScanGamesAsync()=>Tasks.StartUnique("Поиск игр",_=>ScanGamesAsync());
        private async Task ScanGamesAsync()
        {
            if(!btnScan.Enabled||recoveryApplied)return;
            LauncherSession.Record(appDataFolder,"Начат поиск установленных игр");
            btnScan.Enabled = false;
            scanProgress.Visible = true;
            btnScan.Text = "Поиск…";
            lblStatus.Text = "Сканирую... Это может занять до минуты.";


            try
            {
            var foundGames = await Task.Run(GameDiscovery.Scan);

            if(IsDisposed||resourcesDisposed||Tasks.Token.IsCancellationRequested)return;
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
                if(!IsDisposed&&!resourcesDisposed)lblStatus.Text = $"Ошибка поиска: {ex.Message}";
            }
            finally
            {
                if(!IsDisposed&&!resourcesDisposed){btnScan.Enabled = true;btnScan.Text = "Найти игры";scanProgress.Visible = false;}
            }
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
