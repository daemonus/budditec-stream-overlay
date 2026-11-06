using System.Globalization;

namespace Budditec.Overlay.Core.Diagnostics;

/// <summary>
/// Developer-only switches read from environment variables, for load/memory tests:
/// BUDDITEC_OVERLAY_SETTINGS (settings file path), BUDDITEC_OVERLAY_SIMULATE_CHAT (messages per second,
/// replaces real chat), BUDDITEC_OVERLAY_STATS (file to append stats lines to every 5 s).
/// </summary>
public static class DevOptions
{
    public static string? SettingsPath => Environment.GetEnvironmentVariable("BUDDITEC_OVERLAY_SETTINGS");

    public static double? SimulatedChatRate =>
        double.TryParse(Environment.GetEnvironmentVariable("BUDDITEC_OVERLAY_SIMULATE_CHAT"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate > 0
            ? rate
            : null;

    public static string? StatsPath => Environment.GetEnvironmentVariable("BUDDITEC_OVERLAY_STATS");

    /// <summary>Appends "key=value" pairs plus process and GC memory to the stats file every 5 s.</summary>
    public static async Task WriteStatsAsync(string path, Func<IEnumerable<(string Key, object Value)>> extra, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var process = System.Diagnostics.Process.GetCurrentProcess();
        while (await timer.WaitForNextTickAsync(ct))
        {
            process.Refresh();
            var gc = GC.GetGCMemoryInfo();
            var values = new List<(string, object)>
            {
                ("time", DateTime.Now.ToString("HH:mm:ss")),
                ("privateMB", process.PrivateMemorySize64 / 1048576),
                ("workingSetMB", process.WorkingSet64 / 1048576),
                ("managedHeapMB", GC.GetTotalMemory(false) / 1048576),
                ("gcCommittedMB", gc.TotalCommittedBytes / 1048576),
            };
            values.AddRange(extra());
            await File.AppendAllTextAsync(path, string.Join(" ", values.Select(v => $"{v.Item1}={v.Item2}")) + Environment.NewLine, ct);
        }
    }
}
