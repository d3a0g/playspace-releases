using System.Diagnostics;

namespace GameLauncherGUI;

internal enum DownloadSpeedUnit { Megabits, Megabytes }
internal static class DownloadSpeed
{
    internal static string Label(DownloadSpeedUnit unit) => unit == DownloadSpeedUnit.Megabits ? "Мбит/с" : "Мбайт/с";
    internal static double Value(double bytes, DownloadSpeedUnit unit) => Math.Max(0,bytes) * (unit == DownloadSpeedUnit.Megabits ? 8 : 1) / 1_000_000;
    internal static long Bytes(decimal value, DownloadSpeedUnit unit) => (long)Math.Clamp(value * 1_000_000 / (unit == DownloadSpeedUnit.Megabits ? 8 : 1), 0, 10_000_000_000);
    internal static string Format(double bytes, DownloadSpeedUnit unit) => $"{Value(bytes,unit):N2} {Label(unit)}";
}

// One TransferCenter runs one network job at a time, so this bounds its aggregate payload throughput.
// Recheck the live setting in short slices; cancellation and disabling a limit do not wait for a whole file.
internal sealed class DownloadThrottle(Func<long> limit)
{
    internal async Task WaitAsync(int bytes, CancellationToken token)
    {
        double remaining = bytes;
        while(remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            long rate = limit(); if(rate <= 0) return;
            long start = Stopwatch.GetTimestamp();
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(remaining / rate * 1000, 1, 100)),token).ConfigureAwait(false);
            remaining -= Stopwatch.GetElapsedTime(start).TotalSeconds * rate;
        }
    }
}
