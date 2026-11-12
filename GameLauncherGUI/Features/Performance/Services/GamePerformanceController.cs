using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GameLauncherGUI;

internal enum PerformanceBackend { None, Unity, Unreal, Source2, Cyberpunk }

internal static class GamePerformanceController
{
    public static PerformanceBackend Backend(GameInfo game)
    {
        if(game.SteamAppId=="1091500"||Path.GetFileName(game.ExePath).Equals("Cyberpunk2077.exe",StringComparison.OrdinalIgnoreCase))return PerformanceBackend.Cyberpunk;
        if (game.SteamAppId == "730") return PerformanceBackend.Source2;
        var adapter = ModGameAdapter.For(game);
        if (adapter != null) return adapter.IsUnreal ? PerformanceBackend.Unreal : PerformanceBackend.Unity;
        string? folder = Path.GetDirectoryName(game.ExePath);
        if (folder != null && File.Exists(Path.Combine(folder, "UnityPlayer.dll"))) return PerformanceBackend.Unity;
        if(Path.GetFileNameWithoutExtension(game.ExePath).EndsWith("-Win64-Shipping",StringComparison.OrdinalIgnoreCase)||Directory.Exists(Path.Combine(game.InstallDirectory??folder??"", "Engine")))return PerformanceBackend.Unreal;
        return PerformanceBackend.None;
    }

    public static string? Config(GameInfo game, PerformanceProfile profile)
    {
        if(Backend(game)==PerformanceBackend.Cyberpunk){string candidate=profile.ConfigFile??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CD Projekt Red","Cyberpunk 2077","UserSettings.json");return File.Exists(candidate)&&Path.GetFileName(candidate).Equals("UserSettings.json",StringComparison.OrdinalIgnoreCase)?Path.GetFullPath(candidate):null;}
        if (Backend(game) != PerformanceBackend.Unreal) return null;
        if (!string.IsNullOrWhiteSpace(profile.ConfigFile) && Path.GetFileName(profile.ConfigFile).Equals("GameUserSettings.ini", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(profile.ConfigFile)) return Path.GetFullPath(profile.ConfigFile);
        var adapter = ModGameAdapter.For(game);
        var projects = new[] { adapter?.UnrealProject, Path.GetFileNameWithoutExtension(adapter?.Executable??game.ExePath).Replace("-Win64-Shipping","",StringComparison.OrdinalIgnoreCase),Path.GetFileName(game.InstallDirectory?.TrimEnd(Path.DirectorySeparatorChar)) }.Where(p=>!string.IsNullOrWhiteSpace(p)&&p.IndexOfAny(Path.GetInvalidFileNameChars())<0).Select(p=>p!).Distinct();
        var paths = projects.SelectMany(project => new[] { "Windows", "WindowsNoEditor" }.Select(platform =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), project, "Saved", "Config", platform, "GameUserSettings.ini")))
            .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return paths.Length == 1 ? paths[0] : null;
    }

    public static string Support(GameInfo game, PerformanceProfile profile) => Backend(game) switch {
        PerformanceBackend.Cyberpunk=>"Настройки из UserSettings.json: доступны только существующие параметры и значения файла. Перед запуском создаётся резервная копия. Выбранные технологии требуют поддержки GPU и игры.",
        PerformanceBackend.Unreal when Config(game, profile) != null => "Разрешение · режим экрана · FPS · VSync · качество · масштаб рендера. Конфиг сохраняется с резервной копией.",
        PerformanceBackend.Unreal => "Разрешение и режим экрана — при запуске. Для FPS, VSync и качества выбери существующий GameUserSettings.ini или запусти игру один раз.",
        PerformanceBackend.Unity => "Применяются разрешение и режим экрана через параметры Unity. FPS, VSync и качество пока настраиваются в самой игре.",
        PerformanceBackend.Source2 => "Применяются разрешение, режим экрана и fps_max. Качество, VSync и масштаб настраиваются в CS2.",
        _ => "Для этой игры пока нет адаптера применения. Профиль можно сохранить как план настройки."
    };

    public static string[] BuildArguments(GameInfo game, PerformanceProfile raw, Size screen)
    {
        var profile = raw.Copy();
        int width = profile.ResolutionWidth == 0 ? screen.Width : profile.ResolutionWidth;
        int height = profile.ResolutionHeight == 0 ? screen.Height : profile.ResolutionHeight;
        width = Math.Clamp(width, 640, 16384); height = Math.Clamp(height, 480, 16384);
        string w = width.ToString(CultureInfo.InvariantCulture), h = height.ToString(CultureInfo.InvariantCulture);
        return Backend(game) switch {
            PerformanceBackend.Unity => new[] { "-screen-width", w, "-screen-height", h, "-screen-fullscreen", profile.WindowMode == "Оконный" ? "0" : "1",
                "-window-mode", profile.WindowMode == "Полноэкранный" ? "exclusive" : "borderless" },
            PerformanceBackend.Unreal when Config(game, profile) != null => new[] { "-ResX=" + w, "-ResY=" + h },
            PerformanceBackend.Unreal when profile.WindowMode == "Безрамочный" => throw new InvalidOperationException("Для безрамочного режима подключи GameUserSettings.ini этой игры."),
            PerformanceBackend.Unreal => new[] { "-ResX=" + w, "-ResY=" + h, profile.WindowMode == "Оконный" ? "-windowed" : "-fullscreen" },
            PerformanceBackend.Source2 => new[] { "-w", w, "-h", h, profile.WindowMode == "Полноэкранный" ? "-fullscreen" : "-windowed" }
                .Concat(profile.WindowMode == "Безрамочный" ? new[] { "-noborder" } : Array.Empty<string>())
                .Concat(new[] { "+fps_max", profile.UnlimitedFps?"0":profile.TargetFps.ToString(CultureInfo.InvariantCulture) }).ToArray(),
            _ => Array.Empty<string>()
        };
    }

    public static string[] Prepare(GameInfo game)
    {
        if (game.Performance?.ApplyOnLaunch != true) return Array.Empty<string>();
        var profile = game.Performance.Copy();
        if (Backend(game) == PerformanceBackend.None) throw new InvalidOperationException("Для этой игры ещё нет адаптера применения профиля.");
        var adapter = ModGameAdapter.For(game);
        if (adapter != null || Backend(game) == PerformanceBackend.Source2)
        {
            string executable = adapter != null ? Path.GetFileNameWithoutExtension(adapter.Executable) : "cs2";
            var processes = new[] { executable, executable + "-Win64-Shipping" }.SelectMany(Process.GetProcessesByName).ToArray();
            bool running = processes.Length > 0; foreach (var process in processes) process.Dispose();
            if (running) throw new InvalidOperationException("Сначала закрой игру, чтобы применить профиль перед запуском.");
        }
        Size screen = Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
        string? path = Config(game, profile);
        if(Backend(game)==PerformanceBackend.Cyberpunk&&path==null)throw new InvalidOperationException("Подключи существующий UserSettings.json этой игры.");
        if(Backend(game)==PerformanceBackend.Cyberpunk){var gpu=RtxCompatibility.Detect(PcSnapshot.Read().Gpu);if(profile.NativeOptions.Any(p=>p.Key.EndsWith("|DLSSFrameGen")&&p.Value=="true")&&!gpu.NativeFrameGeneration)throw new InvalidOperationException("NVIDIA Frame Generation не поддерживается определённой видеокартой. Для RTX 30 используй отдельный режим FSR/OptiScaler.");if(profile.NativeOptions.Any(p=>p.Key.EndsWith("|DLSS")&&p.Value!="Off")&&!gpu.SuperResolution)throw new InvalidOperationException("Для DLSS нужна совместимая GeForce RTX.");}
        if (path != null){DlssManager.EnsureClosed(game);if(Backend(game)==PerformanceBackend.Cyberpunk)CyberpunkGraphics.Apply(path,profile.NativeOptions);else ApplyConfig(path, profile, screen);}
        return BuildArguments(game, profile, screen);
    }

    internal static string EditIni(string text, PerformanceProfile raw, Size screen)
    {
        var profile = raw.Copy();
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        string? section = lines.Select(line => line.Trim()).FirstOrDefault(line => Regex.IsMatch(line, @"^\[.*GameUserSettings\]$", RegexOptions.IgnoreCase));
        if (section == null) throw new InvalidDataException("В файле нет секции GameUserSettings. Выбери конфиг именно этой игры.");
        void Set(string header, string key, string value)
        {
            int start = lines.FindIndex(line => line.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
            if (start < 0) { lines.Add(""); lines.Add(header); start = lines.Count - 1; }
            int end = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
            bool found = false;
            for (int i = start + 1; i < end; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase)) continue;
                lines[i] = key + "=" + value; found = true;
            }
            if (!found) lines.Insert(end, key + "=" + value);
        }
        string width = (profile.ResolutionWidth == 0 ? screen.Width : profile.ResolutionWidth).ToString(CultureInfo.InvariantCulture);
        string height = (profile.ResolutionHeight == 0 ? screen.Height : profile.ResolutionHeight).ToString(CultureInfo.InvariantCulture);
        string mode = profile.WindowMode == "Полноэкранный" ? "0" : profile.WindowMode == "Безрамочный" ? "1" : "2";
        foreach (string key in new[] { "ResolutionSizeX", "LastUserConfirmedResolutionSizeX" }) Set(section, key, width);
        foreach (string key in new[] { "ResolutionSizeY", "LastUserConfirmedResolutionSizeY" }) Set(section, key, height);
        Set(section, "FullscreenMode", mode); Set(section, "LastConfirmedFullscreenMode", mode);
        Set(section, "FrameRateLimit", profile.UnlimitedFps?"0":profile.TargetFps.ToString(CultureInfo.InvariantCulture));
        Set(section, "bUseVSync", profile.VSync ? "True" : "False");
        Set(section, "bUseDynamicResolution", "False");
        int level = profile.Quality == "Низкие" ? 0 : profile.Quality == "Средние" ? 1 : 2;
        foreach (string key in new[] { "ViewDistanceQuality", "AntiAliasingQuality", "ShadowQuality", "GlobalIlluminationQuality", "ReflectionQuality",
            "PostProcessQuality", "TextureQuality", "EffectsQuality", "FoliageQuality", "ShadingQuality" })
            Set("[ScalabilityGroups]", "sg." + key, (profile.DetailLevels.TryGetValue(key,out int custom)?custom:level).ToString(CultureInfo.InvariantCulture));
        Set("[ScalabilityGroups]", "sg.ResolutionQuality", profile.RenderScale.ToString(CultureInfo.InvariantCulture));
        return string.Join("\r\n", lines);
    }

    internal static PerformanceProfile ReadConfig(string path)
    {
        ManagedPaths.NoLinks(path);if(new FileInfo(path).Length>4*1024*1024)throw new IOException("Конфигурация слишком большая.");var text=File.ReadAllText(path);var profile=new PerformanceProfile{ConfigFile=path};EditIni(text,profile,new(1920,1080));string section="";var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string line in text.Replace("\r\n","\n").Split('\n')){string clean=line.Trim();if(clean.StartsWith('[')){section=clean;continue;}int separator=clean.IndexOf('=');if(separator<1||clean.StartsWith(';'))continue;if(section.Contains("GameUserSettings",StringComparison.OrdinalIgnoreCase)||section.Equals("[ScalabilityGroups]",StringComparison.OrdinalIgnoreCase))values[clean[..separator].Trim()]=clean[(separator+1)..].Trim();}
        int Number(string key,int fallback)=>values.TryGetValue(key,out string? value)&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double parsed)&&double.IsFinite(parsed)&&parsed is >=int.MinValue and <=int.MaxValue?(int)Math.Round(parsed):fallback;
        profile.ResolutionWidth=Number("ResolutionSizeX",0);profile.ResolutionHeight=Number("ResolutionSizeY",0);profile.TargetFps=Number("FrameRateLimit",60);profile.RenderScale=Number("sg.ResolutionQuality",100);profile.VSync=values.TryGetValue("bUseVSync",out string? sync)&&sync.Equals("True",StringComparison.OrdinalIgnoreCase);profile.WindowMode=Number("FullscreenMode",1) switch{0=>"Полноэкранный",2=>"Оконный",_=>"Безрамочный"};
        profile.UnlimitedFps=Number("FrameRateLimit",60)==0;
        if(profile.UnlimitedFps)profile.TargetFps=60;
        foreach(var parameter in GraphicsParameters.Unreal){int level=Number("sg."+parameter.Key,-1);if(level is >=0 and <=3)profile.DetailLevels[parameter.Key]=level;}return profile.Copy();
    }

    internal static void ApplyConfig(string path, PerformanceProfile profile, Size screen)
    {
        ManagedPaths.NoLinks(path);ManagedPaths.NoLinks(path+".playspace-backup");
        if (!Path.GetFileName(path).Equals("GameUserSettings.ini", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Неверное имя конфигурации.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Файл-ссылка не поддерживается.");
        string updated = EditIni(File.ReadAllText(path), profile, screen);
        if (!File.Exists(path + ".playspace-backup")) File.Copy(path, path + ".playspace-backup");
        AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(updated));
    }
    public static void RestoreConfig(string path)
    {
        if(Path.GetFileName(path).Equals("UserSettings.json",StringComparison.OrdinalIgnoreCase)){CyberpunkGraphics.Restore(path);return;}
        ManagedPaths.NoLinks(path);ManagedPaths.NoLinks(path+".playspace-backup");
        if (!File.Exists(path + ".playspace-backup")) throw new FileNotFoundException("Резервная копия не найдена.");
        if (!Path.GetFileName(path).Equals("GameUserSettings.ini", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Неверное имя конфигурации.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Файл-ссылка не поддерживается.");
        AtomicWrite(path, File.ReadAllBytes(path + ".playspace-backup"));
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Replace(temporary, path, null); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
