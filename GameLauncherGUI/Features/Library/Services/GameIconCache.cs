using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

// Images belong to this cache; cards only borrow them and never dispose them.
internal sealed class GameIconCache : IDisposable
{
    private readonly Dictionary<string, Bitmap?> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly Lazy<Dictionary<string, string>> shortcuts = new(ReadSteamShortcuts);
    private bool disposed;

    public Bitmap? Peek(GameInfo game)
    {
        lock (gate)
            return disposed ? null : images.GetValueOrDefault($"{game.SteamAppId}|{game.Name}|{game.ExePath}|{game.CustomIconPath}");
    }

    public Bitmap? Get(GameInfo game)
    {
        string key = $"{game.SteamAppId}|{game.Name}|{game.ExePath}|{game.CustomIconPath}";
        lock (gate)
        {
            if (disposed) return null;
            if (images.TryGetValue(key, out var cached)) return cached;
        }
        Bitmap? image = Load(game);
        lock (gate)
        {
            if (disposed) { image?.Dispose(); return null; }
            if (images.TryGetValue(key, out var existing)) { image?.Dispose(); return existing; }
            images[key] = image;
            return image;
        }
    }

    private Bitmap? Load(GameInfo game)
    {
        if (!string.IsNullOrWhiteSpace(game.CustomIconPath))
        {
            var custom = ReadSquareImage(game.CustomIconPath);
            if (custom != null) return custom;
        }
        if (!string.IsNullOrEmpty(game.SteamAppId))
        {
            if (shortcuts.Value.TryGetValue(game.SteamAppId, out var iconPath))
            {
                var icon = ReadIcon(iconPath);
                if (icon != null) return icon;
            }
            // Steam can launch through a URI or helper exe; use its game artwork first.
            if (game.SteamAppId.All(char.IsAsciiDigit))
            foreach (string steam in SteamFolders())
            {
                string folder = Path.Combine(steam, "appcache", "librarycache", game.SteamAppId);
                foreach (string file in Files(folder, "*.jpg").Where(f => Path.GetFileNameWithoutExtension(f).Length == 40))
                {
                    var image = ReadSquareImage(file);
                    if (image != null) return image;
                }
                foreach (string file in Files(Path.Combine(steam, "appcache", "librarycache"), game.SteamAppId + "_icon.*"))
                {
                    var image = ReadSquareImage(file);
                    if (image != null) return image;
                }
            }
        }
        if (string.Equals(game.Name, "VALORANT", StringComparison.OrdinalIgnoreCase))
        {
            // The launch target is Riot Client, but the card should show Valorant's icon.
            try
            {
                string manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "RiotClientInstalls.json");
                if (File.Exists(manifest))
                {
                    var clients = JObject.Parse(File.ReadAllText(manifest))["associated_client"] as JObject;
                    foreach (var install in clients?.Properties() ?? Enumerable.Empty<JProperty>())
                    {
                        if (!install.Name.Replace('\\', '/').TrimEnd('/').EndsWith("/VALORANT/live", StringComparison.OrdinalIgnoreCase)) continue;
                        var icon = ReadIcon(Path.Combine(install.Name, "VALORANT.exe"));
                        if (icon != null) return icon;
                    }
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { }
        }
        return ReadIcon(game.ExePath);
    }

    private static Bitmap? ReadIcon(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using Icon? icon = Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? new Icon(path, new Size(48, 48)) : Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch (Exception ex) when (IsReadError(ex)) { return null; }
    }

    private static Bitmap? ReadSquareImage(string path)
    {
        try
        {
            using var image = Image.FromFile(path);
            return image.Width == image.Height ? new Bitmap(image) : null;
        }
        catch (Exception ex) when (IsReadError(ex)) { return null; }
    }

    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or
        ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException or
        OutOfMemoryException or Newtonsoft.Json.JsonException or System.Security.SecurityException;

    private static string[] Files(string path, string pattern)
    {
        try { return Directory.Exists(path) ? Directory.GetFiles(path, pattern) : Array.Empty<string>(); }
        catch (Exception ex) when (IsReadError(ex)) { return Array.Empty<string>(); }
    }

    internal static IEnumerable<string> SteamFolders()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path) folders.Add(path);
        }
        catch (Exception ex) when (IsReadError(ex)) { }
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        return folders;
    }

    private static Dictionary<string, string> ReadSteamShortcuts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] folders = {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Steam")
        };
        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (string file in Files(folder, "*.url"))
        {
            try
            {
                string? id = null, icon = null;
                foreach (string line in File.ReadLines(file))
                {
                    if (line.StartsWith("URL=steam://rungameid/", StringComparison.OrdinalIgnoreCase)) id = line[22..].Trim().TrimEnd('/');
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) icon = Environment.ExpandEnvironmentVariables(line[9..].Trim().Trim('"'));
                }
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(icon)) result[id] = icon;
            }
            catch (Exception ex) when (IsReadError(ex)) { }
        }
        return result;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var image in images.Values) image?.Dispose();
            images.Clear();
        }
    }
}
