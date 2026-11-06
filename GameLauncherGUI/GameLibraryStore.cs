using Newtonsoft.Json;

namespace GameLauncherGUI;

internal static class GameLibraryStore
{
    public static (List<GameInfo> Games, string? Notice) Load(string path)
    {
        if (TryRead(path, out var games)) return (games, null);
        if (TryRead(path + ".bak", out games))
            return (games, "Библиотека загружена из резервной копии.");
        bool exists = File.Exists(path) || File.Exists(path + ".bak");
        return (new List<GameInfo>(), exists ? "Не удалось прочитать библиотеку и резервную копию. Исходные файлы сохранены." : null);
    }

    private static bool TryRead(string path, out List<GameInfo> games)
    {
        games = new();
        try
        {
            if (!File.Exists(path)) return false;
            var result = JsonConvert.DeserializeObject<List<GameInfo>>(File.ReadAllText(path));
            if (result == null || result.Any(g => g == null || string.IsNullOrWhiteSpace(g.Name) || string.IsNullOrWhiteSpace(g.ExePath))) return false;
            games = result;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public static void Save(string path, List<GameInfo> games)
    {
        string folder = Path.GetDirectoryName(path) ?? throw new ArgumentException("Не указан каталог библиотеки.");
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $".games-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
                writer.Write(JsonConvert.SerializeObject(games, Formatting.Indented));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path) && TryRead(path, out _)) File.Replace(temporary, path, path + ".bak");
            else
            {
                // Keep a damaged primary for recovery; never rotate it over a healthy backup.
                if (File.Exists(path)) File.Copy(path, Path.Combine(folder, $"games-corrupt-{Guid.NewGuid():N}.json"));
                File.Move(temporary, path, overwrite: true);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
