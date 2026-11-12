using System.Text.RegularExpressions;

namespace GameLauncherGUI;

internal sealed record StrategyGameAdapter(string SteamId, string Name, string DocumentsFolder, string Executable)
{
    public static readonly StrategyGameAdapter[] All = {
        new("394360", "Hearts of Iron IV", "Hearts of Iron IV", "hoi4.exe"),
        new("236850", "Europa Universalis IV", "Europa Universalis IV", "eu4.exe"),
        new("281990", "Stellaris", "Stellaris", "stellaris.exe"),
        new("1158310", "Crusader Kings III", "Crusader Kings III", "binaries/ck3.exe"),
        new("859580", "Imperator: Rome", "Imperator", "binaries/imperator.exe")
    };
    public static StrategyGameAdapter? For(GameInfo game) => All.FirstOrDefault(adapter => adapter.SteamId == game.SteamAppId ||
        (string.IsNullOrEmpty(game.SteamAppId) && Path.GetFileName(game.ExePath).Equals(Path.GetFileName(adapter.Executable), StringComparison.OrdinalIgnoreCase)));
    public string UserDirectory
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) throw new IOException("Не удалось определить папку «Документы» текущего пользователя.");
            return Path.Combine(documents, "Paradox Interactive", DocumentsFolder);
        }
    }
    public string WorkshopUrl => $"steam://url/SteamWorkshopPage/{SteamId}";

    public string? FindExecutable(GameInfo game)
    {
        if (game.InstallDirectory != null)
        {
            string candidate = Path.Combine(game.InstallDirectory, Executable);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        if (!File.Exists(game.ExePath)) return null;
        var folder = Directory.GetParent(game.ExePath);
        for (int i = 0; folder != null && i < 5; i++, folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, Executable);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public IEnumerable<string> WorkshopDirectories(GameInfo game)
    {
        var steamApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folder = File.Exists(game.ExePath) ? Directory.GetParent(game.ExePath) :
            Directory.Exists(game.InstallDirectory) ? new DirectoryInfo(game.InstallDirectory!) : null;
        for (; folder != null; folder = folder.Parent)
            if (folder.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) { steamApps.Add(folder.FullName); break; }
        foreach (string steam in GameIconCache.SteamFolders())
        {
            steamApps.Add(Path.Combine(steam, "steamapps"));
            string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            try
            {
                if (!File.Exists(libraries)) continue;
                foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                    steamApps.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return steamApps.Select(path => Path.Combine(path, "workshop", "content", SteamId)).Where(Directory.Exists);
    }
}

internal sealed record StrategyModEntry(string Name, string? Descriptor, string Content, string Metadata, bool Workshop, string? ManagedId, bool Enabled)
{
    public string SupportedVersion => ParadoxDescriptor.Value(Metadata, "supported_version") ?? "не указана";
    public override string ToString() => (Enabled ? "●  " : "○  ") + Name + (Workshop ? "  ·  Steam Workshop" : "  ·  локальный");
}

internal static class ParadoxDescriptor
{
    private static IEnumerable<(string Line, bool Top)> Lines(string text)
    {
        int depth = 0;
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            yield return (line, depth == 0);
            bool quote = false, escape = false;
            foreach (char c in line)
            {
                if (escape) { escape = false; continue; }
                if (quote && c == '\\') { escape = true; continue; }
                if (c == '"') { quote = !quote; continue; }
                if (!quote && c == '#') break;
                if (!quote && c == '{') depth++;
                if (!quote && c == '}') depth--;
            }
            if (depth < 0) throw new InvalidDataException("Неверная структура descriptor.mod.");
        }
        if (depth != 0) throw new InvalidDataException("Незакрытый блок descriptor.mod.");
    }
    public static string? Value(string text, string key)
    {
        foreach (var (line, top) in Lines(text))
        {
            if (!top) continue;
            var match = Regex.Match(line, "^\\s*" + Regex.Escape(key) + "\\s*=\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        return null;
    }
    public static string Registration(string metadata, string content)
    {
        if (string.IsNullOrWhiteSpace(Value(metadata, "name"))) throw new InvalidDataException("В descriptor.mod не задано name.");
        string path = Path.GetFullPath(content).Replace('\\', '/');
        if (path.Contains('"') || path.Contains('\n')) throw new InvalidDataException("Недопустимый путь мода.");
        return LocalMetadata(metadata) + "\r\npath=\"" + path + "\"\r\n";
    }
    public static string LocalMetadata(string metadata) => string.Join("\r\n", Lines(metadata).Where(item => !item.Top ||
        !Regex.IsMatch(item.Line, @"^\s*(path|archive|remote_file_id)\s*=", RegexOptions.IgnoreCase)).Select(item => item.Line));
}
