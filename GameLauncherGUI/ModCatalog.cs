using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record ModGameAdapter(string SteamId, string Name, string Community, string Executable, string Loader, string? UnrealProject = null)
{
    public bool IsUnreal => UnrealProject != null;
    public static readonly ModGameAdapter[] All = {
        new("3241660", "R.E.P.O.", "repo", "REPO.exe", "BepInEx-BepInExPack"),
        new("1966720", "Lethal Company", "lethal-company", "Lethal Company.exe", "BepInEx-BepInExPack"),
        new("892970", "Valheim", "valheim", "valheim.exe", "denikson-BepInExPack_Valheim"),
        new("632360", "Risk of Rain 2", "riskofrain2", "Risk of Rain 2.exe", "bbepis-BepInExPack"),
        new("1557740", "ROUNDS", "rounds", "Rounds.exe", "BepInEx-BepInExPack_ROUNDS"),
        new("1229490", "ULTRAKILL", "ultrakill", "ULTRAKILL.exe", "BepInEx-BepInExPack"),
        new("1092790", "Inscryption", "inscryption", "Inscryption.exe", "BepInEx-BepInExPack_Inscryption"),
        new("2881650", "Content Warning", "content-warning", "Content Warning.exe", "BepInEx-BepInExPack"),
        new("4704690", "MECCHA CHAMELEON", "meccha-chameleon", "PenguinHotel.exe", "Thunderstore-MecchaChameleon_UE4SS", "Chameleon"),
        new("3527290", "PEAK", "peak", "PEAK.exe", "BepInEx-BepInExPack_PEAK"),
        new("1366540", "Dyson Sphere Program", "dyson-sphere-program", "DSPGAME.exe", "xiaoye97-BepInEx"),
        new("1623730", "Palworld", "palworld", "Palworld.exe", "Thunderstore-unreal_shimloader", "Pal"),
        new("493520", "GTFO", "gtfo", "GTFO.exe", "BepInEx-BepInExPack_GTFO")
    };
    public static ModGameAdapter? For(GameInfo game) => All.FirstOrDefault(a => a.SteamId == game.SteamAppId ||
        (string.IsNullOrEmpty(game.SteamAppId) && Path.GetFileName(game.ExePath).Equals(a.Executable, StringComparison.OrdinalIgnoreCase)));

    public string? FindRoot(GameInfo game)
    {
        bool Valid(string folder) => File.Exists(Path.Combine(folder, Executable)) && (IsUnreal
            ? File.Exists(Path.Combine(folder, UnrealProject!, "Binaries", "Win64", Path.GetFileNameWithoutExtension(Executable) + "-Win64-Shipping.exe"))
            : Directory.EnumerateDirectories(folder, "*_Data").Any());
        if (!string.IsNullOrEmpty(game.InstallDirectory) && Valid(game.InstallDirectory)) return Path.GetFullPath(game.InstallDirectory);
        if (!File.Exists(game.ExePath)) return null;
        var folder = Directory.GetParent(game.ExePath);
        for (int depth = 0; folder != null && depth < 5; depth++, folder = folder.Parent)
            if (Valid(folder.FullName))
                return folder.FullName;
        return null;
    }
}

internal sealed class ModPackage
{
    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("owner")] public string Owner { get; set; } = "";
    [JsonProperty("is_deprecated")] public bool Deprecated { get; set; }
    [JsonProperty("has_nsfw_content")] public bool Nsfw { get; set; }
    [JsonProperty("categories")] public string[] Categories { get; set; } = Array.Empty<string>();
    [JsonProperty("versions")] public List<ModVersion> Versions { get; set; } = new();
    public string Id => Owner + "-" + Name;
    public ModVersion? Latest => Versions.FirstOrDefault(v => v.Active);
    public override string ToString() => $"{Name}  ·  {Owner}  ·  {Latest?.Version}";
}

internal sealed class ModVersion
{
    [JsonProperty("version_number")] public string Version { get; set; } = "";
    [JsonProperty("description")] public string Description { get; set; } = "";
    [JsonProperty("download_url")] public string DownloadUrl { get; set; } = "";
    [JsonProperty("dependencies")] public string[] Dependencies { get; set; } = Array.Empty<string>();
    [JsonProperty("is_active")] public bool Active { get; set; } = true;
    [JsonProperty("downloads")] public long Downloads { get; set; }
}

internal sealed class ModCatalog : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) {
        Timeout = TimeSpan.FromMinutes(3), MaxResponseContentBufferSize = 200 * 1024 * 1024
    };
    public ModCatalog() => http.DefaultRequestHeaders.UserAgent.ParseAdd("Playspace/1.0");
    public async Task<List<ModPackage>> LoadAsync(ModGameAdapter adapter, CancellationToken cancellation)
    {
        if(Workspace.Current.Offline)throw new IOException("Включён офлайн-режим. Используйте сохранённый каталог.");
        using var response = await http.GetAsync($"https://thunderstore.io/c/{adapter.Community}/api/v1/package/",
            HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var interrupt = cancellation.Register(stream.Dispose);
        return await Task.Run(async () =>
        {
            using var text = new StreamReader(stream);
            using var reader = new JsonTextReader(text);
            var serializer = JsonSerializer.CreateDefault();
            var result = new List<ModPackage>();
            try
            {
                bool ended = false;
                if (!await reader.ReadAsync(cancellation) || reader.TokenType != JsonToken.StartArray)
                    throw new InvalidDataException("Неожиданный формат каталога Thunderstore.");
                while (await reader.ReadAsync(cancellation))
                {
                    if (reader.TokenType == JsonToken.EndArray) { ended = true; break; }
                    cancellation.ThrowIfCancellationRequested();
                    var package = serializer.Deserialize<ModPackage>(reader);
                    if (package == null) continue;
                    // Old versions remain available for exact dependency resolution; their descriptions are unnecessary.
                    foreach (var version in package.Versions.Skip(1)) version.Description = "";
                    result.Add(package);
                    if (result.Count > 100000) throw new InvalidDataException("Каталог слишком велик.");
                }
                if (!ended) throw new InvalidDataException("Каталог загружен не полностью.");
                return result;
            }
            catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation); }
        }, cancellation).ConfigureAwait(false);
    }
    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellation,string title="Мод Thunderstore")
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || !(uri.Host.Equals("thunderstore.io", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Архив должен находиться на Thunderstore.");
        return await Workspace.Current.Transfers.Fetch(title,uri,100L*1024*1024,cancellation);
    }
    public void Dispose() => http.Dispose();
}
