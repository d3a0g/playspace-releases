using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GameLauncherGUI;
using Newtonsoft.Json;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException) { checks++; return; }
        throw new Exception(message);
    }
    private static byte[] Zip(params (string Path, string Text)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files)
            { using var writer = new StreamWriter(zip.CreateEntry(file.Path).Open()); writer.Write(file.Text); }
        return stream.ToArray();
    }
    private static ModPackage Package(string owner, string name, params string[] dependencies) => new() {
        Owner = owner, Name = name, Versions = new() { new ModVersion { Version = "1.0.0", Dependencies = dependencies } }
    };
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--public-screenshots")){PublicScreenshots.Run();return;}
        if(args.Contains("--verify-universal-dlss5")){UniversalDlss5Checks.VerifyRealPackage();return;}
        if(args.Contains("--network-dlss"))
        {
            string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceDlssVerified-"+Guid.NewGuid().ToString("N"));
            string file=DlssManager.Download(DlssLibrary.All[0],folder,default).GetAwaiter().GetResult();
            var info=System.Diagnostics.FileVersionInfo.GetVersionInfo(file);
            if(!info.CompanyName?.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase)??true)throw new Exception("Unexpected DLSS publisher");
            Console.WriteLine($"PASS: official NVIDIA download and Authenticode signature; DLL {info.FileVersion}; {new FileInfo(file).Length} bytes. No game files changed.");return;
        }
        if(args.Contains("--network-update"))
        {
            using var client=new System.Net.Http.HttpClient {Timeout=TimeSpan.FromSeconds(30)};
            string feed=UpdateService.BundledFeed();
            var newer=UpdateService.Check(client,feed,new Version(1,0,0),default).GetAwaiter().GetResult();
            if(newer==null||newer.ParsedVersion!=UpdateService.CurrentVersion)throw new Exception("Published update not detected by real client");
            if(UpdateService.Check(client,feed,UpdateService.CurrentVersion,default).GetAwaiter().GetResult()!=null)throw new Exception("Current client incorrectly offered the same update");
            Console.WriteLine($"PASS: real anonymous update feed; version 1.0.0 detects {UpdateService.DisplayVersion}, current client receives no update. No notification or download launched.");
            return;
        }
        var adapter = ModGameAdapter.All[0];
        string root = Path.Combine(Path.GetTempPath(), "PlayspaceModsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "REPO.exe"), "test fixture, not executable");
        Directory.CreateDirectory(Path.Combine(root, "REPO_Data"));
        var game = new GameInfo { Name = "R.E.P.O.", SteamAppId = "3241660", ExePath = Path.Combine(root, "REPO.exe") };
        Check(adapter.FindRoot(game) == root, "Portable install path failed");
        Check(ModGameAdapter.For(game) == adapter, "Steam app matching failed");
        Check(ModGameAdapter.For(new GameInfo { Name = "Fake REPO", ExePath = "Other.exe" }) == null, "Name-only false match");
        Check(ModGameAdapter.For(new GameInfo { Name = "VALORANT", SteamAppId = "1273", ExePath = "VALORANT.exe" }) == null, "Unsupported game accepted");
        var loader = Package("BepInEx", "BepInExPack");
        var dependency = Package("Test", "Library", loader.Id + "-1.0.0");
        var mod = Package("Test", "Mod", dependency.Id + "-1.0.0");
        var catalog = new[] { mod, dependency, loader };
        var plan = ModInstaller.Resolve(mod, catalog, adapter, new ModState());
        Check(plan.Select(p => p.Package.Id).SequenceEqual(new[] { loader.Id, dependency.Id, mod.Id }), "Dependency order failed");
        var cycleA = Package("Test", "A", "Test-B-1.0.0"); var cycleB = Package("Test", "B", "Test-A-1.0.0");
        Reject(() => ModInstaller.Resolve(cycleA, new[] { cycleA, cycleB, loader }, adapter, new()), "Dependency cycle allowed");
        var unavailable = Package("Test", "Missing", "Test-NoSuchPackage-1.0.0");
        Reject(() => ModInstaller.Resolve(unavailable, new[] { unavailable, loader }, adapter, new()), "Missing dependency allowed");
        foreach (string path in new[] { "../escape.dll", "C:/escape.dll", "plugins/../../escape.dll", "plugins/file.dll:stream", "plugins/../evil" })
            Reject(() => ModInstaller.MapArchive(new(mod, mod.Latest!), Zip((path, "x")), adapter), "Traversal allowed: " + path);
        Reject(() => ModInstaller.MapArchive(new(mod, mod.Latest!), Zip(("install.exe", "x")), adapter), "Executable installer allowed");
        Reject(() => ModInstaller.MapArchive(new(mod, mod.Latest!), Zip(("plugins/a.dll", "a"), ("plugins/A.dll", "b")), adapter), "Case collision allowed");
        var loaderFiles = ModInstaller.MapArchive(new(loader, loader.Latest!), Zip(("manifest.json", "{}"),
            ("BepInExPack/winhttp.dll", "loader"), ("BepInExPack/doorstop_config.ini", "doorstop"),
            ("BepInExPack/BepInEx/core/core.dll", "core"), ("BepInExPack/start_game_bepinex.sh", "linux")), adapter);
        Check(loaderFiles.Count == 3 && loaderFiles.ContainsKey("winhttp.dll"), "Loader layout mapping failed");
        var pluginFiles = ModInstaller.MapArchive(new(mod, mod.Latest!), Zip(("Mod.dll", "plugin"), ("BepInEx/config/test.cfg", "config")), adapter);
        Check(pluginFiles.ContainsKey("BepInEx/plugins/Test-Mod/Mod.dll") && pluginFiles.ContainsKey("BepInEx/config/test.cfg"), "Plugin/config routing failed");
        var dependencyFiles = ModInstaller.MapArchive(new(dependency, dependency.Latest!), Zip(("plugins/Library.dll", "lib")), adapter);
        var installer = new ModInstaller(root);
        var progress = new Progress<string>();
        installer.Apply(new() { (new(loader, loader.Latest!), loaderFiles), (new(dependency, dependency.Latest!), dependencyFiles), (new(mod, mod.Latest!), pluginFiles) }, progress);
        Check(installer.ReadState().Packages.Count == 3, "Installed package ledger missing");
        Check(File.ReadAllText(Path.Combine(root, "BepInEx/plugins/Test-Mod/Mod.dll")) == "plugin", "Plugin wasn't installed");
        Reject(() => installer.Uninstall(loader.Id), "Loader removed with dependants");
        Reject(() => installer.Uninstall(dependency.Id), "Dependency removed while needed");
        string pluginPath = Path.Combine(root, "BepInEx/plugins/Test-Mod/Mod.dll");
        File.WriteAllText(pluginPath, "user edit");
        Reject(() => installer.Uninstall(mod.Id), "Modified mod file silently deleted");
        Check(File.ReadAllText(pluginPath) == "user edit", "User edit lost");
        File.WriteAllText(pluginPath, "plugin");
        string configPath = Path.Combine(root, "BepInEx/config/test.cfg");
        File.WriteAllText(configPath, "user preferences");
        installer.Uninstall(mod.Id); installer.Uninstall(dependency.Id); installer.Uninstall(loader.Id);
        Check(File.ReadAllText(configPath) == "user preferences", "Changed configuration deleted");
        Check(installer.ReadState().Packages.Count == 0 && !File.Exists(pluginPath) && !File.Exists(Path.Combine(root, "winhttp.dll")), "Uninstall incomplete");
        Check(File.ReadAllText(Path.Combine(root, "REPO.exe")) == "test fixture, not executable", "Original game file changed");
        var conflict = Package("Test", "Conflict");
        Reject(() => installer.Apply(new() { (new(conflict, conflict.Latest!), new() { ["REPO.exe"] = Encoding.UTF8.GetBytes("overwrite") }) }, progress), "Original overwritten");
        Check(installer.ReadState().Packages.Count == 0, "Failed transaction updated ledger");
        // Recovery of an interrupted install removes only the newly-created recorded file.
        string interrupted = "BepInEx/plugins/interrupted.dll";
        string interruptedPath = Path.Combine(root, interrupted);
        Directory.CreateDirectory(Path.GetDirectoryName(interruptedPath)!); File.WriteAllText(interruptedPath, "incomplete transaction");
        var journal = new ModJournal { Changes = new() { new ModChange { Path = interrupted,
            Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(interruptedPath))) } } };
        File.WriteAllText(Path.Combine(root, ".playspace-mods/pending.json"), JsonConvert.SerializeObject(journal));
        installer.ReadState();
        Check(!File.Exists(interruptedPath), "Interrupted install not rolled back");
        // A file already present with identical contents is shared but never owned/deleted.
        string shared = "BepInEx/plugins/shared.dll"; File.WriteAllText(Path.Combine(root, shared), "shared");
        installer.Apply(new() { (new(conflict, conflict.Latest!), new() { [shared] = Encoding.UTF8.GetBytes("shared") }) }, progress);
        installer.Uninstall(conflict.Id);
        Check(File.Exists(Path.Combine(root, shared)), "Pre-existing shared file deleted");
        var second = Package("Test", "Second");
        string ownedShared = "BepInEx/plugins/owned-shared.dll";
        var sharedContents = new Dictionary<string, byte[]> { [ownedShared] = Encoding.UTF8.GetBytes("owned shared") };
        installer.Apply(new() { (new(conflict, conflict.Latest!), sharedContents), (new(second, second.Latest!), sharedContents) }, progress);
        installer.Uninstall(conflict.Id);
        Check(File.Exists(Path.Combine(root, ownedShared)), "Shared dependency file removed too early");
        installer.Uninstall(second.Id);
        Check(!File.Exists(Path.Combine(root, ownedShared)), "Orphan owned file not removed");
        File.WriteAllText(Path.Combine(root, "blocked"), "not a directory");
        string rollback = "BepInEx/plugins/rollback/new.dll";
        Reject(() => installer.Apply(new() { (new(conflict, conflict.Latest!), new() {
            [rollback] = Encoding.UTF8.GetBytes("new"), ["blocked/second.dll"] = Encoding.UTF8.GetBytes("blocked") }) }, progress), "Failed write did not fail");
        Check(!File.Exists(Path.Combine(root, rollback)) && !installer.ReadState().Packages.ContainsKey(conflict.Id), "Failed install not rolled back");
        string restorePath = "BepInEx/plugins/restore.dll";
        byte[] restoreBytes = Encoding.UTF8.GetBytes("undo delete");
        string backupRelative = "undo/crash.bin";
        Directory.CreateDirectory(Path.Combine(root, ".playspace-mods/undo"));
        File.WriteAllBytes(Path.Combine(root, ".playspace-mods", backupRelative), restoreBytes);
        var deleteJournal = new ModJournal { Changes = new() { new ModChange { Path = restorePath,
            Backup = backupRelative, Hash = Convert.ToHexString(SHA256.HashData(restoreBytes)) } } };
        File.WriteAllText(Path.Combine(root, ".playspace-mods/pending.json"), JsonConvert.SerializeObject(deleteJournal));
        installer.ReadState();
        Check(File.ReadAllBytes(Path.Combine(root, restorePath)).SequenceEqual(restoreBytes), "Interrupted deletion not restored");
        var unreal = ModGameAdapter.All.First(a => a.Community == "meccha-chameleon");
        string unrealRoot = Path.Combine(root, "unreal-game");
        Directory.CreateDirectory(Path.Combine(unrealRoot, "Chameleon/Binaries/Win64"));
        File.WriteAllText(Path.Combine(unrealRoot, "PenguinHotel.exe"), "bootstrap fixture");
        File.WriteAllText(Path.Combine(unrealRoot, "Chameleon/Binaries/Win64/PenguinHotel-Win64-Shipping.exe"), "shipping fixture");
        var unrealGame = new GameInfo { Name = unreal.Name, SteamAppId = unreal.SteamId,
            ExePath = Path.Combine(unrealRoot, "Chameleon/Binaries/Win64/PenguinHotel-Win64-Shipping.exe") };
        Check(unreal.FindRoot(unrealGame) == unrealRoot, "Unreal install root wasn't found above shipping exe");
        Check(unreal.FindRoot(new GameInfo { Name = unreal.Name, SteamAppId = unreal.SteamId,
            ExePath = "steam://rungameid/4704690", InstallDirectory = unrealRoot }) == unrealRoot, "Unreal Steam URI install root failed");
        var lua = Package("Test", "LuaMod");
        var luaFiles = ModInstaller.MapArchive(new(lua, lua.Latest!), Zip(("mod/Scripts/main.lua", "lua"), ("mod/enabled.txt", "")), unreal);
        Check(luaFiles.ContainsKey(".playspace-mods/unreal/mods/LuaMod/Scripts/main.lua"), "Canonical UE4SS mod mapping failed");
        var manualLua = ModInstaller.MapArchive(new(lua, lua.Latest!), Zip(("Mods/NamedMod/Scripts/main.lua", "lua")), unreal);
        Check(manualLua.ContainsKey(".playspace-mods/unreal/mods/NamedMod/Scripts/main.lua"), "Manual Mods layout failed");
        var rootLua = ModInstaller.MapArchive(new(lua, lua.Latest!), Zip(("Scripts/main.lua", "lua"), ("enabled.txt", "")), unreal);
        Check(rootLua.ContainsKey(".playspace-mods/unreal/mods/LuaMod/Scripts/main.lua"), "Root Scripts layout failed");
        var pak = ModInstaller.MapArchive(new(lua, lua.Latest!), Zip(("Chameleon-Windows_P.pak", "pak"),
            ("Chameleon-Windows_P.ucas", "ucas"), ("Chameleon-Windows_P.utoc", "utoc"), ("CHANGELOG.txt", "docs")), unreal);
        Check(pak.Count == 3 && pak.ContainsKey("Chameleon/Content/Paks/Chameleon-Windows_P.utoc"), "IoStore patch mapping failed");
        var shim = Package("Thunderstore", "unreal_shimloader");
        var overlay = Package("Thunderstore", "MecchaChameleon_UE4SS", shim.Id + "-1.0.0");
        var uePlan = ModInstaller.Resolve(lua, new[] { lua, shim, overlay }, unreal, new());
        Check(uePlan.Count == 3, "Implicit UE4SS dependencies not included");
        var shimFiles = ModInstaller.MapArchive(new(shim, shim.Latest!), Zip(("dwmapi.dll", "shim"), ("UE4SS/UE4SS.dll", "core")), unreal);
        Check(shimFiles.ContainsKey("Chameleon/Binaries/Win64/UE4SS.dll"), "UE4SS core must be next to Shipping exe");
        var overlayFiles = ModInstaller.MapArchive(new(overlay, overlay.Latest!), Zip(("overlay/ue4ss.dll", "special core"),
            ("overlay/UE4SS_Signatures/StaticConstructObject.lua", "signature")), unreal);
        Check(overlayFiles.Keys.All(path => path.StartsWith(".playspace-mods/unreal/overlay/Thunderstore-MecchaChameleon_UE4SS/")), "Community overlay routing failed");
        var unrealInstaller = new ModInstaller(unrealRoot);
        unrealInstaller.Apply(new() { (new(shim, shim.Latest!), shimFiles), (new(overlay, overlay.Latest!), overlayFiles), (new(lua, lua.Latest!), luaFiles) }, progress);
        var launchArgs = UnrealModLayout.LaunchArguments(unrealGame);
        Check(launchArgs.Length == 8 && launchArgs.Contains("--overlay-dir"), "Unreal launch profile arguments missing");
        var steamStart = UnrealModLayout.SteamStart(@"C:\Steam Folder\steam.exe", "4704690", launchArgs);
        Check(steamStart.ArgumentList[0] == "-applaunch" && steamStart.ArgumentList[1] == "4704690" && steamStart.ArgumentList.Count == 10,
            "Steam launch argument boundaries lost");
        Reject(() => ModInstaller.MapArchive(new(lua, lua.Latest!), Zip(("Mods/../../escape.lua", "bad")), unreal), "Unreal path traversal allowed");
        Reject(() => ModInstaller.MapArchive(new(lua, lua.Latest!), Zip(("install.ps1", "bad")), unreal), "Unreal script installer allowed");
        Reject(() => unrealInstaller.Uninstall(shim.Id), "UE4SS shim removed while required");
        unrealInstaller.Uninstall(lua.Id); unrealInstaller.Uninstall(overlay.Id); unrealInstaller.Uninstall(shim.Id);
        Check(UnrealModLayout.LaunchArguments(unrealGame).Length == 0, "Unreal launch profile remains active after uninstall");
        Check(File.ReadAllText(Path.Combine(unrealRoot, "PenguinHotel.exe")) == "bootstrap fixture", "Unreal game binary changed");
        Console.WriteLine($"PASS: {checks} adapter, dependency, archive, installation, uninstall and recovery checks. Only temporary fixtures were modified.");
        PerformanceChecks.Run(args.Contains("--ui"));
        LibraryChecks.Run(args.Contains("--ui"));
        DiscoveryChecks.Run(args.Contains("--ui"));
        PreferencesChecks.Run(args.Contains("--ui"));
        UpdateChecks.Run(args.Contains("--ui"));
        RefreshChecks.Run(args.Contains("--ui"));
        SupportBotChecks.Run();
        UniversalDlss5Checks.Run(args.Contains("--ui"));
        StrategyChecks.Run(args.Contains("--ui"));
        if (args.Contains("--ui")) ModUiChecks.Run(root, catalog.ToList());
        if (args.Contains("--network")) Network(adapter).GetAwaiter().GetResult();
        if (args.Contains("--inspect-unreal")) InspectUnreal().GetAwaiter().GetResult();
        if (args.Contains("--network-all"))
        {
            bool failed = false;
            string? only = args.FirstOrDefault(arg => arg.StartsWith("--only="))?[7..];
            foreach (var supported in ModGameAdapter.All.Where(a => only == null || only.Split(',').Contains(a.Community)))
            {
                try { Network(supported).GetAwaiter().GetResult(); }
                catch (Exception ex) { failed = true; Console.WriteLine("LIVE FAIL: " + supported.Name + ": " + ex.Message); }
            }
            if (failed) throw new Exception("One or more live adapters failed validation.");
        }
    }
    private static async Task InspectUnreal()
    {
        var adapter = ModGameAdapter.All.First(a => a.IsUnreal);
        using var service = new ModCatalog();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var catalog = await service.LoadAsync(adapter, cancel.Token);
        foreach (var mod in catalog.Where(p => !p.Deprecated && !p.Nsfw && p.Latest != null))
        {
            Console.WriteLine("PACKAGE " + mod.Id + " " + mod.Latest!.Version + " DEPS " + string.Join(",", mod.Latest.Dependencies));
            byte[] bytes = await service.DownloadAsync(mod.Latest.DownloadUrl, cancel.Token);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')).Take(55)) Console.WriteLine("  " + entry.FullName);
        }
    }
    private static async Task Network(ModGameAdapter adapter)
    {
        using var service = new ModCatalog();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var catalog = await service.LoadAsync(adapter, timeout.Token);
        var loader = catalog.First(p => p.Id == adapter.Loader);
        var archive = await service.DownloadAsync(loader.Latest!.DownloadUrl, timeout.Token);
        var files = ModInstaller.MapArchive(new(loader, loader.Latest), archive, adapter);
        Check(adapter.IsUnreal ? files.Keys.Any(path => path.Contains("/overlay/", StringComparison.OrdinalIgnoreCase) || path.EndsWith("dwmapi.dll")) :
            files.ContainsKey("winhttp.dll") || files.ContainsKey("version.dll"), "Live loader layout incompatible");
        Console.WriteLine($"LIVE PASS: {adapter.Name}: {catalog.Count} packages; loader {loader.Latest.Version}, {files.Count} mapped files. Downloaded and inspected only; not installed.");
        if (adapter.Community == "repo")
        {
            var library = catalog.First(p => p.Id == "Zehs-REPOLib");
            var plan = ModInstaller.Resolve(library, catalog, adapter, new());
            var packageArchive = await service.DownloadAsync(library.Latest!.DownloadUrl, timeout.Token);
            var mapped = ModInstaller.MapArchive(new(library, library.Latest), packageArchive, adapter);
            Check(mapped.Keys.Any(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)), "Live plugin archive routing failed");
            Console.WriteLine($"LIVE PASS: REPOLib {library.Latest.Version}: {plan.Count} packages in dependency plan, {mapped.Count} mapped files; not installed.");
        }
        if (adapter.Community == "meccha-chameleon")
        {
            string root = Path.Combine(Path.GetTempPath(), "PlayspaceUnrealLive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Chameleon/Binaries/Win64"));
            File.WriteAllText(Path.Combine(root, "PenguinHotel.exe"), "fixture");
            File.WriteAllText(Path.Combine(root, "Chameleon/Binaries/Win64/PenguinHotel-Win64-Shipping.exe"), "fixture");
            var installer = new ModInstaller(root);
            foreach (var package in catalog.Where(p => !p.Deprecated && !p.Nsfw && p.Latest != null))
            {
                byte[] bytes = await service.DownloadAsync(package.Latest!.DownloadUrl, timeout.Token);
                var paths = ModInstaller.MapArchive(new(package, package.Latest), bytes, adapter);
                Console.WriteLine("  VERIFIED " + package.Id + " -> " + paths.Count + " files");
            }
            var performance = catalog.First(p => p.Id == "cmdaeo-MecchaChameleonPerformance");
            var plan = ModInstaller.Resolve(performance, catalog, adapter, new());
            var staged = new List<(ModSelection Mod, Dictionary<string, byte[]> Files)>();
            foreach (var item in plan)
            {
                byte[] bytes = await service.DownloadAsync(item.Version.DownloadUrl, timeout.Token);
                staged.Add((item, ModInstaller.MapArchive(item, bytes, adapter)));
            }
            installer.Apply(staged, new Progress<string>());
            var game = new GameInfo { Name = adapter.Name, SteamAppId = adapter.SteamId, ExePath = Path.Combine(root, "PenguinHotel.exe") };
            Check(UnrealModLayout.LaunchArguments(game).Length == 8, "Live UE4SS launch profile missing");
            Check(File.Exists(Path.Combine(root, ".playspace-mods/unreal/mods/MecchaChameleonPerformance/Scripts/main.lua")), "Performance mod not routed to UE4SS mods path");
            installer.Uninstall(performance.Id); installer.Uninstall(adapter.Loader); installer.Uninstall(UnrealModLayout.Shim);
            Check(!File.Exists(Path.Combine(root, "Chameleon/Binaries/Win64/dwmapi.dll")), "Shim not removed from temporary fixture");
            Console.WriteLine("LIVE PASS: all Meccha Chameleon archives mapped; real performance-mod dependencies installed/removed ONLY in temporary fixture; no game was launched.");
        }
    }
}
