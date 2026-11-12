using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GameLauncherGUI;

public sealed class PerformanceProfile
{
    public Dictionary<string,int> DetailLevels {get;set;}=new();
    public Dictionary<string,string> NativeOptions {get;set;}=new();
    public int TargetFps { get; set; } = 60;
    public int MeasuredFps { get; set; }
    public int RenderScale { get; set; } = 100;
    public string Quality { get; set; } = "Средние";
    public bool ApplyOnLaunch { get; set; }
    public int ResolutionWidth { get; set; }
    public int ResolutionHeight { get; set; }
    public string WindowMode { get; set; } = "Безрамочный";
    public bool VSync { get; set; }
    public bool UnlimitedFps {get;set;}
    public string? ConfigFile { get; set; }

    public PerformanceProfile Copy() => new() {
        TargetFps = Math.Clamp(TargetFps, 30, 360), MeasuredFps = Math.Clamp(MeasuredFps, 0, 1000),
        RenderScale = Math.Clamp(RenderScale, 50, 100),
        Quality = new[] { "Низкие", "Средние", "Высокие" }.Contains(Quality) ? Quality : "Средние",
        ApplyOnLaunch = ApplyOnLaunch, ResolutionWidth = ResolutionWidth is >= 640 and <= 16384 ? ResolutionWidth : 0,
        ResolutionHeight = ResolutionHeight is >= 480 and <= 16384 ? ResolutionHeight : 0,
        WindowMode = new[] { "Полноэкранный", "Безрамочный", "Оконный" }.Contains(WindowMode) ? WindowMode : "Безрамочный",
        VSync = VSync, ConfigFile = ConfigFile,UnlimitedFps=UnlimitedFps,
        DetailLevels=GraphicsParameters.Sanitize(DetailLevels),NativeOptions=NativeOptions?.Where(p=>p.Key.Length<=160&&p.Value!=null&&p.Value.Length<=100&&(p.Key.StartsWith("/graphics",StringComparison.Ordinal)||p.Key.StartsWith("/video/display|",StringComparison.Ordinal))).Take(100).ToDictionary(p=>p.Key,p=>p.Value)??new()
    };
}

internal sealed record PcSnapshot(string Cpu, string Gpu, ulong TotalRam, ulong AvailableRam,
    int Width, int Height, int RefreshRate, bool OnBattery)
{
    public static PcSnapshot Read()
    {
        string cpu = "Не удалось определить процессор";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? cpu;
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        var adapters = new HashSet<string>();
        for (uint index = 0; index < 32; index++)
        {
            var adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref adapter, 0)) break;
            if ((adapter.Flags & 8) == 0 && !string.IsNullOrWhiteSpace(adapter.Description)) adapters.Add(adapter.Description);
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        var screen = Screen.PrimaryScreen;
        var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
        bool displayKnown = EnumDisplaySettings(screen?.DeviceName, -1, ref mode);
        return new PcSnapshot(cpu, adapters.Count > 0 ? string.Join(" / ", adapters) : "Не удалось определить видеокарту",
            memory.TotalPhysical, memory.AvailablePhysical, screen?.Bounds.Width ?? 0, screen?.Bounds.Height ?? 0,
            displayKnown && mode.Frequency > 1 ? (int)mode.Frequency : 0,
            SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Explicit, Size = 220, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(184)] public uint Frequency;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DisplayMode info);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus info);
}

internal static class PerformanceAdvisor
{
    public static int SuggestTarget(PcSnapshot pc, int measuredFps)
    {
        // A measured baseline can guide a sustainable goal; hardware names alone cannot predict FPS.
        int ceiling = measuredFps > 0 ? measuredFps : 60;
        if (pc.RefreshRate > 0) ceiling = Math.Min(ceiling, pc.RefreshRate);
        return new[] { 30, 40, 45, 50, 60, 75, 90, 100, 120, 144, 165, 180, 200, 240, 300, 360 }.LastOrDefault(fps => fps <= ceiling, 30);
    }

    public static string Plan(PcSnapshot pc, PerformanceProfile raw)
    {
        var profile = raw.Copy();
        var lines = new List<string> {
            $"Цель: {profile.TargetFps} FPS · бюджет кадра {1000.0 / profile.TargetFps:0.0} мс.",
            $"План: качество «{profile.Quality}», масштаб рендера {profile.RenderScale}%."
        };
        if (pc.Width > 0 && pc.Height > 0)
            lines.Add($"При базовом разрешении {pc.Width} × {pc.Height} рендер будет примерно {pc.Width * profile.RenderScale / 100} × {pc.Height * profile.RenderScale / 100}.");
        if (profile.MeasuredFps == 0)
            lines.Add("Сначала замерь средний FPS в самой игре при текущих настройках. Введи замер выше — он поможет выбрать стартовую цель.");
        else if (profile.MeasuredFps < profile.TargetFps)
        {
            lines.Add($"Текущий замер ниже цели на {profile.TargetFps - profile.MeasuredFps} FPS. Достижимость цели пока не подтверждена.");
            lines.Add("Сравни одну и ту же сцену с меньшим масштабом рендера. Если FPS вырос — попробуй снизить тени, отражения и масштаб; если почти не изменился — проверь загрузку CPU и фоновые приложения.");
        }
        else lines.Add("Средний FPS в замере достигает цели. Проверь также просадки и время кадра: одно среднее значение не показывает плавность.");
        if (pc.RefreshRate > 0 && profile.TargetFps > pc.RefreshRate)
            lines.Add($"Экран работает на {pc.RefreshRate} Гц: он не покажет все {profile.TargetFps} кадров, хотя высокий FPS может уменьшать задержку ввода.");
        if (pc.TotalRam > 0 && pc.AvailableRam < 3UL * 1024 * 1024 * 1024)
            lines.Add("Свободно меньше 3 ГБ RAM на момент проверки. Перед игрой закрой ненужные приложения вручную и повтори замер.");
        if (pc.OnBattery) lines.Add("ПК работает от батареи. Для повторяемого теста подключи питание.");
        lines.Add("Меняй по одному параметру и повторяй замер в той же сцене. Настройки и доступные технологии зависят от игры.");
        lines.Add(profile.ApplyOnLaunch ? "Доступные параметры применяются перед запуском. Поддержка перечислена в карточке игры; целевой FPS не является прогнозом производительности." :
            "Автоприменение выключено. Включи «Применять перед запуском» или нажми «Применить и играть». Целевой FPS не является прогнозом.");
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }
}
