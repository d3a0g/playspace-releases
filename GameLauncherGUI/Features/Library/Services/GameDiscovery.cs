using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Newtonsoft.Json;
namespace GameLauncherGUI.Features.Library.Services;

internal static class GameDiscovery
{
    internal static List<GameInfo> Scan()
    {
        var games = FindSteamGames();
        games.AddRange(FindEpicGames());
        games.AddRange(FindRiotGames());
        games.AddRange(FindPirateGamesDeep());
        return games;
    }
        internal static List<GameInfo> FindEpicGames()
        {
            var games = new List<GameInfo>();
            string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            foreach (string file in EnumerateFilesSafe(manifests, "*.item", 0))
            {
                try
                {
                    var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                    if (manifest.Value<bool?>("bIsIncompleteInstall") == true) continue;
                    string? location = manifest.Value<string>("InstallLocation");
                    string? executable = manifest.Value<string>("LaunchExecutable");
                    if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(executable)) continue;
                    string path = Path.Combine(location, executable);
                    if (File.Exists(path)) games.Add(new GameInfo {
                        Name = manifest.Value<string>("DisplayName") ?? Path.GetFileName(location), ExePath = path, InstallDirectory = location });
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (JsonException) { }
            }
            return games;
        }

        internal static string GetLocalGameName(string executable)
        {
            string name = Path.GetFileNameWithoutExtension(executable);
            if (name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("VALORANT-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) return "VALORANT";
            var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "game", "games", "live", "content", "binaries", "bin", "win32", "win64", "x64", "x86", "launcher" };
            var folder = Directory.GetParent(executable);
            if (folder == null) return name;
            if (!generic.Contains(folder.Name)) return folder.Name;
            if (!generic.Contains(name)) return Regex.Replace(name, "-Win(32|64)-Shipping$", "", RegexOptions.IgnoreCase);
            while (folder != null && generic.Contains(folder.Name)) folder = folder.Parent;
            return folder?.Name ?? name;
        }

        internal static bool ConfigureValorant(GameInfo game)
        {
            if (!game.Name.Equals("VALORANT", StringComparison.OrdinalIgnoreCase)) return false;
            string manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games", "RiotClientInstalls.json");
            try
            {
                if (!File.Exists(manifest)) return false;
                var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));
                var clients = data["associated_client"] as Newtonsoft.Json.Linq.JObject;
                string? client = clients?.Properties().FirstOrDefault(p =>
                    p.Name.Replace('\\', '/').TrimEnd('/').EndsWith("/VALORANT/live", StringComparison.OrdinalIgnoreCase))?.Value.ToString();
                client ??= data.Value<string>("rc_live") ?? data.Value<string>("rc_default");
                if (string.IsNullOrWhiteSpace(client) || !File.Exists(client)) return false;
                game.ExePath = client;
                game.LaunchArguments = "--launch-product=valorant --launch-patchline=live";
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (JsonException) { return false; }
        }

        internal static List<GameInfo> FindRiotGames()
        {
            var games = new List<GameInfo>();
            string manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games", "RiotClientInstalls.json");
            try
            {
                if (!File.Exists(manifest)) return games;
                var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));
                var clients = data["associated_client"] as Newtonsoft.Json.Linq.JObject;
                foreach (var install in clients?.Properties() ?? Enumerable.Empty<Newtonsoft.Json.Linq.JProperty>())
                {
                    if (!install.Name.Replace('\\', '/').TrimEnd('/').EndsWith("/VALORANT/live", StringComparison.OrdinalIgnoreCase)) continue;
                    string executable = Path.Combine(install.Name, "VALORANT.exe");
                    if (!File.Exists(executable)) continue;
                    var game = new GameInfo { Name = "VALORANT", ExePath = executable };
                    if (ConfigureValorant(game)) games.Add(game);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
            return games;
        }

        internal static IEnumerable<string> EnumerateFilesSafe(string folder, string pattern, int depth)
        {
            var options = new EnumerationOptions {
                RecurseSubdirectories = depth > 0, MaxRecursionDepth = depth,
                IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            try { return Directory.GetFiles(folder, pattern, options); }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        internal static List<GameInfo> FindSteamGames()
        {
            var steamGames = new List<GameInfo>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key == null) return steamGames;
                    string steamPath = key.GetValue("SteamPath")?.ToString();
                    if (string.IsNullOrEmpty(steamPath)) return steamGames;

                    var libraryFoldersFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    var steamAppsFolders = new List<string> { Path.Combine(steamPath, "steamapps") };

                    if (File.Exists(libraryFoldersFile))
                    {
                        string content = File.ReadAllText(libraryFoldersFile);
                        foreach (Match match in Regex.Matches(content, @"""path""\s+""([^""]+)"""))
                            steamAppsFolders.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
                    }

                    foreach (var appsPath in steamAppsFolders)
                    {
                        if (!Directory.Exists(appsPath)) continue;
                        foreach (var manifest in Directory.GetFiles(appsPath, "appmanifest_*.acf"))
                        {
                            try
                            {
                                string acfContent = File.ReadAllText(manifest);
                                var nameMatch = Regex.Match(acfContent, @"""name""\s+""([^""]+)""");
                                var dirMatch = Regex.Match(acfContent, @"""installdir""\s+""([^""]+)""");
                                var appIdMatch = Regex.Match(acfContent, @"""appid""\s+""([^""]+)""");

                                if (nameMatch.Success && dirMatch.Success && appIdMatch.Success)
                                {
                                    string gameName = nameMatch.Groups[1].Value;
                                    string appId = appIdMatch.Groups[1].Value;
                                    string installDirName = dirMatch.Groups[1].Value;
                                    
                                    if (steamGames.Any(g => g.SteamAppId == appId)) continue;
                                    if (gameName.Contains("Redistributables") || gameName.Contains("Steamworks")) continue;

                                    string gamePath = Path.Combine(appsPath, "common", installDirName);
                                    string mainExe = FindSteamExe(gamePath, installDirName);
                                    
                                    if (Directory.Exists(gamePath))
                                        steamGames.Add(new GameInfo { Name = gameName, ExePath = mainExe ?? $"steam://rungameid/{appId}", SteamAppId = appId, InstallDirectory = gamePath });
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return steamGames;
        }

        internal static string FindSteamExe(string gameFolder, string installDirName)
        {
            if (!Directory.Exists(gameFolder)) return null;
            try
            {
                var exes = EnumerateFilesSafe(gameFolder, "*.exe", 12);
                var validExes = exes.Where(e => 
                {
                    string filename = Path.GetFileNameWithoutExtension(e).ToLower();
                    return !filename.Contains("unins") && !filename.Contains("setup") && 
                           !filename.Contains("vcredist") && !filename.Contains("crashhandler") &&
                           !filename.Contains("dxsetup");
                }).ToList();

                if (validExes.Count == 0) return null;
                
                string cleanDirName = installDirName.Replace(" ", "").ToLower();
                var exactMatch = validExes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Replace(" ", "").ToLower().Contains(cleanDirName));
                if (exactMatch != null) return exactMatch;

                var rootExe = validExes.FirstOrDefault(e => Path.GetDirectoryName(e).Equals(gameFolder, StringComparison.OrdinalIgnoreCase));
                if (rootExe != null) return rootExe;

                return validExes.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            }
            catch { return null; }
        }

        internal static List<GameInfo> FindPirateGamesDeep()
        {
            var pirateGames = new List<GameInfo>();
            
            var priorityPaths = new List<string>();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                priorityPaths.Add(Path.Combine(drive.Name, "Games"));
                priorityPaths.Add(Path.Combine(drive.Name, "Игры"));
                priorityPaths.Add(drive.Name);
            }

            var ignoreFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "windows", "users", "programdata", "appdata", "$recycle.bin", 
                "system volume information", "perflogs", "msocache", 
                "steamapps",
                "nvidia", "amd", "intel", "microsoft", "windowsapps",
                "drivers", "common files",
                "redist", "support", "Riot Client", "VALORANT", "shareplay",
                
                "dism++", "qbittorrent", "hoyoplay", "ds4windows", "virtualbox",
                "$windows.~tmp", "$windows.~bt", "$windows.~ws"
            };

            foreach (var path in priorityPaths.Distinct())
            {
                if (Directory.Exists(path))
                {
                    SearchDirectoryRecursive(path, 0, 7, pirateGames, ignoreFolders);
                }
            }

            return pirateGames;
        }

        internal static void SearchDirectoryRecursive(string currentPath, int currentDepth, int maxDepth, List<GameInfo> results, HashSet<string> ignoreFolders)
        {
            if (currentDepth > maxDepth || !Directory.Exists(currentPath)) return;

            try
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0) return;
                string dirName = new DirectoryInfo(currentPath).Name;
                
                if (dirName.StartsWith("$")) return;
                if (currentDepth > 0 && ignoreFolders.Contains(dirName)) return;

                bool container = new[] { "Games", "Игры", "Program Files", "Program Files (x86)", "Epic Games", "Riot Games", "SteamLibrary", "Ubisoft", "Ubisoft Game Launcher", "Origin Games", "EA Games", "Battle.net" }.Contains(dirName, StringComparer.OrdinalIgnoreCase);
                
                bool gameLibrary = IsInsideGameLibrary(currentPath);
                bool unrealGame = Directory.Exists(Path.Combine(currentPath, "Engine")) &&
                    Directory.Exists(Path.Combine(currentPath, "Content"));
                if (!container && currentDepth > 0 && unrealGame)
                {
                    string potentialExe = FindPirateMainExe(currentPath, true);
                    if (potentialExe != null)
                    {
                        if (!results.Any(g => g.ExePath == potentialExe))
                        {
                            results.Add(new GameInfo { Name = GetLocalGameName(potentialExe), ExePath = potentialExe });
                        }
                        return; 
                    }
                }

                if (currentDepth > 0 && !container)
                {
                    string rootExe = FindPirateMainExe(currentPath, false);
                    if (rootExe != null && (gameLibrary || HasGameFiles(currentPath, rootExe)))
                    {
                        if (!results.Any(g => g.ExePath == rootExe))
                        {
                            results.Add(new GameInfo { Name = GetLocalGameName(rootExe), ExePath = rootExe });
                        }
                        return;
                    }
                }

                var subDirs = Directory.GetDirectories(currentPath);
                foreach (var dir in subDirs)
                {
                    string subDirName = new DirectoryInfo(dir).Name;
                    if (ignoreFolders.Contains(subDirName)) continue;
                    if (subDirName.StartsWith(".")) continue;
                    if (new[] { "Engine", "Binaries", "node_modules", "obj", "bin" }
                        .Contains(subDirName, StringComparer.OrdinalIgnoreCase)) continue;

                    SearchDirectoryRecursive(dir, currentDepth + 1, maxDepth, results, ignoreFolders);
                }
            }
            catch { }
        }

        internal static bool IsInsideGameLibrary(string path)
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Games", "Игры", "GOG Games", "Epic Games", "Origin Games", "EA Games",
                "Riot Games", "Ubisoft Game Launcher", "Battle.net"
            };
            for (DirectoryInfo? folder = Directory.GetParent(path); folder != null; folder = folder.Parent)
                if (libraries.Contains(folder.Name)) return true;
            return false;
        }

        internal static bool HasGameFiles(string folder, string executable)
        {
            string name = Path.GetFileNameWithoutExtension(executable);
            // Use engine-specific evidence rather than generic Content/Binaries folders or exe size.
            bool unity = File.Exists(Path.Combine(folder, "UnityPlayer.dll")) &&
                Directory.Exists(Path.Combine(folder, name + "_Data"));
            bool godot = File.Exists(Path.ChangeExtension(executable, ".pck"));
            bool rpgMaker = File.Exists(Path.Combine(folder, "Game.ini")) &&
                (Directory.Exists(Path.Combine(folder, "Data")) ||
                 File.Exists(Path.Combine(folder, "Game.rgss3a")) ||
                 File.Exists(Path.Combine(folder, "Game.rgss2a")));
            bool unreal = EnumerateFilesSafe(folder, "*.pak", 4)
                .Any(file => file.Contains(Path.DirectorySeparatorChar + "Content" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
            return unity || godot || rpgMaker || unreal;
        }

        internal static string FindPirateMainExe(string folderPath, bool deepSearch)
        {
            if (!Directory.Exists(folderPath)) return null;
            
            try
            {
                var exes = EnumerateFilesSafe(folderPath, "*.exe", deepSearch ? 8 : 0);
                
                var badWords = new[] { 
                    "unins", "setup", "install", "vcredist", "dxsetup", "crash", 
                    "updater", "report", "helper", "config",
                    "ds4", "virtualbox", "vbox", "driver", "service", "portable",
                    "uninstall", "readme",
                    "ubisoft", "uplay", "origin", "ea desktop", "battle.net",
                    "dism++", "qbittorrent", "launcher", "update", "editor", "ue4editor", "unrealeditor"
                };

                var validExes = exes.Where(e => 
                {
                    if (IsServiceExecutable(e)) return false;
                    string fn = Path.GetFileNameWithoutExtension(e).ToLower();
                    
                    if (badWords.Any(b => fn.Contains(b))) 
                        return false;
                    if (!IsInsideGameLibrary(folderPath) && !HasGameFiles(folderPath, e)) return false;
                        
                    return true;
                }).ToList();

                if (validExes.Count == 0) return null;
                
                return validExes.OrderByDescending(f => new FileInfo(f).Length).First();
            }
            catch { return null; }
        }

        internal static bool IsServiceExecutable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return Path.GetFileName(path).Equals("SharePlayClient.exe", StringComparison.OrdinalIgnoreCase) ||
                (path.Replace('\\', '/').Contains("/Ubisoft Game Launcher/shareplay/", StringComparison.OrdinalIgnoreCase));
        }

}
