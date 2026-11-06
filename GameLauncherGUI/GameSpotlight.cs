namespace GameLauncherGUI;

internal sealed class GameShuffle
{
    private readonly HashSet<GameInfo> suggested = new();

    public GameInfo? Next(IReadOnlyList<GameInfo> games, GameInfo? selected)
    {
        if (games.Count == 0) return null;
        suggested.RemoveWhere(game => !games.Contains(game));
        var remaining = games.Where(game => !suggested.Contains(game) && (games.Count == 1 || game != selected)).ToArray();
        if (remaining.Length == 0)
        {
            suggested.Clear();
            remaining = games.Where(game => games.Count == 1 || game != selected).ToArray();
        }
        var chosen = remaining[Random.Shared.Next(remaining.Length)];
        suggested.Add(chosen);
        return chosen;
    }
}

internal static class GameArtworkLoader
{
    public static Bitmap? Load(GameInfo game)
    {
        string? id = game.SteamAppId;
        if (string.IsNullOrEmpty(id) || !id.All(char.IsAsciiDigit)) return null;
        foreach (string steam in GameIconCache.SteamFolders())
        {
            string cache = Path.Combine(steam, "appcache", "librarycache");
            foreach (string path in new[] {
                Path.Combine(cache, id, "library_hero.jpg"), Path.Combine(cache, id + "_library_hero.jpg"),
                Path.Combine(cache, id, "header.jpg"), Path.Combine(cache, id + "_header.jpg") })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using var image = Image.FromFile(path);
                    int width = Math.Min(image.Width, 1280);
                    int height = Math.Max(1, (int)((long)image.Height * width / image.Width));
                    return new Bitmap(image, width, height);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                    OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { }
            }
        }
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncherPRO", "artwork");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, id + "-hero.jpg");
            if (!File.Exists(file))
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                using var response = client.GetAsync($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/library_hero.jpg", System.Net.Http.HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var input = response.Content.ReadAsStream();
                using var data = new MemoryStream(); byte[] block = new byte[81920]; int count;
                while ((count=input.Read(block))>0) { if(data.Length+count>10*1024*1024)return null;data.Write(block,0,count); }
                data.Position=0;using var original=Image.FromStream(data);
                using var scaled=new Bitmap(original,1280,Math.Max(1,1280*original.Height/original.Width));
                scaled.Save(file,System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            using var cached=Image.FromFile(file); return new Bitmap(cached);
        }
        catch(Exception ex) when(ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException) { }        return null;
    }
}
