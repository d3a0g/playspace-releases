using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record ModSelection(ModPackage Package, ModVersion Version)
{
    public string FullId => Package.Id + "-" + Version.Version;
}
internal sealed class InstalledMod
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public List<string> Files { get; set; } = new();
    public override string ToString() => Id + " · " + Version;
}
internal sealed class ManagedModFile
{
    public string Hash { get; set; } = "";
    public bool Created { get; set; }
}
internal sealed class ModState
{
    public string Transaction { get; set; } = "";
    public Dictionary<string, InstalledMod> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ManagedModFile> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
internal sealed class ModChange
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public string? Backup { get; set; }
}
internal sealed class ModJournal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<ModChange> Changes { get; set; } = new();
}

internal sealed class ModInstaller
{
    private readonly string root;
    private readonly string stateRoot;
    private string StateFile => Path.Combine(stateRoot, "installed.json");
    private string JournalFile => Path.Combine(stateRoot, "pending.json");
    public ModInstaller(string gameRoot)
    {
        root = Path.GetFullPath(gameRoot);
        stateRoot = SafePath(root, ".playspace-mods");
    }

    internal static string SafePath(string root, string relative)
    {
        relative = relative.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Contains(':') ||
            relative.Split('/').Any(part => string.IsNullOrEmpty(part) || part is ".." or "." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Недопустимый путь в архиве/журнале: " + relative);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь выходит за каталог игры.");
        for (string? node = path; node != null; node = Path.GetDirectoryName(node))
        {
            if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Папки-ссылки не поддерживаются: " + node);
            if (node.TrimEnd(Path.DirectorySeparatorChar).Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    public ModState ReadState()
    {
        if (!Directory.Exists(stateRoot)) return new();
        using var guard = Lock();
        Recover();
        ModProfileStore.Recover(root);
        return LoadState();
    }
    private ModState LoadState() => File.Exists(StateFile) ? JsonConvert.DeserializeObject<ModState>(File.ReadAllText(StateFile))
        ?? throw new InvalidDataException("Повреждён список установленных модов.") : new();

    public static List<ModSelection> Resolve(ModPackage selected, IReadOnlyList<ModPackage> catalog, ModGameAdapter adapter, ModState installed)
    {
        var versions = new Dictionary<string, ModSelection>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in catalog)
        foreach (var version in package.Versions.Where(v => v.Active))
            versions.TryAdd(package.Id + "-" + version.Version, new(package, version));
        var result = new List<ModSelection>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Visit(ModSelection item)
        {
            if (chosen.TryGetValue(item.Package.Id, out var version))
            {
                if (version != item.Version.Version) throw new InvalidDataException("Конфликт версий зависимости: " + item.Package.Id);
                return;
            }
            if (!visiting.Add(item.FullId)) throw new InvalidDataException("Циклическая зависимость: " + item.FullId);
            if (item.Package.Deprecated || item.Package.Nsfw || item.Package.Categories.Contains("Modpacks", StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Устаревшие пакеты и модпаки не устанавливаются автоматически: " + item.Package.Id);
            foreach (var dependency in item.Version.Dependencies)
            {
                if (!versions.TryGetValue(dependency, out var required)) throw new InvalidDataException("Зависимость недоступна: " + dependency);
                Visit(required);
            }
            visiting.Remove(item.FullId);
            chosen[item.Package.Id] = item.Version.Version;
            if (installed.Packages.TryGetValue(item.Package.Id, out var old))
            {
                if (old.Version != item.Version.Version) throw new InvalidDataException("Уже установлена другая версия " + old.Id + ". Сначала удали зависящие от неё моды.");
            }
            else result.Add(item);
            if (chosen.Count > 64) throw new InvalidDataException("Слишком много зависимостей: нужен специализированный менеджер.");
        }
        var loader = catalog.FirstOrDefault(p => p.Id == adapter.Loader);
        if (loader?.Latest == null) throw new InvalidDataException("В каталоге не найден загрузчик " + adapter.Loader);
        // Let explicit dependencies choose their exact loader version first.
        Visit(new(selected, selected.Latest ?? throw new InvalidDataException("Нет активной версии.")));
        if (!chosen.ContainsKey(adapter.Loader))
        {
            if (installed.Packages.TryGetValue(adapter.Loader, out var existing) &&
                versions.TryGetValue(adapter.Loader + "-" + existing.Version, out var existingLoader)) Visit(existingLoader);
            else Visit(new(loader, loader.Latest));
        }
        return result;
    }

    internal static Dictionary<string, byte[]> MapArchive(ModSelection mod, byte[] archive, ModGameAdapter adapter)
    {
        if (!Regex.IsMatch(mod.Package.Id, @"^[A-Za-z0-9_]+-[A-Za-z0-9_]+$")) throw new InvalidDataException("Недопустимое имя пакета.");
        bool loader = mod.Package.Id == adapter.Loader;
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        if (zip.Entries.Count > 4096) throw new InvalidDataException("Слишком много файлов в пакете.");
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            string name = entry.FullName.Replace('\\', '/');
            SafePath(Path.GetTempPath(), name); // Validate traversal before applying any folder mapping.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Ссылки в архивах запрещены.");
            string leaf = Path.GetFileName(name);
            if (!name.Contains('/') && (leaf.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("icon.png", StringComparison.OrdinalIgnoreCase) || leaf.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                leaf.StartsWith("license", StringComparison.OrdinalIgnoreCase))) continue;
            string destination;
            if (adapter.IsUnreal)
            {
                string? mapped = UnrealModLayout.MapFile(mod, name, adapter);
                if (mapped == null) continue;
                destination = mapped;
            }
            else if (loader)
            {
                var parts = name.Split('/');
                if (parts[0].StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase)) name = string.Join('/', parts.Skip(1));
                if (name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || name.StartsWith("doorstop_libs/", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
                    new[] { "changelog.txt", "readme.txt", "license.txt", "credits.txt", "steam_appid.txt" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (!(name.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase) || name.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("version.dll", StringComparison.OrdinalIgnoreCase) ||
                    (name.StartsWith("dotnet/",StringComparison.OrdinalIgnoreCase)&&(new[]{".dll",".json",".txt",".version",".pdb"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase)||name.Equals("dotnet/dotnet.exe",StringComparison.OrdinalIgnoreCase)||name.Equals("dotnet/.version",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(name).Equals("LICENSE",StringComparison.OrdinalIgnoreCase))) ||
                    (adapter.Community == "rounds" && name.StartsWith("corlibs/", StringComparison.OrdinalIgnoreCase)) ||
                    name.Equals(".doorstop_version", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Неподдерживаемый файл загрузчика: " + name);
                destination = name;
            }
            else
            {
                if (name.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) name = name[8..];
                var parts = name.Split('/');
                string[] routed = { "plugins", "config", "patchers", "monomod" };
                int route = Array.FindIndex(parts, part => routed.Contains(part, StringComparer.OrdinalIgnoreCase));
                if (route >= 0 && route < parts.Length - 1)
                {
                    string suffix = string.Join('/', parts.Skip(route + 1));
                    destination = parts[route].Equals("config", StringComparison.OrdinalIgnoreCase)
                        ? "BepInEx/config/" + suffix : $"BepInEx/{parts[route]}/{mod.Package.Id}/{suffix}";
                }
                else destination = "BepInEx/plugins/" + mod.Package.Id + "/" + name;
                if (new[] { ".exe", ".bat", ".cmd", ".ps1", ".sh" }.Contains(Path.GetExtension(destination), StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("Пакет требует выполнения установщика: " + leaf);
            }
            if (entry.Length > 64L * 1024 * 1024 || (total += entry.Length) > (loader?256L:128L) * 1024 * 1024)
                throw new InvalidDataException("Распакованный пакет превышает лимит размера.");
            using var source = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > entry.Length || output.Length + read > 64L * 1024 * 1024)
                    throw new InvalidDataException("Размер файла не соответствует архиву.");
                output.Write(buffer, 0, read);
            }
            if (output.Length != entry.Length) throw new InvalidDataException("Неполный файл в архиве.");
            if (!files.TryAdd(destination, output.ToArray())) throw new InvalidDataException("Дублирующий путь в архиве: " + destination);
        }
        if (files.Count == 0) throw new InvalidDataException("В пакете нет устанавливаемых файлов.");
        return files;
    }

    public async Task InstallAsync(List<ModSelection> plan, ModGameAdapter adapter, ModCatalog catalog, IProgress<string> progress, CancellationToken cancellation)
    {
        var prepared = new List<(ModSelection Mod, Dictionary<string, byte[]> Files)>();
        long total = 0;
        foreach (var mod in plan)
        {
            progress.Report("Загрузка " + mod.FullId);
            byte[] archive = await catalog.DownloadAsync(mod.Version.DownloadUrl, cancellation,"Thunderstore · "+mod.FullId);
            var files = await Task.Run(() => MapArchive(mod, archive, adapter), cancellation);
            if ((total += files.Values.Sum(data => (long)data.Length)) > 256L * 1024 * 1024) throw new InvalidDataException("Суммарный размер превышает 256 МБ.");
            prepared.Add((mod, files));
        }
        cancellation.ThrowIfCancellationRequested();
        await Task.Run(() => Apply(prepared, progress)); // Commit is non-cancellable; it either completes or rolls back.
    }

    internal void Apply(List<(ModSelection Mod, Dictionary<string, byte[]> Files)> prepared, IProgress<string> progress)
        => ApplyCore(prepared.Select(item => (item.Mod, item.Files.ToDictionary(pair => pair.Key,
            pair => new InstallSource(pair.Value, null), StringComparer.OrdinalIgnoreCase))).ToList(), progress);

    internal void ApplyStaged(ModSelection mod, Dictionary<string, string> files, IProgress<string> progress)
        => ApplyCore(new() { (mod, files.ToDictionary(pair => pair.Key, pair => new InstallSource(null, pair.Value), StringComparer.OrdinalIgnoreCase)) }, progress);

    private sealed record InstallSource(byte[]? Bytes, string? FilePath)
    {
        public string Digest => Bytes != null ? Hash(Bytes) : FileHash(FilePath!);
        public void Write(Stream target)
        {
            if (Bytes != null) target.Write(Bytes);
            else { using var source = File.OpenRead(FilePath!); source.CopyTo(target); }
        }
    }

    private void ApplyCore(List<(ModSelection Mod, Dictionary<string, InstallSource> Files)> prepared, IProgress<string> progress)
    {
        using var guard = Lock(); Recover(); ModProfileStore.Recover(root);
        var state = LoadState();
        var writes = new Dictionary<string, InstallSource>(StringComparer.OrdinalIgnoreCase);
        var journal = new ModJournal();
        foreach (var (mod, files) in prepared)
        {
            if (state.Packages.ContainsKey(mod.Package.Id)) throw new IOException("Список модов изменился. Обнови каталог.");
            foreach (var (relative, bytes) in files)
            {
                string path = SafePath(root, relative), hash = bytes.Digest;
                if (writes.TryGetValue(relative, out var previous) && previous.Digest != hash) throw new IOException("Конфликт файлов: " + relative);
                if (state.Files.TryGetValue(relative, out var tracked) && tracked.Hash != hash) throw new IOException("Другому установленному моду нужна иная версия файла: " + relative);
                if (File.Exists(path) && FileHash(path) != hash) throw new IOException("Существующий файл не будет перезаписан: " + relative);
                bool exists = File.Exists(path);
                if (!state.Files.ContainsKey(relative)) state.Files[relative] = new ManagedModFile { Hash = hash, Created = !exists };
                else if (!exists) state.Files[relative].Created = true;
                if (!exists && !writes.ContainsKey(relative)) journal.Changes.Add(new ModChange { Path = relative, Hash = hash });
                writes[relative] = bytes;
            }
            state.Packages.Add(mod.Package.Id, new InstalledMod { Id = mod.Package.Id, Version = mod.Version.Version,
                Dependencies = mod.Version.Dependencies, Files = files.Keys.ToList() });
        }
        WriteJson(JournalFile, journal);
        try
        {
            foreach (var (relative, bytes) in writes)
            {
                string path = SafePath(root, relative);
                if (File.Exists(path)) continue;
                progress.Report("Установка " + relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                bytes.Write(stream); stream.Flush(true);
            }
            state.Transaction = journal.Id; WriteJson(StateFile, state);
            Cleanup(journal);
        }
        catch { Recover(); throw; }
    }

    public void Uninstall(string id)
    {
        using var guard = Lock(); Recover(); ModProfileStore.Recover(root);
        var state = LoadState();
        if (!state.Packages.TryGetValue(id, out var package)) return;
        var frameworks = ModGameAdapter.All.Select(adapter => adapter.Loader).Append(UnrealModLayout.Shim).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (frameworks.Contains(id) && state.Packages.Keys.Any(other => other != id && !frameworks.Contains(other)))
            throw new IOException("Сначала удали остальные моды: им нужен этот загрузчик.");
        var dependants = state.Packages.Values.Where(p => p.Id != id && p.Dependencies.Contains(id + "-" + package.Version)).ToArray();
        if (dependants.Length > 0) throw new IOException("Сначала удали зависящие моды: " + string.Join(", ", dependants.Select(p => p.Id)));
        var journal = new ModJournal();
        var otherPaths = state.Packages.Values.Where(p => p.Id != id).SelectMany(p => p.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in package.Files.Where(path => !otherPaths.Contains(path)))
        {
            string path = SafePath(root, relative);
            if (!state.Files.TryGetValue(relative, out var managed) || !managed.Created || !File.Exists(path)) continue;
            string currentHash=FileHash(path);
            if (currentHash != managed.Hash)
            {
                if(id==OptiScalerManager.PackageId&&new[]{"OptiScaler.ini","fakenvapi.ini"}.Contains(relative,StringComparer.OrdinalIgnoreCase)){
                    string saved=SafePath(stateRoot,"preserved/"+journal.Id+"-"+Path.GetFileName(relative));
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);File.Copy(path,saved,false);
                }
                else {
                if (relative.Replace('\\', '/').StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase) ||
                    relative.Replace('\\', '/').StartsWith(".playspace-mods/unreal/cfg/", StringComparison.OrdinalIgnoreCase)) continue;
                throw new IOException("Файл изменён после установки; сохрани его и удали вручную: " + relative);
                }
            }
            string backup = "undo/" + journal.Id + "-" + journal.Changes.Count + ".bin";
            string backupPath = SafePath(stateRoot, backup);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(path, backupPath);
            journal.Changes.Add(new ModChange { Path = relative, Hash = currentHash, Backup = backup });
        }
        WriteJson(JournalFile, journal);
        try
        {
            foreach (var change in journal.Changes) File.Delete(SafePath(root, change.Path));
            state.Packages.Remove(id);
            foreach (string relative in package.Files.Where(path => !otherPaths.Contains(path))) state.Files.Remove(relative);
            state.Transaction = journal.Id; WriteJson(StateFile, state); Cleanup(journal);
        }
        catch { Recover(); throw; }
    }

    private FileStream Lock()
    {
        SafePath(root, ".playspace-mods"); Directory.CreateDirectory(stateRoot);
        return new FileStream(SafePath(stateRoot, "manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void Recover()
    {
        if (!File.Exists(JournalFile)) return;
        var journal = JsonConvert.DeserializeObject<ModJournal>(File.ReadAllText(JournalFile)) ?? throw new InvalidDataException("Повреждён журнал модов.");
        if (LoadState().Transaction != journal.Id)
        foreach (var change in journal.Changes)
        {
            string path = SafePath(root, change.Path);
            if (change.Backup == null)
            {
                if (File.Exists(path))
                {
                    if (FileHash(path) != change.Hash) throw new IOException("Незавершённая установка: файл изменён, требуется ручное восстановление: " + change.Path);
                    File.Delete(path);
                }
            }
            else
            {
                string backup = SafePath(stateRoot, change.Backup);
                if (FileHash(backup) != change.Hash) throw new InvalidDataException("Повреждена копия отката.");
                if (File.Exists(path) && FileHash(path) != change.Hash) throw new IOException("Файл изменён во время удаления: " + change.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(backup, path, true);
            }
        }
        Cleanup(journal);
    }
    private void Cleanup(ModJournal journal)
    {
        foreach (var change in journal.Changes.Where(c => c.Backup != null)) File.Delete(SafePath(stateRoot, change.Backup!));
        File.Delete(JournalFile);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void WriteJson(string path, object value)
    {
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
            writer.Write(JsonConvert.SerializeObject(value, Formatting.Indented)); writer.Flush(); stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }
}
