using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>Hardening: content-bound checks (TOCTOU), the PowerShell 7 installer, update URLs, the process runner.</summary>
public class HardeningTests
{
    static AppPaths Paths(out string root)
    {
        root = TestUtil.TempDir();
        var p = new AppPaths(root, Path.Combine(root, "cache"));
        p.EnsureFolders();
        return p;
    }

    // ---------- content hash instead of file time ----------

    [Fact]
    public void Scan_hash_follows_the_content_even_when_the_file_time_is_restored()
    {
        var paths = Paths(out _);
        string file = Path.Combine(paths.Scripts, "a.ps1");
        File.WriteAllText(file, "Write-Host 'aaaa'");
        var t = File.GetLastWriteTimeUtc(file);
        string h1 = ScriptCatalog.Scan(paths).Single().Hash;
        Assert.Equal(64, h1.Length);
        File.WriteAllText(file, "Write-Host 'bbbb'");   // same length
        File.SetLastWriteTimeUtc(file, t);               // touch -r: the old time is back
        var again = ScriptCatalog.Scan(paths).Single();
        Assert.Equal(t.Ticks.ToString(), again.Ticks);
        Assert.NotEqual(h1, again.Hash);
        Assert.Equal(Safety.FileSha256(file), again.Hash);
    }

    [Fact]
    public void Trust_and_the_safety_question_go_by_the_hash_that_was_checked()
    {
        string path = Path.Combine(TestUtil.TempDir(), "x.ps1");
        File.WriteAllText(path, "Remove-Item . -Recurse");
        string oldHash = Safety.FileSha256(path);
        var warn = new List<DangerFinding> { new() { Rule = "Del", Sev = "medium", Line = 1 } };
        var s = new Settings();
        Safety.AddTrust(s, "x.ps1", Safety.TrustKeyOf("x.ps1", oldHash));
        Assert.Equal(Safety.TrustKey("x.ps1", path), Safety.TrustKeyOf("x.ps1", oldHash));
        File.WriteAllText(path, "Remove-Item C:\\ -Recurse");   // swapped after the check
        Assert.False(Safety.Needed(s, warn, "inline", "x.ps1", path, oldHash));                       // the findings belong to the old content: trusted as before
        Assert.True(Safety.Needed(s, warn, "inline", "x.ps1", path, Safety.FileSha256(path)));        // the new content is not trusted
        Assert.True(Safety.Needed(s, warn, "inline", "x.ps1", path));                                  // without a hash: the file is read, so also not trusted
        Assert.Equal("", Safety.TrustKeyOf("x.ps1", ""));
    }

    [Fact]
    public void An_administrator_window_checks_the_hash_before_the_script_runs()
    {
        string hash = new string('A', 64);
        const string done = "C:\\c\\1.exit";
        string a = RunArgs.WindowArgs("C:\\s\\a.ps1", " -X 'y'", hash, done);
        Assert.Contains("Get-FileHash -LiteralPath 'C:\\s\\a.ps1' -Algorithm SHA256", a);
        Assert.Contains("-ne '" + hash + "'", a);
        Assert.Contains("& 'C:\\s\\a.ps1' -X 'y'", a);
        Assert.DoesNotContain("Get-FileHash", RunArgs.WindowArgs("C:\\s\\a.ps1", " -X 'y'", null, done));
        Assert.DoesNotContain("Get-FileHash", RunArgs.WindowArgs("C:\\s\\a.ps1", "", "not a hash", done));   // only a real SHA-256 goes into the command
        Assert.StartsWith("-NoProfile -NoExit -ExecutionPolicy Bypass -Command ", RunArgs.WindowArgs("C:\\s\\a.ps1", "", hash, done));
    }

    [PwshFact]
    public void The_administrator_command_runs_the_script_only_when_the_hash_matches()
    {
        string pwsh = TestUtil.RequirePwsh();
        string dir = TestUtil.TempDir();
        string script = Path.Combine(dir, "mark.ps1");
        string marker = Path.Combine(dir, "ran.txt");
        File.WriteAllText(script, "Set-Content -LiteralPath '" + marker + "' -Value ran");
        string good = Safety.FileSha256(script);
        // WindowArgs is "-NoProfile -NoExit ... -Command <cmd>": run the same command without the window
        string Run(string hash)
        {
            string args = RunArgs.WindowArgs(script, "", hash, Path.Combine(dir, "done.exit")).Replace("-NoExit ", "");
            using var p = Process.Start(new ProcessStartInfo(pwsh, args) { RedirectStandardOutput = true, UseShellExecute = false })!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return o;
        }
        Run(new string('0', 64));
        Assert.False(File.Exists(marker));   // a different hash: not run
        Run(good);
        Assert.True(File.Exists(marker));
    }

    // ---------- PowerShell 7 installer ----------

    [Theory]
    [InlineData("https://github.com/PowerShell/PowerShell/releases/download/v7.5.0/PowerShell-7.5.0-win-x64.msi", true)]
    [InlineData("http://github.com/PowerShell/PowerShell/releases/download/v7.5.0/PowerShell-7.5.0-win-x64.msi", false)]
    [InlineData("https://github.com.evil.example/PowerShell/PowerShell/releases/download/v7/a.msi", false)]
    [InlineData("https://evil.example/https://github.com/PowerShell/PowerShell/releases/download/v7/a.msi", false)]
    [InlineData("https://github.com/Other/PowerShell/releases/download/v7/a.msi", false)]
    [InlineData("https://github.com/PowerShell/PowerShell/releases/download/v7/a.exe", false)]
    [InlineData("https://github.com/PowerShell/PowerShell/releases/download/v7/a.msi?x=1", false)]
    [InlineData("https://user@github.com/PowerShell/PowerShell/releases/download/v7/a.msi", false)]
    public void Installer_address_must_be_a_PowerShell_release_download(string url, bool ok) =>
        Assert.Equal(ok, Regex.IsMatch(url, EngineLocator.MsiUrlPattern));

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
    [InlineData("CN=Microsoft Corporation", true)]
    [InlineData("CN=Evil Microsoft Corporation, O=x", false)]
    [InlineData("CN=Microsoft Corporation Fake, O=x", false)]
    [InlineData("O=Microsoft Corporation", false)]
    [InlineData("", false)]
    public void Installer_signer_must_be_Microsoft_Corporation(string subject, bool ok) =>
        Assert.Equal(ok, Regex.IsMatch(subject, EngineLocator.MicrosoftSubjectPattern));

    [Fact]
    public void Msi_install_script_checks_address_hash_and_signature_before_msiexec()
    {
        string s = EngineLocator.InstallScript("msi", null);
        int sig = s.IndexOf("Get-AuthenticodeSignature", StringComparison.Ordinal);
        int hash = s.IndexOf("Get-FileHash", StringComparison.Ordinal);
        int msi = s.IndexOf("Start-Process msiexec.exe", StringComparison.Ordinal);
        Assert.True(hash > 0 && sig > hash && msi > sig, "address -> SHA-256 -> signature -> msiexec");
        Assert.Contains("[IO.File]::Open($file, 'Open', 'Read', 'Read')", s);   // the file is held while it is checked and installed
        Assert.DoesNotContain("$env:TEMP", s);
        Assert.Contains("[guid]::NewGuid()", s);
        Assert.Contains(EngineLocator.MsiUrlPattern, s);
        Assert.Contains(EngineLocator.MicrosoftSubjectPattern, s);
        foreach (var lang in new[] { "en", "ru" })
        {
            Loc.Language = lang;
            foreach (var k in new[] { "p7BadUrl", "p7BadHash", "p7BadSig", "updBadUrl", "runChanged", "runChangedFail", "runChangedAdmin" })
                Assert.NotEqual(k, Loc.T(k));
        }
        Loc.Language = "en";
    }

    [PwshFact]
    public void Msi_install_script_is_valid_powershell()
    {
        string pwsh = TestUtil.RequirePwsh();
        string dir = TestUtil.TempDir();
        string file = Path.Combine(dir, "install.ps1");
        File.WriteAllText(file, EngineLocator.InstallScript("msi", null), new System.Text.UTF8Encoding(true));
        string cmd = "$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile('" + file + "',[ref]$null,[ref]$e); exit $e.Count";
        using var p = Process.Start(new ProcessStartInfo(pwsh, "-NoProfile -NonInteractive -Command \"" + cmd + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    // ---------- update addresses ----------

    [Theory]
    [InlineData("https://github.com/someone/shelly/releases/download/v1/shelly-1.zip", true)]
    [InlineData("https://github.com/Someone/Shelly/releases/latest", true)]
    [InlineData("https://github.com/other/repo/releases/download/v1/shelly-1.zip", false)]
    [InlineData("https://github.com/someone/shelly-evil/releases", false)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", true)]
    [InlineData("https://aka.ms/powershell-release?tag=stable", true)]
    [InlineData("https://learn.microsoft.com/powershell", true)]
    [InlineData("https://microsoft.com.evil.example/x", false)]
    [InlineData("http://github.com/someone/shelly/releases", false)]
    [InlineData("https://github.com:8443/someone/shelly/releases", false)]
    [InlineData("https://user:pw@github.com/someone/shelly/releases", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("C:\\Windows\\System32\\calc.exe", false)]
    [InlineData("ms-msdt:/id", false)]
    [InlineData("", false)]
    public void Only_official_https_addresses_are_opened(string url, bool ok) =>
        Assert.Equal(ok, UpdateChecker.SafeUrl(url, "someone/shelly") != null);

    [Fact]
    public void A_huge_version_number_in_a_tag_is_not_a_crash_and_the_first_asset_wins()
    {
        Assert.Null(UpdateChecker.ParseVersion("v99999999999.0"));
        Assert.Null(UpdateChecker.ParseVersion("1.99999999999"));
        Assert.Null(UpdateChecker.ParseVersion("1.0.99999999999"));
        Assert.Equal(new Version(1, 2, 3), UpdateChecker.ParseVersion("v1.2.3"));
        Assert.False(UpdateChecker.IsNewer("v99999999999.0", "1.0.0"));
        Assert.Null(UpdateChecker.Parse("{\"tag_name\":\"v99999999999.0\"}"));
        var r = UpdateChecker.Parse("{\"tag_name\":\"v1.2.0\",\"assets\":[{\"name\":\"shelly-1.2.0.zip\",\"browser_download_url\":\"https://first\"},{\"name\":\"shelly-1.2.1.zip\",\"browser_download_url\":\"https://second\"}]}")!;
        Assert.Equal("https://first", r.AssetUrl);
    }

    // ---------- the analysis belongs to the content ----------

    [PwshFact]
    public void A_swapped_file_with_the_old_file_time_is_analyzed_again()
    {
        string pwsh = TestUtil.RequirePwsh();
        var paths = Paths(out _);
        string file = Path.Combine(paths.Scripts, "a.ps1");
        string bad = "Remove-Item C:\\ -Recurse";
        File.WriteAllText(file, "Get-Date".PadRight(bad.Length));
        var t = File.GetLastWriteTimeUtc(file);
        using var svc = new AnalysisService(new AnalysisService.Engines { Pwsh = pwsh }, Path.Combine(paths.CacheDir, "a.json"), paths.CacheDir);
        var f1 = ScriptCatalog.Scan(paths).Single();
        Assert.True(svc.WaitAsync(f1, TimeSpan.FromSeconds(120)).Result);
        Assert.Equal("", svc.Meta(f1, new Settings(), true, false).DangerLevel);
        File.WriteAllText(file, bad);
        File.SetLastWriteTimeUtc(file, t);
        var f2 = ScriptCatalog.Scan(paths).Single();
        Assert.Equal(f1.Ticks, f2.Ticks);
        Assert.Equal(f1.Size, f2.Size);
        Assert.True(svc.IsPending(f2));   // by file time and size the old verdict would still stand
        Assert.True(svc.WaitAsync(f2, TimeSpan.FromSeconds(120)).Result);
        Assert.Equal("high", svc.Meta(f2, new Settings(), true, false).DangerLevel);
    }

    [PwshFact]
    public void A_file_that_no_longer_matches_its_hash_is_not_analyzed_under_that_hash()
    {
        string pwsh = TestUtil.RequirePwsh();
        var paths = Paths(out _);
        string file = Path.Combine(paths.Scripts, "a.ps1");
        File.WriteAllText(file, "Get-Date");
        using var svc = new AnalysisService(new AnalysisService.Engines { Pwsh = pwsh }, Path.Combine(paths.CacheDir, "a.json"), paths.CacheDir);
        var stale = ScriptCatalog.Scan(paths).Single();
        File.WriteAllText(file, "Remove-Item C:\\ -Recurse");   // changed after the scan, before the analyzer read it
        Assert.True(svc.WaitAsync(stale, TimeSpan.FromSeconds(120)).Result);
        var rec = svc.Get(stale, "7");
        Assert.NotNull(rec);
        Assert.True(rec!.Failed);   // no verdict is stored for content nobody hashed
        Assert.True(svc.Meta(stale, new Settings(), true, false).FallbackOnly);
        var fresh = ScriptCatalog.Scan(paths).Single();
        Assert.True(svc.WaitAsync(fresh, TimeSpan.FromSeconds(120)).Result);
        Assert.Equal("high", svc.Meta(fresh, new Settings(), true, false).DangerLevel);
        Assert.Empty(Directory.GetFiles(paths.CacheDir, "snap-*"));   // the private copies are removed
    }

    [Fact]
    public void An_analysis_cache_of_the_older_format_is_dropped()
    {
        string dir = TestUtil.TempDir();
        string file = Path.Combine(dir, "c.json");
        var c = new AnalysisCache(file);
        c.Put(new AnalysisRecord { Path = "x.ps1", Ticks = "123", Engine = "7" });
        c.Save();
        string text = File.ReadAllText(file);
        Assert.StartsWith("v" + AnalysisCache.FormatVersion + "|", text);
        File.WriteAllText(file, "v1" + text.Substring(text.IndexOf('|')));   // the file-time format
        var c2 = new AnalysisCache(file);
        c2.Load();
        Assert.Null(c2.Get("x.ps1", "123", "7"));
    }

    // ---------- Runner ----------

    static InlineRunner RunPwsh(string code, out List<string> lines)
    {
        string pwsh = TestUtil.RequirePwsh();
        var r = new InlineRunner { StreamGrace = TimeSpan.FromHours(1) };   // the run must end by itself, not by the grace time
        using var done = new ManualResetEventSlim();
        r.Finished += _ => done.Set();
        r.Start(pwsh, "-NoProfile -NonInteractive -Command " + Quoting.NativeArg(code), Path.GetTempPath());
        Assert.True(done.Wait(TimeSpan.FromSeconds(90)), "the run did not end");
        lines = new List<string>();
        while (r.Lines.TryDequeue(out var l)) lines.Add(l);
        return r;
    }

    [PwshFact]
    public void Runner_filters_clixml_noise_on_stderr_and_marks_real_errors()
    {
        using var r = RunPwsh("[Console]::Error.WriteLine('#< CLIXML'); [Console]::Error.WriteLine(\"<Objs Version='1.1.0.1'>\"); [Console]::Error.WriteLine('real error'); 'out line'", out var lines);
        Assert.Contains("out line", lines);
        Assert.Contains("! real error", lines);
        Assert.DoesNotContain(lines, l => l.Contains("CLIXML") || l.Contains("<Objs"));
        Assert.Equal("real error", r.ErrorText());
        Assert.Equal(0, r.ExitCode);
    }

    [PwshFact]
    public void Runner_ends_without_waiting_and_releases_the_process()
    {
        using var r = RunPwsh("'x'; exit 3", out var lines);
        Assert.Equal(3, r.ExitCode);
        Assert.False(r.Running);
        Assert.Null(r.Proc);   // disposed
        Assert.Contains("x", lines);
    }

    [PwshFact]
    public void Runner_can_be_stopped_and_a_second_run_is_independent()
    {
        string pwsh = TestUtil.RequirePwsh();
        using var r = new InlineRunner();
        using var done = new ManualResetEventSlim();
        r.Finished += _ => done.Set();
        r.Start(pwsh, "-NoProfile -NonInteractive -Command \"Start-Sleep 60\"", Path.GetTempPath());
        Thread.Sleep(500);
        r.Stop();
        Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(r.StoppedByUser);
        using var r2 = RunPwsh("'again'", out var lines);
        Assert.Contains("again", lines);
        Assert.Equal(0, r2.ExitCode);
    }
}
