using System.Diagnostics;

namespace GameLauncherGUI;

internal static class UnrealModLayout
{
    public const string Shim = "Thunderstore-unreal_shimloader";
    private const string Profile = ".playspace-mods/unreal/";

    public static string? MapFile(ModSelection mod, string name, ModGameAdapter adapter)
    {
        string binary = adapter.UnrealProject + "/Binaries/Win64/";
        string leaf = Path.GetFileName(name);
        if (!name.Contains('/') && new[] { "changelog.txt", "readme.txt", "credits.txt" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        if (new[] { ".exe", ".bat", ".cmd", ".ps1", ".sh" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Исполняемый установщик не поддерживается: " + name);
        if (mod.Package.Id == Shim)
        {
            if (!(name.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) || name.StartsWith("UE4SS/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Неизвестный файл unreal-shimloader: " + name);
            if (name.StartsWith("UE4SS/Mods/", StringComparison.OrdinalIgnoreCase)) return Profile + "mods/" + name[11..];
            if (name.StartsWith("UE4SS/", StringComparison.OrdinalIgnoreCase))
            {
                name = name[6..];
                if (name.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase)) return null; // Keep shim's proxy, not UE4SS's bundled proxy.
            }
            return binary + name;
        }
        if (name.StartsWith("overlay/", StringComparison.OrdinalIgnoreCase))
            return Profile + "overlay/" + mod.Package.Id + "/" + name[8..];
        if (name.StartsWith("mod/", StringComparison.OrdinalIgnoreCase))
            return Profile + "mods/" + mod.Package.Name + "/" + name[4..];
        if (name.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = name[5..];
            if (!suffix.Contains('/')) throw new InvalidDataException("Общий mods.txt не будет перезаписан; требуется enabled.txt в папке мода.");
            return Profile + "mods/" + suffix;
        }
        if (name.StartsWith("pak/", StringComparison.OrdinalIgnoreCase)) return Profile + "paks/" + name[4..];
        if (name.StartsWith("cfg/", StringComparison.OrdinalIgnoreCase)) return Profile + "cfg/" + name[4..];
        if (!name.Contains('/') && new[] { ".pak", ".ucas", ".utoc" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            return adapter.UnrealProject + "/Content/Paks/" + leaf;
        return Profile + "mods/" + mod.Package.Name + "/" + name;
    }

    public static string[] LaunchArguments(GameInfo game)
    {
        var adapter = ModGameAdapter.For(game);
        string? root = adapter?.IsUnreal == true ? adapter.FindRoot(game) : null;
        if (root == null) return Array.Empty<string>();
        if (!new ModInstaller(root).ReadState().Packages.ContainsKey(Shim)) return Array.Empty<string>();
        var arguments = new List<string>();
        foreach (var (option, folder) in new[] { ("--mod-dir", "mods"), ("--pak-dir", "paks"), ("--cfg-dir", "cfg"), ("--overlay-dir", "overlay") })
        {
            string path = ModInstaller.SafePath(root, Profile + folder);
            Directory.CreateDirectory(path);
            arguments.Add(option); arguments.Add(path);
        }
        return arguments.ToArray();
    }

    public static ProcessStartInfo SteamStart(string steamExe, string appId, IEnumerable<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(appId) || !appId.All(char.IsAsciiDigit)) throw new InvalidDataException("Неверный Steam App ID.");
        var start = new ProcessStartInfo { FileName = steamExe, UseShellExecute = false };
        start.ArgumentList.Add("-applaunch"); start.ArgumentList.Add(appId);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}
