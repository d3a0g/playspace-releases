using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal sealed class StrategyModsManager
{
    private sealed class Profile
    {
        public string[] OriginalEnabled { get; set; } = Array.Empty<string>();
        public string[] Enabled { get; set; } = Array.Empty<string>();
    }
    private readonly StrategyGameAdapter adapter;
    private readonly GameInfo game;
    private readonly string userRoot;
    private readonly ModInstaller installer;
    private readonly Action? verifyClosed;
    private string LoadFile => ModInstaller.SafePath(userRoot, "dlc_load.json");
    private string ProfileFile => ModInstaller.SafePath(userRoot, ".playspace-mods/strategy-profile.json");
    public string UserRoot => userRoot;
    public StrategyModsManager(StrategyGameAdapter adapter, GameInfo game, string? userDirectory = null, Action? verifyClosed = null)
    {
        this.adapter = adapter; this.game = game; userRoot = Path.GetFullPath(userDirectory ?? adapter.UserDirectory);
        this.verifyClosed = verifyClosed;
        installer = new ModInstaller(userRoot);
    }

    private static string ReadDescriptor(string file)
    {
        if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("Descriptor мода слишком велик.");
        return File.ReadAllText(file);
    }
    private JObject LoadJson() => File.Exists(LoadFile) ? JObject.Parse(File.ReadAllText(LoadFile)) : new JObject();
    private string[] Enabled() => File.Exists(ProfileFile)
        ? (JsonConvert.DeserializeObject<Profile>(File.ReadAllText(ProfileFile)) ?? throw new InvalidDataException("Повреждён профиль модов.")).Enabled
        : LoadJson()["enabled_mods"]?.ToObject<string[]>() ?? Array.Empty<string>();
    private static string ResolveContent(string metadata, string userRoot)
    {
        string? path = ParadoxDescriptor.Value(metadata, "path") ?? ParadoxDescriptor.Value(metadata, "archive");
        if (string.IsNullOrWhiteSpace(path)) return "";
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(userRoot, path));
    }

    public List<StrategyModEntry> List()
    {
        var enabled = Enabled(); var state = installer.ReadState();
        var result = new List<StrategyModEntry>();
        string modRoot = ModInstaller.SafePath(userRoot, "mod");
        if (Directory.Exists(modRoot))
        foreach (string file in Directory.GetFiles(modRoot, "*.mod"))
        {
            string relative = "mod/" + Path.GetFileName(file);
            string metadata = ReadDescriptor(file);
            string content = ResolveContent(metadata, userRoot);
            var owned = state.Packages.Values.FirstOrDefault(package => package.Files.Contains(relative, StringComparer.OrdinalIgnoreCase));
            result.Add(new StrategyModEntry(ParadoxDescriptor.Value(metadata, "name") ?? Path.GetFileNameWithoutExtension(file), relative,
                content, metadata, owned?.Id.StartsWith("Workshop-", StringComparison.OrdinalIgnoreCase) == true, owned?.Id,
                enabled.Contains(relative, StringComparer.OrdinalIgnoreCase)));
        }
        foreach (string workshop in adapter.WorkshopDirectories(game))
        foreach (string directory in Directory.GetDirectories(workshop))
        {
            string file = Path.Combine(directory, "descriptor.mod");
            if (!File.Exists(file) || !new DirectoryInfo(directory).Name.All(char.IsAsciiDigit)) continue;
            if (result.Any(mod => mod.Content.Equals(directory, StringComparison.OrdinalIgnoreCase))) continue;
            string metadata = ReadDescriptor(file);
            result.Add(new StrategyModEntry(ParadoxDescriptor.Value(metadata, "name") ?? new DirectoryInfo(directory).Name,
                null, directory, metadata, true, null, false));
        }
        return result.OrderBy(entry => entry.Enabled ? Array.FindIndex(enabled, path => path.Equals(entry.Descriptor, StringComparison.OrdinalIgnoreCase)) : int.MaxValue)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string[] Dependencies(string metadata)
    {
        var match = Regex.Match(metadata, @"(?m)^\s*dependencies\s*=\s*\{([^}]+)\}", RegexOptions.IgnoreCase);
        return match.Success ? Regex.Matches(match.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray() : Array.Empty<string>();
    }

    public void EnsureClosed()
    {
        if (verifyClosed != null) { verifyClosed(); return; }
        string name = Path.GetFileNameWithoutExtension(adapter.Executable);
        var processes = new[] { name, "Paradox Launcher", "dowser" }.SelectMany(Process.GetProcessesByName).ToArray();
        bool running = processes.Length > 0; foreach (var process in processes) process.Dispose();
        if (running) throw new IOException("Сначала закрой игру и Paradox Launcher.");
    }

    public void SetEnabled(StrategyModEntry entry, bool enable)
    {
        EnsureClosed();
        var mods = List();
        if (enable)
        {
            if (!Directory.Exists(entry.Content) && !File.Exists(entry.Content)) throw new DirectoryNotFoundException("Файлы мода не найдены.");
            var missing = Dependencies(entry.Metadata).Where(name => !mods.Any(mod => mod.Enabled && mod.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length > 0) throw new IOException("Сначала установи и включи зависимости: " + string.Join(", ", missing));
        }
        else
        {
            var dependants = mods.Where(mod => mod.Enabled && mod.Descriptor != entry.Descriptor && Dependencies(mod.Metadata).Contains(entry.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (dependants.Length > 0) throw new IOException("Сначала отключи зависящие моды: " + string.Join(", ", dependants.Select(mod => mod.Name)));
        }
        string? descriptor = entry.Descriptor;
        if (descriptor == null && enable) descriptor = RegisterWorkshop(entry);
        if (descriptor == null) return;
        using var guard = ProfileLock();
        var json = LoadJson(); var enabled = Enabled().ToList();
        CaptureOriginal(enabled);
        enabled.RemoveAll(path => path.Equals(descriptor, StringComparison.OrdinalIgnoreCase));
        if (enable) enabled.Add(descriptor);
        WriteEnabled(json, enabled);
    }

    private string RegisterWorkshop(StrategyModEntry entry)
    {
        string id = new DirectoryInfo(entry.Content).Name;
        if (!id.All(char.IsAsciiDigit)) throw new InvalidDataException("Некорректный Workshop ID.");
        string relative = "mod/playspace_ws_" + id + ".mod";
        string metadata = ParadoxDescriptor.Registration(entry.Metadata, entry.Content);
        var package = new ModPackage { Owner = "Workshop", Name = id };
        installer.Apply(new() { (new(package, new ModVersion { Version = "local" }), new Dictionary<string, byte[]> {
            [relative] = new UTF8Encoding(false).GetBytes(metadata) }) }, new Progress<string>());
        return relative;
    }

    public void Move(StrategyModEntry entry, int direction)
    {
        EnsureClosed(); if (!entry.Enabled || entry.Descriptor == null) return;
        using var guard = ProfileLock();
        var json = LoadJson(); var enabled = Enabled().ToList();
        int index = enabled.FindIndex(path => path.Equals(entry.Descriptor, StringComparison.OrdinalIgnoreCase));
        int next = index + Math.Sign(direction);
        if (index < 0 || next < 0 || next >= enabled.Count) return;
        var byDescriptor = List().Where(mod => mod.Descriptor != null).ToDictionary(mod => mod.Descriptor!, StringComparer.OrdinalIgnoreCase);
        (enabled[index], enabled[next]) = (enabled[next], enabled[index]);
        foreach (var (path, position) in enabled.Select((path, position) => (path, position)))
        {
            if (!byDescriptor.TryGetValue(path, out var mod)) continue;
            foreach (string dependency in Dependencies(mod.Metadata))
            {
                int dependencyPosition = enabled.FindIndex(item => byDescriptor.TryGetValue(item, out var required) && required.Name.Equals(dependency, StringComparison.OrdinalIgnoreCase));
                if (dependencyPosition < 0 || dependencyPosition > position) throw new IOException("Зависимость должна загружаться раньше: " + dependency);
            }
        }
        CaptureOriginal(json["enabled_mods"]?.ToObject<List<string>>() ?? new());
        WriteEnabled(json, enabled);
    }

    public void Remove(StrategyModEntry entry)
    {
        EnsureClosed();
        if (entry.ManagedId == null) throw new IOException("Этот мод не установлен PLAYSPACE. Его файлы не будут удалены.");
        if (entry.Enabled) SetEnabled(entry, false);
        installer.Uninstall(entry.ManagedId);
    }

    public void RestoreControl()
    {
        EnsureClosed(); using var guard = ProfileLock();
        if (!File.Exists(ProfileFile)) return;
        var profile = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(ProfileFile)) ?? throw new InvalidDataException("Повреждён профиль.");
        var json = LoadJson(); json["enabled_mods"] = JArray.FromObject(profile.OriginalEnabled);
        WriteJson(LoadFile, json); File.Delete(ProfileFile);
    }
    private void CaptureOriginal(List<string> enabled)
    {
        if (File.Exists(ProfileFile)) return;
        if (File.Exists(LoadFile)) File.Copy(LoadFile, ModInstaller.SafePath(userRoot, ".playspace-mods/strategy-original-" + Guid.NewGuid().ToString("N") + ".json"));
        WriteJson(ProfileFile, new Profile { OriginalEnabled = enabled.ToArray(), Enabled = enabled.ToArray() });
    }
    private void WriteEnabled(JObject json, List<string> enabled)
    {
        var profile = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(ProfileFile)) ?? throw new InvalidDataException("Повреждён профиль.");
        profile.Enabled = enabled.ToArray(); WriteJson(ProfileFile, profile);
        json["enabled_mods"] = JArray.FromObject(enabled); WriteJson(LoadFile, json);
    }
    private FileStream ProfileLock()
    {
        string state = ModInstaller.SafePath(userRoot, ".playspace-mods"); Directory.CreateDirectory(state);
        return new FileStream(ModInstaller.SafePath(userRoot, ".playspace-mods/strategy.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public string ImportZip(string archive, IProgress<string> progress, CancellationToken cancellation)
    {
        EnsureClosed();
        string staging = Path.Combine(Path.GetTempPath(), "PlayspaceStrategy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var staged = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count > 100000) throw new InvalidDataException("Слишком много файлов в архиве.");
            var descriptors = zip.Entries.Where(entry => Path.GetFileName(entry.FullName).Equals("descriptor.mod", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (descriptors.Length != 1) throw new InvalidDataException("Нужен ZIP одного мода с единственным descriptor.mod внутри.");
            var descriptor = descriptors[0];
            if (descriptor.Length > 2 * 1024 * 1024) throw new InvalidDataException("Descriptor слишком велик.");
            string metadata; using (var reader = new StreamReader(descriptor.Open())) metadata = reader.ReadToEnd();
            string name = ParadoxDescriptor.Value(metadata, "name") ?? throw new InvalidDataException("Не задано имя мода.");
            string prefix = descriptor.FullName.Replace('\\', '/')[..^"descriptor.mod".Length];
            string id = Guid.NewGuid().ToString("N");
            string destination = "mod/playspace_" + id;
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                string raw = entry.FullName.Replace('\\', '/');
                if (raw.EndsWith('/')) continue;
                ModInstaller.SafePath(staging, raw);
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Ссылки в ZIP не поддерживаются.");
                if (!raw.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string relative = raw[prefix.Length..];
                if (new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".sh" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("Архив содержит установщик или нативный модуль: " + relative);
                if (entry.Length > 1024L * 1024 * 1024 || (total += entry.Length) > 8L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Размер архива превышает лимит 8 ГБ / 1 ГБ на файл.");
                string temporary = ModInstaller.SafePath(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
                if (!files.TryAdd(destination + "/" + relative, temporary)) throw new InvalidDataException("Дублирующий путь в архиве.");
                progress.Report("Подготовка " + relative);
                using var source = entry.Open();
                using var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                staged.Add(temporary);
                var buffer = new byte[81920]; int read; long written = 0;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested(); written += read;
                    if (written > entry.Length) throw new InvalidDataException("Размер файла не соответствует ZIP.");
                    target.Write(buffer, 0, read);
                }
                if (written != entry.Length) throw new InvalidDataException("Неполный файл в ZIP.");
            }
            cancellation.ThrowIfCancellationRequested();
            if (files.Count == 0) throw new InvalidDataException("В архиве не найдены файлы мода.");
            string stagedDescriptor = files[destination + "/descriptor.mod"];
            File.WriteAllText(stagedDescriptor, ParadoxDescriptor.LocalMetadata(metadata), new UTF8Encoding(false));
            string registration = ParadoxDescriptor.Registration(metadata, ModInstaller.SafePath(userRoot, destination));
            string registryTemp = Path.Combine(staging, "registration-" + id + ".mod");
            File.WriteAllText(registryTemp, registration, new UTF8Encoding(false)); staged.Add(registryTemp);
            files.Add("mod/playspace_" + id + ".mod", registryTemp);
            EnsureClosed();
            installer.ApplyStaged(new(new ModPackage { Owner = "Playspace", Name = "PDX_" + id }, new ModVersion { Version = "local" }), files, progress);
            return name;
        }
        finally
        {
            foreach (string file in staged) if (File.Exists(file)) File.Delete(file);
            var parents = staged.SelectMany(file => Parents(file, staging)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(path => path.Length);
            foreach (string directory in parents) if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (Directory.Exists(staging) && !Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
        }
    }
    private static IEnumerable<string> Parents(string file, string root)
    {
        for (string? directory = Path.GetDirectoryName(file); directory != null && directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); directory = Path.GetDirectoryName(directory)) yield return directory;
    }
    private static void WriteJson(string path, object value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string? DirectExecutable(GameInfo game)
    {
        var adapter = StrategyGameAdapter.For(game);
        if (adapter == null) return null;
        var manager = new StrategyModsManager(adapter, game);
        return manager.PrepareDirectExecutable();
    }
    internal string? PrepareDirectExecutable()
    {
        var manager = this;
        if (!File.Exists(manager.ProfileFile)) return null;
        manager.EnsureClosed();
        using var guard = manager.ProfileLock();
        var profile = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(manager.ProfileFile)) ?? throw new InvalidDataException("Повреждён профиль модов.");
        foreach (string reference in profile.Enabled)
        {
            string descriptor = ModInstaller.SafePath(manager.userRoot, reference);
            if (!File.Exists(descriptor)) throw new FileNotFoundException("В профиле отсутствует мод: " + reference);
            string content = ResolveContent(ReadDescriptor(descriptor), manager.userRoot);
            if (!Directory.Exists(content) && !File.Exists(content)) throw new FileNotFoundException("Файлы мода отсутствуют: " + reference);
        }
        string executable = adapter.FindExecutable(game) ?? throw new FileNotFoundException("Не найден исполняемый файл стратегии.");
        var json = manager.LoadJson(); json["enabled_mods"] = JArray.FromObject(profile.Enabled); WriteJson(manager.LoadFile, json);
        return executable;
    }
}
