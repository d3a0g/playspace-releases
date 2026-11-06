using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

public partial class Form1
{
    private NotifyIcon? tray;
    private bool exiting;
    private readonly System.Windows.Forms.Timer sessionTimer = new() { Interval = 5000 };
    private readonly Dictionary<GameInfo, (DateTime Until, bool Seen)> sessions = new();
    private readonly Stopwatch sessionClock = new();
    private readonly Dictionary<string, Bitmap?> posters = new();
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
        InitializeUpdates();
        FormClosing += HandleCloseChoice;
        sessionTimer.Tick += (s,e) => TickSessions();
        sessionClock.Start(); sessionTimer.Start();
        Shown += async (s,e) => { LoadSteamSettings(); if (steamKey.Length > 0) await SyncLibraryAchievements(); };
        gameMenu!.Items.Add(new ToolStripSeparator());
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
    private void ArmTracking(GameInfo game) => sessions.TryAdd(game,(DateTime.UtcNow.AddMinutes(5),false));
    private bool sampling;
    private async void TickSessions()
    {
        if (sampling || IsDisposed) return;
        sampling = true;
        string[] paths;
        try { paths = await Task.Run(RunningPaths); }
        finally { sampling = false; }
        if (IsDisposed) return;
        if (steamKey.Length > 0 && syncedLibrary != AchievementLibrarySignature())
        {
            syncedLibrary = AchievementLibrarySignature();
            _ = SyncLibraryAchievements();
        }
        double seconds = Math.Min(10,sessionClock.Elapsed.TotalSeconds); sessionClock.Restart();
        bool changed = false;bool sessionEnded=false;
        foreach (var game in myGames.ToArray()) {
            bool running = MatchesGame(game,paths);
            if (running) {
                if (!sessions.TryGetValue(game,out var state)) state = (DateTime.UtcNow,true);
                if (state.Seen) { game.PlayedSeconds += seconds; changed = true; }
                else { game.LastLaunchUtc = DateTimeOffset.UtcNow; changed = true; }
                sessions[game] = (state.Until,true);
            } else if (sessions.TryGetValue(game,out var state) && (state.Seen || state.Until < DateTime.UtcNow)) {sessions.Remove(game);sessionEnded|=state.Seen;}
        }
        if(steamKey.Length>0 && (DateTimeOffset.UtcNow-lastSteamHoursAttempt>TimeSpan.FromMinutes(10) || sessionEnded && DateTimeOffset.UtcNow-lastSteamHoursAttempt>TimeSpan.FromSeconds(30))) _ = RefreshSteamHours();
        if (changed) {
            try { SaveData(); UpdateSpotlight(SelectedGame); }
            catch (IOException) { lblStatus.Text = "Не удалось сохранить время игры; повторим при следующей проверке."; }
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
    private async Task<Bitmap?> LoadPosterAsync(GameInfo game)
    {
        string id = game.SteamAppId ?? "";
        if (id.Length == 0 || !id.All(char.IsAsciiDigit)) return null;
        if (posters.TryGetValue(id,out var existing)) return existing;
        var image = await Task.Run(async () => {
            string folder = Path.Combine(appDataFolder,"posters");
            string file = Path.Combine(folder,id+".jpg");
            try {
                Directory.CreateDirectory(folder);
                if (!File.Exists(file)) {
                    using var response = await mediaClient.GetAsync($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/library_600x900_2x.jpg",HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 10*1024*1024) return null;
                    using var input = await response.Content.ReadAsStreamAsync(); using var buffer = new MemoryStream();
                    byte[] block = new byte[81920]; int n;
                    while ((n=await input.ReadAsync(block))>0) { if (buffer.Length+n>10*1024*1024) return null; buffer.Write(block,0,n); }
                    buffer.Position=0; using var validated = Image.FromStream(buffer);
                    using var resized = new Bitmap(validated,300,450); resized.Save(file,System.Drawing.Imaging.ImageFormat.Jpeg);
                }
                using var cached = Image.FromFile(file); return new Bitmap(cached);
            } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException) { return null; }
        });
        if (IsDisposed) { image?.Dispose(); return null; }
        if (posters.TryGetValue(id,out existing)) { image?.Dispose(); return existing; }
        posters[id]=image; return image;
    }
    private Task FetchAchievements() { OpenSettings(); return Task.CompletedTask; }
    private void DisposeLibraryExtras()
    {
        DisposeUpdates();
        sessionTimer.Stop();sessionTimer.Dispose();
        if (tray != null) { tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose(); }
        foreach (var image in posters.Values)image?.Dispose();posters.Clear();steamKey="";
    }
}
