using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.1.1: the Python summary line of Settings > Environment, and virtual environments next to scripts (on a fake
/// file system, so it runs on any OS; plus a real venv when python3 -m venv works here).</summary>
public class VenvAndSummaryTests
{
    // ---------- Summary line ----------

    static T InLang<T>(string lang, Func<T> f)
    {
        string old = Loc.Language;
        try { Loc.SetLanguage(lang); return f(); }
        finally { Loc.SetLanguage(old); }
    }

    static PythonInfo Found(string version, string source, string path = @"C:\Py\python.exe") => new() { Path = path, Version = version, Source = source };

    [Theory]
    [InlineData("en", "", "py", "Python 3.13 · auto-detect")]
    [InlineData("en", @"D:\py\python.exe", "setting", "Python 3.12 · custom path")]
    [InlineData("ru", "", "path", "Python 3.13 · автопоиск")]
    [InlineData("ru", @"D:\py\python.exe", "setting", "Python 3.12 · свой путь")]
    public void Summary_shows_the_short_version_and_how_it_was_found(string lang, string configured, string source, string expected)
    {
        var py = Found(source == "setting" ? "3.12.4" : "3.13.0rc1", source);
        var sum = InLang(lang, () => PythonSummary.For(configured, py, "", false));
        Assert.Equal(expected, sum.Text);
        Assert.Equal(@"C:\Py\python.exe", sum.Tip);   // the full path is in the tooltip
        Assert.Equal("ok", sum.Level);
        Assert.Equal(configured.Length > 0, sum.CanReset);
    }

    [Fact]
    public void Summary_for_a_missing_python_and_a_bad_custom_path()
    {
        var none = InLang("ru", () => PythonSummary.For("", null, "pyNotFound", false));
        Assert.Equal("Python не найден", none.Text);
        Assert.False(none.CanReset);   // already auto-detect: Reset is greyed out
        Assert.Equal("warn", none.Level);
        var bad = InLang("ru", () => PythonSummary.For(@"D:\old\python.exe", null, "pyBadPath", false));
        Assert.Equal("Свой путь недоступен", bad.Text);
        Assert.Equal(@"D:\old\python.exe", bad.Tip);
        Assert.True(bad.CanReset);
        Assert.Equal("Custom path unavailable", InLang("en", () => PythonSummary.For(@"D:\old\python.exe", null, "pyBadPath", false)).Text);
        Assert.Equal("Python not found", InLang("en", () => PythonSummary.For(null, null, "pyNotFound", false)).Text);
    }

    [Fact]
    public void Summary_while_searching_says_so_and_keeps_reset_for_a_custom_path()
    {
        var s = InLang("en", () => PythonSummary.For(@"D:\py\python.exe", null, "", true));
        Assert.Equal("Looking for Python…", s.Text);
        Assert.True(s.CanReset);
        Assert.False(InLang("en", () => PythonSummary.For("", Found("3.12.1", "py"), "", true)).CanReset);
    }

    [Theory]
    [InlineData("3.12.4", "3.12")]
    [InlineData("3.13.0rc1", "3.13")]
    [InlineData("3.9", "3.9")]
    [InlineData("", "")]
    [InlineData("weird", "weird")]
    public void Short_version(string v, string expected) => Assert.Equal(expected, PythonSummary.ShortVersion(v));

    [Fact]
    public void Summary_strings_are_short_and_have_no_long_dashes()
    {
        foreach (var lang in new[] { "en", "ru" })
            foreach (var k in new[] { "pySumCustom", "pySumAuto", "pySumNone", "pySumBad", "pyPick", "pyReset", "setShell", "setUseVenv", "setEngSystem", "setEngines" })
            {
                string t = InLang(lang, () => Loc.T(k));
                Assert.True(t.Length <= 40, k);
                Assert.DoesNotContain("\u2014", t);
            }
        Assert.Equal("System", InLang("en", () => Loc.T("setEngSystem")));   // 1.1.2: was "Auto" (engAuto)
        Assert.Equal("Системная", InLang("ru", () => Loc.T("setEngSystem")));
        Assert.Equal("Environment", InLang("en", () => Loc.T("setEngines")));
        Assert.Equal("Окружение", InLang("ru", () => Loc.T("setEngines")));
        Assert.False(Loc.Has("cWin11"));   // the "full Windows 11 styling" row is gone
        Assert.True(Loc.Has("cWin10"));
    }

    [Fact]
    public void No_message_points_to_the_old_section_name()
    {
        foreach (var lang in new[] { "en", "ru" })
            InLang(lang, () =>
            {
                foreach (var k in new[] { "pyMissingText", "pyBadPathText", "pyMarkMissing" })
                {
                    string t = Loc.T(k);
                    Assert.DoesNotContain("Engines", t);
                    Assert.DoesNotContain("Движки", t);
                    Assert.Contains(lang == "en" ? "Settings > Environment" : "Параметры > Окружение", t);
                }
                return 0;
            });
    }

    // ---------- Virtual environments on a fake file system ----------

    sealed class FakeFs : IVenvFileSystem
    {
        public readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Links = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Text = new(StringComparer.OrdinalIgnoreCase);
        public bool FileExists(string path) => Files.Contains(path);
        public bool IsLink(string dir) => Links.Contains(dir);
        public string? ReadText(string path) => Text.TryGetValue(path, out var t) ? t : null;

        /// <summary>A working venv: pyvenv.cfg and Scripts/python.exe.</summary>
        public string Venv(string dir, string version = "3.12.4", bool broken = false)
        {
            string cfg = Path.Combine(dir, "pyvenv.cfg");
            Files.Add(cfg);
            Text[cfg] = "home = C:\\Python312\ninclude-system-site-packages = false\nversion = " + version + "\n";
            string exe = Path.Combine(dir, "Scripts", "python.exe");
            if (!broken) Files.Add(exe);
            return exe;
        }
    }

    static readonly string Root = Path.Combine(Path.GetTempPath(), "fake-shelly", "Scripts");
    static string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    [Fact]
    public void Venv_in_the_script_folder_is_found()
    {
        var fs = new FakeFs();
        string exe = fs.Venv(P("Tools", ".venv"));
        var v = VenvLocator.Find(P("Tools", "report.py"), Root, fs)!;
        Assert.False(v.Broken);
        Assert.Equal(P("Tools", ".venv"), v.Root);
        Assert.Equal(exe, v.Python);
        Assert.Equal("3.12.4", v.Version);
        Assert.Equal(".venv", v.Name);
        Assert.Equal(Path.Combine(P("Tools", ".venv"), "Scripts"), v.BinDir);
    }

    [Fact]
    public void The_nearest_venv_wins_and_dot_venv_goes_before_venv_and_env()
    {
        var fs = new FakeFs();
        fs.Venv(P(".venv"), "3.11.0");
        fs.Venv(P("A", "env"), "3.12.0");
        fs.Venv(P("A", "venv"), "3.12.1");
        Assert.Equal(P("A", "venv"), VenvLocator.Find(P("A", "B", "C", "s.py"), Root, fs)!.Root);   // walks up from C to A
        Assert.Equal(P(".venv"), VenvLocator.Find(P("Other", "s.py"), Root, fs)!.Root);              // up to the root itself
        Assert.Equal(P(".venv"), VenvLocator.Find(P("s.py"), Root, fs)!.Root);
        fs.Venv(P("A", ".venv"), "3.13.0");
        Assert.Equal("3.13.0", VenvLocator.Find(P("A", "s.py"), Root, fs)!.Version);
    }

    [Fact]
    public void Search_never_goes_above_the_folder_root()
    {
        var fs = new FakeFs();
        string above = Path.GetDirectoryName(Root)!;
        fs.Venv(Path.Combine(above, ".venv"));   // next to Scripts, outside it
        Assert.Null(VenvLocator.Find(P("Tools", "s.py"), Root, fs));
        Assert.Null(VenvLocator.Find(P("s.py"), Root, fs));
        // A root with a trailing separator works the same
        Assert.Null(VenvLocator.Find(P("s.py"), Root + Path.DirectorySeparatorChar, fs));
        fs.Venv(P("venv"));
        Assert.NotNull(VenvLocator.Find(P("Tools", "s.py"), Root + Path.DirectorySeparatorChar, fs));
        // A sibling whose name starts like the root is not inside it
        var fs2 = new FakeFs();
        fs2.Venv(Root + "2" + Path.DirectorySeparatorChar + ".venv");
        Assert.Null(VenvLocator.Find(Path.Combine(Root + "2", "x", "s.py"), Root, fs2));
        // Without a root only the script's own folder is looked at
        Assert.Null(VenvLocator.Find(P("Tools", "s.py"), null, fs));
        Assert.NotNull(VenvLocator.Find(P("s.py"), null, fs));
    }

    [Fact]
    public void A_folder_without_pyvenv_cfg_or_a_link_is_not_a_venv()
    {
        var fs = new FakeFs();
        fs.Files.Add(P("env", "Scripts", "python.exe"));   // "env" folder of some other tool: no pyvenv.cfg
        Assert.Null(VenvLocator.Find(P("s.py"), Root, fs));
        fs.Venv(P("Tools", ".venv"));
        fs.Links.Add(P("Tools", ".venv"));                  // a junction to somewhere else: not followed
        Assert.Null(VenvLocator.Find(P("Tools", "s.py"), Root, fs));
    }

    [Fact]
    public void A_broken_venv_is_reported_not_skipped()
    {
        var fs = new FakeFs();
        fs.Venv(P("Tools", ".venv"), broken: true);
        fs.Venv(P(".venv"));   // a working one further up must NOT be used instead
        var v = VenvLocator.Find(P("Tools", "s.py"), Root, fs)!;
        Assert.True(v.Broken);
        Assert.Equal(P("Tools", ".venv"), v.Root);
    }

    [Fact]
    public void Posix_layout_bin_python_is_supported()
    {
        var fs = new FakeFs();
        string dir = P("proj", ".venv");
        fs.Files.Add(Path.Combine(dir, "pyvenv.cfg"));
        fs.Files.Add(Path.Combine(dir, "bin", "python"));
        var v = VenvLocator.Find(P("proj", "s.py"), Root, fs)!;
        Assert.False(v.Broken);
        Assert.Equal(Path.Combine(dir, "bin", "python"), v.Python);
        Assert.Equal(Path.Combine(dir, "bin"), v.BinDir);
        Assert.Equal("", v.Version);
    }

    [Theory]
    [InlineData("home = C:\\Py\nversion = 3.12.4\n", "3.12.4")]
    [InlineData("home = /usr\r\nversion_info = 3.11.9.final.0\r\n", "3.11.9")]
    [InlineData("VERSION=3.10.1", "3.10.1")]
    [InlineData("home = C:\\Py", "")]
    [InlineData("", "")]
    public void Version_comes_from_pyvenv_cfg(string cfg, string version) => Assert.Equal(version, VenvLocator.ParseVersion(cfg));

    // ---------- Priority, setting, environment ----------

    [Fact]
    public void Venv_goes_before_the_custom_path_and_auto_detect()
    {
        var fs = new FakeFs();
        string exe = fs.Venv(P(".venv"), "3.12.4");
        var v = VenvLocator.Find(P("s.py"), Root, fs)!;
        var py = PythonInfo.FromVenv(v);
        Assert.Equal("venv", py.Source);
        Assert.Equal(exe, py.Path);
        Assert.Empty(py.Prefix);
        Assert.Equal(".venv · Python 3.12", py.RunLabel);
        Assert.Equal("Python 3.12.4", new PythonInfo { Version = "3.12.4" }.RunLabel);
        // The run command uses the venv interpreter, not the configured one
        Assert.Equal("\"-u\" \"s.py\"", PythonRun.InlineArgs(py, "s.py", ""));
    }

    [Fact]
    public void Use_venv_setting_is_on_by_default_and_old_files_load()
    {
        Assert.True(new Settings().UseVenv);
        var old = new Settings();
        old.LoadJson("{\"language\": \"ru\", \"pythonPath\": \"C:\\\\Py\\\\python.exe\"}");   // a 1.1.0 file: no useVenv
        Assert.True(old.UseVenv);
        Assert.Equal(@"C:\Py\python.exe", old.PythonPath);
        var off = new Settings { UseVenv = false };
        var back = new Settings();
        back.LoadJson(off.ToJson());
        Assert.False(back.UseVenv);
        Assert.Contains("\"useVenv\"", off.ToJson());
        // A stray key from older builds (such as a removed toggle) is ignored, not an error
        var stray = new Settings();
        stray.LoadJson("{\"win11Styling\": false, \"useVenv\": true}");
        Assert.True(stray.UseVenv);
    }

    [Fact]
    public void Venv_run_environment_is_like_activate()
    {
        var v = new PythonVenv(@"C:\S\.venv", @"C:\S\.venv\Scripts\python.exe", "3.12.4", false);
        var env = PythonRun.EnvFor(PythonInfo.FromVenv(v), @"C:\Windows;C:\Tools");
        var d = env.ToDictionary(e => e.Key, e => e.Value);
        Assert.Equal(@"C:\S\.venv", d["VIRTUAL_ENV"]);
        Assert.Equal(@"C:\S\.venv\Scripts;C:\Windows;C:\Tools", d["PATH"]);
        Assert.True(d.ContainsKey("PYTHONHOME"));
        Assert.Null(d["PYTHONHOME"]);   // removed
        Assert.Equal("1", d["PYTHONUTF8"]);
        // Without a venv nothing of that is set
        var plain = PythonRun.EnvFor(new PythonInfo { Path = @"C:\Py\python.exe" }, @"C:\Windows").Select(e => e.Key).ToList();
        Assert.DoesNotContain("VIRTUAL_ENV", plain);
        Assert.DoesNotContain("PATH", plain);
        Assert.DoesNotContain("PYTHONHOME", plain);
    }

    [Fact]
    public void Window_and_admin_runs_set_up_the_venv_too()
    {
        var v = new PythonVenv(@"C:\S\.venv", @"C:\S\.venv\Scripts\python.exe", "3.12.4", false);
        string w = PythonRun.WindowArgs(PythonInfo.FromVenv(v), @"C:\S\it's.py", "-v", new string('b', 64), @"C:\c\1.exit");
        Assert.Contains("$env:VIRTUAL_ENV='C:\\S\\.venv'", w);
        Assert.Contains("$env:PATH='C:\\S\\.venv\\Scripts;' + $env:PATH", w);
        Assert.Contains("Remove-Item Env:PYTHONHOME", w);
        Assert.Contains("& 'C:\\S\\.venv\\Scripts\\python.exe' '-u'", w);
        Assert.Contains("Write-Host '.venv · Python 3.12'", w);
        Assert.Contains("Get-FileHash", w);
        Assert.DoesNotContain("VIRTUAL_ENV", PythonRun.WindowArgs(new PythonInfo { Path = @"C:\Py\python.exe", Version = "3.12.1" }, @"C:\S\a.py", "", null, @"C:\c\1.exit"));
    }

    // ---------- A real venv (python3 -m venv) ----------

    [PythonVenvFact]
    public void Real_venv_is_found_and_runs_with_its_own_environment()
    {
        string host = PythonTests.HostPython()!;
        string root = TestUtil.TempDir();
        string proj = Path.Combine(root, "proj");
        Directory.CreateDirectory(proj);
        string venvDir = Path.Combine(proj, ".venv");
        var psi = new System.Diagnostics.ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-m", "venv", "--without-pip", venvDir }) psi.ArgumentList.Add(a);
        using (var p = System.Diagnostics.Process.Start(psi)!)
        {
            p.StandardOutput.ReadToEnd(); string err = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(120_000) && p.ExitCode == 0, "python -m venv failed: " + err.Trim());
        }
        string script = Path.Combine(proj, "sub", "t.py");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "import os, sys\nprint(sys.prefix)\nprint(os.environ.get('VIRTUAL_ENV', ''))\nprint(os.environ['PATH'].split(os.pathsep)[0])\nprint('home' if 'PYTHONHOME' in os.environ else 'nohome')\n");
        var v = VenvLocator.Find(script, root, SystemVenvFileSystem.Instance);
        Assert.NotNull(v);
        Assert.False(v!.Broken);   // Scripts\python.exe on Windows, bin/python elsewhere
        Assert.Equal(venvDir, v.Root);
        Assert.NotEqual("", v.Version);
        var py = PythonInfo.FromVenv(v);
        var env = PythonRun.EnvFor(py, Environment.GetEnvironmentVariable("PATH")).ToList();
        env.Add(("PYTHONHOME", "C:\\bogus"));   // set before: EnvFor removes it again (null value comes after)
        env.Add(("PYTHONHOME", null));
        using var r = new InlineRunner { StreamGrace = TimeSpan.FromHours(1) };
        using var done = new ManualResetEventSlim();
        r.Finished += _ => done.Set();
        r.Start(py.Path, PythonRun.InlineArgs(py, script, ""), Path.GetDirectoryName(script)!, env);
        Assert.True(done.Wait(TimeSpan.FromSeconds(60)));
        var lines = new List<string>();
        while (r.Lines.TryDequeue(out var l)) lines.Add(l);
        Assert.Equal(0, r.ExitCode);
        string norm(string s) => Path.GetFullPath(s.Trim()).TrimEnd(Path.DirectorySeparatorChar);
        Assert.Contains(lines, l => l.Trim().Length > 0 && !l.StartsWith("!") && SafeEq(l, venvDir));   // sys.prefix is the venv
        Assert.Contains(venvDir, lines.Select(l => l.Trim()));                                            // VIRTUAL_ENV
        Assert.Contains(lines, l => SafeEq(l, v.BinDir));                                                   // first on PATH
        Assert.Contains("nohome", lines);
        bool SafeEq(string a, string b) { try { return string.Equals(norm(a), norm(b), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    }
}

/// <summary>A test that needs a real Python 3 with the venv module (python3 -m venv); skipped with the reason otherwise.</summary>
public sealed class PythonVenvFactAttribute : FactAttribute
{
    public PythonVenvFactAttribute()
    {
        string? py = PythonTests.HostPython();
        if (py == null) { Skip = "Python 3 not found (install it or set SHELLY_TEST_PYTHON)"; return; }
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(py) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-c", "import venv" }) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            if (!p.WaitForExit(20_000) || p.ExitCode != 0) Skip = "Python has no venv module (install python3-venv)";
        }
        catch (Exception ex) { Skip = "Python could not be started: " + ex.Message; }
    }
}
