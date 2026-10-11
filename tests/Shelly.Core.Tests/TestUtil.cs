using Shelly.Core;

// Loc.Language is global state that several tests switch; run the test classes one after another
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Shelly.Core.Tests;

static class TestUtil
{
    public static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "shelly-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>pwsh for the analyzer integration tests: SHELLY_TEST_PWSH, ~/.pwsh/pwsh, or pwsh on PATH. Null = those tests pass without running.</summary>
    public static string? Pwsh()
    {
        string? env = Environment.GetEnvironmentVariable("SHELLY_TEST_PWSH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pwsh", OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (File.Exists(home)) return home;
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (d.Length == 0) continue;
            string p = Path.Combine(d, OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
            if (File.Exists(p)) return p;
        }
        if (OperatingSystem.IsWindows())
        {
            string p = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static List<AnalysisRecord> Analyze(params (string Name, string Code)[] scripts)
    {
        string? pwsh = Pwsh();
        if (pwsh == null) return new();
        string dir = TempDir();
        var paths = new List<string>();
        foreach (var (n, c) in scripts)
        {
            string p = Path.Combine(dir, n);
            File.WriteAllText(p, c, new System.Text.UTF8Encoding(true));
            paths.Add(p);
        }
        return AnalyzerRunner.RunAsync(pwsh, paths, dir, TimeSpan.FromSeconds(120)).GetAwaiter().GetResult();
    }
}
