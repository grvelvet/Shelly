using System.Diagnostics;
using System.Text;

namespace Shelly.Services;

/// <summary>Hidden startup timing log. Off by default; on with <c>--perf</c>, the environment variable
/// <c>SHELLY_PERF=1</c>, or <c>"perfLog": true</c> in launcher-settings.json. Marks are always collected (a Stopwatch read
/// each, a dozen per start) and written to perf.log next to shelly.log only when the log is on.</summary>
public static class Perf
{
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static readonly List<(string Phase, double Ms)> marks = new();
    static double last;
    public static bool On { get; set; } = Environment.GetEnvironmentVariable("SHELLY_PERF") == "1";
    public static string? FilePath;

    public static void Mark(string phase)
    {
        double now = clock.Elapsed.TotalMilliseconds;
        marks.Add((phase, now));
    }

    /// <summary>Time from process start to the first line of Main/OnStartup (runtime + JIT start-up).</summary>
    public static double ProcessToStartupMs()
    {
        try { return (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds - clock.Elapsed.TotalMilliseconds; }
        catch { return -1; }
    }

    static double runtimeMs = -1;
    public static void Begin() { runtimeMs = ProcessToStartupMs(); }

    /// <summary>Writes the collected marks plus memory figures (called once the app is idle after start).</summary>
    public static void Flush(string extra)
    {
        if (!On || FilePath == null) return;
        try
        {
            var p = Process.GetCurrentProcess();
            p.Refresh();
            var sb = new StringBuilder();
            sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Shelly {AppState.Version}  .NET {Environment.Version}  {Environment.OSVersion.VersionString}");
            sb.AppendLine($"  process start -> OnStartup      {runtimeMs,8:0} ms");
            last = 0;
            foreach (var (ph, ms) in marks)
            {
                sb.AppendLine($"  {ph,-32}{ms - last,8:0.0} ms   (t={ms:0})");
                last = ms;
            }
            sb.AppendLine($"  working set {p.WorkingSet64 / 1048576.0:0.0} MB, private {p.PrivateMemorySize64 / 1048576.0:0.0} MB, GC heap {GC.GetTotalMemory(false) / 1048576.0:0.0} MB, gen0/1/2 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
            if (extra.Length > 0) sb.AppendLine("  " + extra);
            File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}
