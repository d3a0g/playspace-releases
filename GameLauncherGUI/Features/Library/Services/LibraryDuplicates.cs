namespace GameLauncherGUI;

internal sealed record DuplicateGroup(GameInfo[] Games, string Reason);

internal static class LibraryDuplicates
{
    internal static IReadOnlyList<DuplicateGroup> Find(IEnumerable<GameInfo> library)
    {
        var remaining = library.ToList();
        var result = new List<DuplicateGroup>();
        while (remaining.Count > 0)
        {
            var group = new List<GameInfo> { remaining[0] }; remaining.RemoveAt(0);
            bool added;
            do
            {
                added = false;
                foreach (var game in remaining.ToArray())
                    if (group.Any(other => Same(other, game)) && !group.Any(other => ValidId(other.SteamAppId) && ValidId(game.SteamAppId) && other.SteamAppId!=game.SteamAppId)) { group.Add(game); remaining.Remove(game); added = true; }
            } while (added);
            if (group.Count > 1) result.Add(new(group.ToArray(), "Совпадает путь EXE или Steam AppID. Выберите запись, которую сохранить."));
        }
        return result;
    }
    private static bool Same(GameInfo a, GameInfo b) =>
        ValidId(a.SteamAppId) && a.SteamAppId == b.SteamAppId ||
        !(ValidId(a.SteamAppId)&&ValidId(b.SteamAppId)&&a.SteamAppId!=b.SteamAppId) &&
        a.LaunchArguments==b.LaunchArguments && Normalize(a.ExePath) is string path && path == Normalize(b.ExePath);
    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.All(char.IsAsciiDigit) && id.Any(c => c != '0');
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant(); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return null; }
    }
    internal static GameInfo Merge(GameInfo keep, IEnumerable<GameInfo> records)
    {
        // Clone first: a failed save must not mutate the live library.
        var merged = Newtonsoft.Json.JsonConvert.DeserializeObject<GameInfo>(Newtonsoft.Json.JsonConvert.SerializeObject(keep))!;
        foreach (var game in records)
        {
            if(!ValidId(merged.SteamAppId)&&ValidId(game.SteamAppId))merged.SteamAppId=game.SteamAppId;
            merged.IsPinned |= game.IsPinned;
            merged.PlayedSeconds = Math.Max(merged.PlayedSeconds, game.PlayedSeconds);
            if (merged.LastLaunchUtc == null || game.LastLaunchUtc > merged.LastLaunchUtc) merged.LastLaunchUtc = game.LastLaunchUtc;
            if(merged.SteamPlaytimeUser==null&&!merged.SteamPlaytimeMinutes.HasValue)merged.SteamPlaytimeUser=game.SteamPlaytimeUser;
            if (merged.SteamPlaytimeUser == game.SteamPlaytimeUser && game.SteamPlaytimeMinutes.HasValue)
                merged.SteamPlaytimeMinutes = Math.Max(merged.SteamPlaytimeMinutes ?? 0, game.SteamPlaytimeMinutes.Value);
            merged.CustomIconPath ??= game.CustomIconPath;
            merged.Performance ??= game.Performance;
        }
        return merged;
    }
}
