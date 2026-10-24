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

    /// <summary>pwsh for the analyzer integration tests: SHELLY_TEST_PWSH, ~/.pwsh/pwsh, or pwsh on PATH (on Windows also Windows PowerShell).
    /// Null = not found: <see cref="PwshFactAttribute"/> tests are skipped (shown as skipped, with the reason), or fail when
    /// <see cref="PwshRequired"/> (CI).</summary>
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

    /// <summary>CI (the CI variable that GitHub Actions sets) or SHELLY_REQUIRE_PWSH=1: a missing PowerShell is an error, not a skip.</summary>
    public static bool PwshRequired =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
        Environment.GetEnvironmentVariable("SHELLY_REQUIRE_PWSH") == "1";

    /// <summary>The PowerShell for a test; throws (the test fails) when there is none.</summary>
    public static string RequirePwsh() =>
        Pwsh() ?? throw new InvalidOperationException("PowerShell not found: install pwsh or set SHELLY_TEST_PWSH");

    public static List<AnalysisRecord> Analyze(params (string Name, string Code)[] scripts)
    {
        string pwsh = RequirePwsh();
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
