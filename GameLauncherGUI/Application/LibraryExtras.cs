using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

public partial class Form1
{
    private NotifyIcon? tray;
    private bool exiting;
    private readonly System.Windows.Forms.Timer sessionTimer = new() { Interval = 5000 };
    // Owned by the UI thread, including continuations after awaited process sampling.
    private readonly Dictionary<GameInfo, (DateTime Until, bool Seen)> sessions = new();
    private readonly Stopwatch sessionClock = new();
    private readonly UiImageCache posters = new();
    private readonly int libraryThread=Environment.CurrentManagedThreadId;
    private UiTaskScope? uiTasks;
    private UiTaskScope Tasks=>uiTasks??=new(this,(operation,error)=>{
        LauncherSession.Record(appDataFolder,operation,error);
        if(!IsDisposed&&!resourcesDisposed)lblStatus.Text="Не удалось выполнить: "+operation+". Можно повторить действие.";
    });
    private static readonly HttpClient mediaClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string steamKey = "", steamUser = ""; // API key stays in memory only.

    private void InitializeLibraryExtras()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть PLAYSPACE", null, (s,e) => RestoreWindow());
        menu.Items.Add("Выход", null, (s,e) => { exiting = true; Close(); });
        tray = new NotifyIcon { Text = "PLAYSPACE", Icon = appIcon ?? SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (s,e) => RestoreWindow();
        preferences=LauncherPreferences.Load(appDataFolder);
        ApplyHomePreferences();
        InitializeWorkspace();
        ConfigureDesktopLibrary();
        InstallOperationStrip();
        LauncherSession.Record(appDataFolder,"Открыта библиотека");
        if(updateChecksEnabled&&!LauncherSession.SafeMode&&!preferences.OnboardingCompleted&&myGames.Count==0)
            Shown+=(s,e)=>BeginInvoke(new Action(()=>{if(!IsDisposed)FirstRun();}));
        InitializeUpdates();
        FormClosing += HandleCloseChoice;
        sessionTimer.Tick += async (s,e) => {
            try { await Tasks.RunAsync("Проверка игровых сессий",_=>TickSessionsAsync()); }
            catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or IOException or NotSupportedException){if(!IsDisposed)lblStatus.Text="Не удалось проверить процессы игр; повторим позже.";}
        };
        sessionClock.Start(); sessionTimer.Start();
        Shown += async (s,e) => { if(LauncherSession.SafeMode){lblStatus.Text="БЕЗОПАСНЫЙ РЕЖИМ · сеть и применение профилей отключены. Установленные моды в файлах игр не меняются.";return;}LoadSteamSettings(); if (steamKey.Length > 0) await Tasks.RunAsync("Синхронизация Steam",_=>SyncLibraryAchievements()); };
        gameMenu!.Items.Add(new ToolStripSeparator());
        gameMenu.Items.Add("Перенести игру на другой диск…",null,(s,e)=>OpenGameTransfer());
        gameMenu.Items.Add("Найти дубликаты в библиотеке…",null,(s,e)=>OpenDuplicates());
        gameMenu.Items.Add("Обновления игры…",null,(s,e)=>OpenGameUpdates());
        gameMenu.Items.Add("Помощь с запуском…",null,(s,e)=>OpenLastLaunchHelp());
        gameMenu.Items.Add("Своя иконка…", null, (s,e) => ChangeIcon(false));
        gameMenu.Items.Add("Сбросить иконку", null, (s,e) => ChangeIcon(true));
        gameMenu.Items.Add("Процесс для учёта времени…", null, (s,e) => {
            if (SelectedGame is not GameInfo game) return;
            using var dialog = new OpenFileDialog { Filter = "Процесс игры|*.exe", Title = "Выбери основной EXE игры, а не лаунчер" };
            if (dialog.ShowDialog(this) == DialogResult.OK) { game.TrackingExecutable = dialog.FileName; SaveData(); }
        });
        gameMenu.Items.Add("Достижения Steam…", null, async (s,e) => await FetchAchievements());
        gameMenu.Items.Add("Скриншоты Steam", null, (s,e) => {
            if (SelectedGame is not GameInfo game || string.IsNullOrEmpty(game.SteamAppId)) return;
            foreach (string steam in GameIconCache.SteamFolders()) {
                string users = Path.Combine(steam,"userdata");
                if (!Directory.Exists(users)) continue;
                foreach (string user in Directory.GetDirectories(users)) {
                    string folder = Path.Combine(user,"760","remote",game.SteamAppId,"screenshots");
                    if (Directory.Exists(folder)) { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); return; }
                }
            }
            lblStatus.Text = "Локальные скриншоты этой игры в Steam не найдены.";
        });
        CompactGameMenu();
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void ChangeIcon(bool reset)
    {
        if (SelectedGame is not GameInfo game) return;
        try {
            if (reset) game.CustomIconPath = null;
            else {
                using var dialog = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.ico;*.bmp" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using var original = Image.FromFile(dialog.FileName);
                using var image = new Bitmap(original,128,128);
                string folder = Path.Combine(appDataFolder,"custom-icons"); Directory.CreateDirectory(folder);
                string destination = Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
                image.Save(destination,System.Drawing.Imaging.ImageFormat.Png); game.CustomIconPath = destination;
            }
            SaveData(); RefreshList();
        } catch (Exception ex) { lblStatus.Text = "Не удалось сохранить иконку: " + ex.Message; }
    }
    private void VerifyLibraryThread(){if(Environment.CurrentManagedThreadId!=libraryThread)throw new InvalidOperationException("Session state must be accessed on the UI thread.");}
    private void ArmTracking(GameInfo game){VerifyLibraryThread();sessions.TryAdd(game,(DateTime.UtcNow.AddMinutes(5),false));}
    private bool sampling; // UI reentrancy guard, not a cross-thread mutex.
    internal Func<Task<string[]>> SessionProcessSampler=()=>Task.Run(RunningPaths);
    private async Task TickSessionsAsync()
    {
        VerifyLibraryThread();
        if (sampling || IsDisposed || resourcesDisposed) return;
        sampling = true;
        try
        {
            string[] paths = await SessionProcessSampler();
            VerifyLibraryThread();
            if (IsDisposed || resourcesDisposed) return;
            if (steamKey.Length > 0 && syncedLibrary != AchievementLibrarySignature())
            {
                Tasks.StartUnique("Синхронизация Steam",_=>SyncLibraryAchievements());
            }
            double seconds = Math.Min(10,sessionClock.Elapsed.TotalSeconds); sessionClock.Restart();
            bool changed = false;bool sessionEnded=false;
            var games=myGames.ToArray();
            var library=new HashSet<GameInfo>(games);
            // Snapshot keys before removing from the same dictionary; this is not thread synchronization.
            foreach(var removed in sessions.Keys.Where(game=>!library.Contains(game)).ToArray()){
                sessions.Remove(removed);
                sessionStarted.Remove(removed);
            }
            foreach (var game in games) {
                bool running = MatchesGame(game,paths);
                if (running) {
                    if(!sessionStarted.ContainsKey(game))sessionStarted[game]=DateTimeOffset.UtcNow;
                    // First observation is a new session, even when the game was launched elsewhere.
                    // Do not charge the preceding polling interval before the process was observed.
                    if (!sessions.TryGetValue(game,out var state)) state = (DateTime.UtcNow,false);
                    if (state.Seen) { game.PlayedSeconds += seconds; changed = true; }
                    else { game.LastLaunchUtc = DateTimeOffset.UtcNow; changed = true; }
                    sessions[game] = (state.Until,true);
                } else if (sessions.TryGetValue(game,out var state) && (state.Seen || state.Until < DateTime.UtcNow)) {sessions.Remove(game);sessionStarted.Remove(game);sessionEnded|=state.Seen;if(state.Seen)Workspace.For(appDataFolder).History.Add(game,"Сессии","Отслеживаемый процесс игры завершился");}
            }
            if(steamKey.Length>0 && (DateTimeOffset.UtcNow-lastSteamHoursAttempt>TimeSpan.FromMinutes(10) || sessionEnded && DateTimeOffset.UtcNow-lastSteamHoursAttempt>TimeSpan.FromSeconds(30))) Tasks.StartUnique("Обновление часов Steam",_=>RefreshSteamHours());
            UpdateGameMode(paths);
            foreach(var issue in launchAttempts.Observe(DateTimeOffset.UtcNow,g=>MatchesGame(g,paths),games))RecordLaunchIssue(issue);
            if (changed) {
                try { SaveData(); UpdateSpotlight(SelectedGame); }
                catch (IOException) { lblStatus.Text = "Не удалось сохранить время игры; повторим при следующей проверке."; }
            }
        }
        finally
        {
            sampling=false;
        }
    }
    private static string[] RunningPaths()
    {
        var paths = new List<string>();
        foreach (var process in Process.GetProcesses()) {
            using(process) try { if(process.MainModule?.FileName is string path)paths.Add(path); }
            catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        }
        return paths.ToArray();
    }
    private static bool IsGameRunning(GameInfo game) => MatchesGame(game,RunningPaths());
    private static bool MatchesGame(GameInfo game,IEnumerable<string> paths)
    {
        string? exe = !string.IsNullOrWhiteSpace(game.TrackingExecutable) ? game.TrackingExecutable :
            StrategyGameAdapter.For(game)?.FindExecutable(game) ?? (File.Exists(game.ExePath) ? game.ExePath : null);
        if (exe != null) {
            string name=Path.GetFileNameWithoutExtension(exe);
            if(new[]{"steam","epicgameslauncher","riotclientservices","dowser"}.Contains(name,StringComparer.OrdinalIgnoreCase))return false;
            return paths.Contains(Path.GetFullPath(exe),StringComparer.OrdinalIgnoreCase);
        }
        if (!Directory.Exists(game.InstallDirectory))return false;
        string prefix=Path.GetFullPath(game.InstallDirectory!).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        return paths.Any(path => path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) &&
            !new[]{"launcher","crash","helper","setup","update","redist","uninstall","dowser"}.Any(Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Contains));
    }
    private Task<Bitmap?> LoadPosterAsync(GameInfo game)
    {
        posters.VerifyAccess();
        if(LauncherSession.SafeMode||resourcesDisposed)return Task.FromResult<Bitmap?>(null);
        string id=game.SteamAppId??"";
        if(id.Length==0||!id.All(char.IsAsciiDigit))return Task.FromResult<Bitmap?>(null);
        bool offline=Workspace.For(appDataFolder).Offline;
        return posters.GetAsync(id,token=>PosterLoader.LoadAsync(appDataFolder,id,offline,token));
    }
    private Task FetchAchievements() { OpenSettings(); return Task.CompletedTask; }
    private void DisposeLibraryExtras()
    {
        DisposeUpdates();
        sessionTimer.Stop();sessionTimer.Dispose();
        if (tray != null) { tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose(); }
        posters.Dispose();steamKey="";
    }
}
