using System;
using System.Linq;
namespace GameLauncherGUI
{
    public class GameInfo
    {
        public string Name { get; set; }
        public string ExePath { get; set; }
        public string SteamAppId { get; set; }
        public string LaunchArguments { get; set; } = "";
        public bool? CheckUbisoftBeforeLaunch { get; set; }
        public bool IsPinned { get; set; }
        public double PlayedSeconds { get; set; }
        public long? SteamPlaytimeMinutes { get; set; }
        public string? SteamPlaytimeUser { get; set; }
        public DateTimeOffset? SteamPlaytimeUpdatedUtc { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public double DisplayPlayedSeconds => SteamPlaytimeMinutes.HasValue ? SteamPlaytimeMinutes.Value * 60.0 : PlayedSeconds;
        [Newtonsoft.Json.JsonIgnore]
        public string PlaytimeLabel => SteamPlaytimeMinutes.HasValue ? $"Steam · {SteamPlaytimeMinutes.Value / 60.0:N1} ч" :
            !string.IsNullOrEmpty(SteamAppId) && SteamAppId.All(char.IsAsciiDigit) ? "Steam · время не загружено" : $"Локально · {PlayedSeconds / 3600:0.0} ч";
        public string? CustomIconPath { get; set; }
        public string TrackingExecutable { get; set; } = "";
        public string? AchievementSummary { get; set; }
        public PerformanceProfile? Performance { get; set; }
        public DateTimeOffset? LastLaunchUtc { get; set; }
        public string? InstallDirectory { get; set; }
        public GameCareOptions? Care { get; set; }
        public string LibraryId { get; set; } = Guid.NewGuid().ToString("N");
        public override string ToString()=>Name;
    }

}
