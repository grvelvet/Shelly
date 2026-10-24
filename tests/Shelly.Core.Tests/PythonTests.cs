using Xunit;

namespace Shelly.Core.Tests;

/// <summary>Python scripts (1.1.0): discovery, the runner by extension, the command line, the interpreter search (on a fake
/// machine, so it runs on any OS) and, when a Python 3 is available, a real run.</summary>
public class PythonTests
{
    static void Write(string dir, string name, string text = "print(1)")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), text);
    }

    // ---------- Discovery ----------

    [Fact]
    public void Scan_lists_py_next_to_ps1_in_Scripts_and_in_external_folders()
    {
        string root = TestUtil.TempDir();
        var paths = new AppPaths(Path.Combine(root, "shelly"));
        paths.EnsureFolders();
        string ext = Path.Combine(root, "proj");
        Write(paths.Scripts, "a.ps1", "Write-Host 1");
        Write(paths.Scripts, "b.py");
        Write(paths.Scripts, "C.PY");
        Write(paths.Scripts, "data.pyc");
        Write(paths.Scripts, "notes.txt");
        Write(ext, "run.py");
        Write(Path.Combine(ext, "Sub"), "deep.py");
        var files = ScriptCatalog.Scan(paths, new[] { ext });
        Assert.Equal(new[] { "a.ps1", "b.py", "C.PY" }, files.Where(f => !f.IsExternal).Select(f => f.Rel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        string key = ExternalScripts.RootKey(ext);
        Assert.Contains(files, f => f.Rel == key + "\\run.py" && f.IsExternal && f.IsPython && f.Name == "run");
        Assert.Contains(files, f => f.Rel == key + "\\Sub\\deep.py" && f.Folder == key + "\\Sub");
        Assert.False(files.Single(f => f.Rel == "a.ps1").IsPython);
        Assert.True(files.Single(f => f.Rel == "b.py").IsPython);
        Assert.True(files.Single(f => f.Rel == "C.PY").IsPython);
        Assert.All(files, f => Assert.Equal(64, f.Hash.Length));   // the SHA-256 is taken for .py the same way
    }

    [Fact]
    public void Scan_skips_private_modules_caches_and_virtual_environments()
    {
        string root = TestUtil.TempDir();
        var paths = new AppPaths(Path.Combine(root, "shelly"));
        paths.EnsureFolders();
        string ext = Path.Combine(root, "proj");
        Write(ext, "main.py");
        Write(ext, "_helpers.py");
        Write(ext, "__init__.py");
        Write(ext, ".hidden.py");
        Write(Path.Combine(ext, "__pycache__"), "main.py");
        Write(Path.Combine(ext, "node_modules", "x"), "tool.py");
        Write(Path.Combine(ext, "lib", "site-packages", "pkg"), "mod.py");
        string venv = Path.Combine(ext, "venv");
        Write(venv, "pyvenv.cfg", "home = C:\\Python312");
        Write(Path.Combine(venv, "Scripts"), "activate_this.py");
        Write(Path.Combine(ext, "tools"), "ok.py");
        var rels = ScriptCatalog.Scan(paths, new[] { ext }).Select(f => f.Rel.Substring(f.Rel.IndexOf('\\') + 1)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "main.py", "tools\\ok.py" }, rels);
        Assert.True(ScriptCatalog.SkipFolder(venv));
        Assert.False(ScriptCatalog.SkipFolder(Path.Combine(ext, "tools")));
    }

    [Fact]
    public void A_py_and_a_ps1_with_the_same_name_are_two_scripts()
    {
        string root = TestUtil.TempDir();
        var paths = new AppPaths(Path.Combine(root, "shelly"));
        paths.EnsureFolders();
        Write(paths.Scripts, "report.ps1", "Write-Host 1");
        Write(paths.Scripts, "report.py");
        var files = ScriptCatalog.Scan(paths);
        Assert.Equal(2, files.Count);
        Assert.Equal(2, files.Select(f => f.Rel).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files, f => Assert.Equal("report", f.Name));
    }

    // ---------- Runner by extension ----------

    [Theory]
    [InlineData("a.py", "python", true)]
    [InlineData(@"C:\x\Tool.PY", "python", true)]
    [InlineData("ext:0123456789ab\\sub\\t.py", "python", true)]
    [InlineData("a.ps1", "powershell", false)]
    [InlineData("A.PS1", "powershell", false)]
    [InlineData("a.pyw", "powershell", false)]
    [InlineData("a.py.txt", "powershell", false)]
    public void Runner_is_chosen_by_the_extension(string path, string runner, bool py)
    {
        Assert.Equal(runner, ScriptKinds.RunnerOf(path));
        Assert.Equal(py, ScriptKinds.IsPython(path));
        Assert.Equal(py, new ScriptFile { Path = path, Rel = path }.IsPython);
    }

    [Fact]
    public void Script_kinds_know_both_extensions()
    {
        Assert.Equal(".ps1", ScriptKinds.Ext("x.PS1"));
        Assert.Equal(".py", ScriptKinds.Ext("x.Py"));
        Assert.Null(ScriptKinds.Ext("x.psm1"));
        Assert.Null(ScriptKinds.Ext(null));
        Assert.Equal("tool", ScriptKinds.StripExt("tool.py", out var e1));
        Assert.Equal(".py", e1);
        Assert.Equal("tool.txt", ScriptKinds.StripExt("tool.txt", out var e2));
        Assert.Null(e2);
        Assert.True(ScriptKinds.IsHiddenPython("__init__.py"));
        Assert.False(ScriptKinds.IsHiddenPython("_x.ps1"));
    }

    [Fact]
    public void Python_meta_has_no_PowerShell_analysis()
    {
        string dir = TestUtil.TempDir();
        string p = Path.Combine(dir, "t.py");
        File.WriteAllText(p, "#!/usr/bin/env python3\n# -*- coding: utf-8 -*-\n\"\"\"Курс валют за сегодня.\n\nДлинное описание.\"\"\"\nprint(1)\n");
        var f = new ScriptFile { Path = p, Rel = "t.py", Name = "t", Hash = Safety.FileSha256(p) };
        using var svc = new AnalysisService(new AnalysisService.Engines { Ps51 = "powershell.exe" }, Path.Combine(dir, "cache.json"), dir);
        Assert.False(svc.IsPending(f));
        svc.Request(new[] { f });   // never queued for the PowerShell analyzer
        Assert.False(svc.IsPending(f));
        var m = svc.Meta(f, new Settings(), false, true);
        Assert.True(m.IsPython);
        Assert.Equal(EngineInfo.Python, m.Engine.Engine);
        Assert.Equal("none", m.Scan);
        Assert.False(m.Pending);
        Assert.Empty(m.Params);
        Assert.Empty(m.Danger);
        Assert.Empty(m.Issues);
        Assert.Equal("Курс валют за сегодня.", m.Desc);
        Assert.Equal("Python", EngineInfo.Name(EngineInfo.Python));
        Assert.Equal((true, "PY", true), EngineInfo.PythonMark(true));
    }

    [Theory]
    [InlineData("\"\"\"One line.\"\"\"\nprint(1)", "One line.")]
    [InlineData("'''\n  Second line text\n'''", "Second line text")]
    [InlineData("#!/usr/bin/env python\n# Shelly-Mode: window\n# Description: Backup of D:\\Work\nimport os", "Backup of D:\\Work")]
    [InlineData("import os\n# not a header", "")]
    [InlineData("r\"\"\"Raw docstring\"\"\"", "Raw docstring")]
    [InlineData("", "")]
    public void Description_comes_from_the_docstring_or_the_first_comment(string text, string desc) =>
        Assert.Equal(desc, PythonRun.Description(text));

    [Fact]
    public void Shelly_directives_work_in_python_comments()
    {
        Assert.Equal("window", ScriptHeader.ModeIn("#!/usr/bin/env python3\n# Shelly-Mode: window\nprint(1)"));
        Assert.Equal("\uE9D2", ScriptHeader.IconGlyph("# Shelly-Icon: E9D2\nprint(1)"));
    }

    // ---------- Command line ----------

    static PythonInfo Py(params string[] prefix) => new() { Path = @"C:\Python312\python.exe", Version = "3.12.1", Prefix = prefix };

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("  ", new string[0])]
    [InlineData("--name Anna -v", new[] { "--name", "Anna", "-v" })]
    [InlineData("--name \"Anna Smith\" x", new[] { "--name", "Anna Smith", "x" })]
    [InlineData("\"C:\\Program Files\\\\\" next", new[] { "C:\\Program Files\\", "next" })]
    [InlineData("say \\\"hi\\\"", new[] { "say", "\"hi\"" })]
    [InlineData("a\\\\b c", new[] { "a\\\\b", "c" })]
    [InlineData("\"\" x", new[] { "", "x" })]
    [InlineData("\"он сказал \"\"да\"\"\"", new[] { "он сказал \"да\"" })]
    public void Arguments_are_split_like_a_windows_command_line(string text, string[] expected) =>
        Assert.Equal(expected, PythonRun.Split(text).ToArray());

    [Fact]
    public void Inline_command_is_unbuffered_and_quotes_every_argument()
    {
        string args = PythonRun.InlineArgs(Py(), @"C:\Scripts\Отчёт 1.py", "--name \"Anna Smith\" -v");
        Assert.Equal("\"-u\" \"C:\\Scripts\\Отчёт 1.py\" \"--name\" \"Anna Smith\" \"-v\"", args);
        Assert.Equal(new[] { "-3", "-u", "s.py" }, PythonRun.InlineArgList(Py("-3"), "s.py", null));
        // Splitting what is passed gives back exactly the list (what Python sees in sys.argv[1:])
        var list = PythonRun.InlineArgList(Py(), @"C:\a b\s.py", "x \"y z\" \"q\\\"\"");
        Assert.Equal(list, PythonRun.Split(string.Join(" ", list.Select(Quoting.NativeArg))));
    }

    [Fact]
    public void Environment_makes_the_output_utf8_and_unbuffered()
    {
        var env = PythonRun.EnvVars.ToDictionary(e => e.Key, e => e.Value);
        Assert.Equal("1", env["PYTHONUTF8"]);
        Assert.Equal("utf-8", env["PYTHONIOENCODING"]);
        Assert.Equal("1", env["PYTHONUNBUFFERED"]);
    }

    [Fact]
    public void Window_command_sets_the_folder_and_checks_the_hash_for_admin_runs()
    {
        string hash = new string('a', 64);
        const string done = @"C:\c\1.exit";
        string w = PythonRun.WindowArgs(Py(), @"C:\Work\it's.py", "-v", hash, done);
        Assert.StartsWith("-NoProfile -NoExit -ExecutionPolicy Bypass -Command ", w);
        Assert.Contains("Set-Location -LiteralPath " + Quoting.PsString(Path.GetDirectoryName(@"C:\Work\it's.py") ?? ""), w);   // C:\Work on Windows
        Assert.Contains("$env:PYTHONUTF8='1'", w);
        Assert.Contains("& 'C:\\Python312\\python.exe' '-u' 'C:\\Work\\it''s.py' '-v'", w);
        Assert.Contains("Get-FileHash -LiteralPath 'C:\\Work\\it''s.py'", w);
        Assert.Contains(hash, w);
        Assert.DoesNotContain("Get-FileHash", PythonRun.WindowArgs(Py(), @"C:\Work\a.py", "", null, done));
        Assert.DoesNotContain("Get-FileHash", PythonRun.WindowArgs(Py(), @"C:\Work\a.py", "", "not-a-hash", done));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("--a 1\t--b 2", true)]
    [InlineData("--a\n--b", false)]
    [InlineData("--a\r", false)]
    [InlineData("x\0", false)]
    public void Saved_arguments_must_be_one_line(string text, bool ok) => Assert.Equal(ok, PythonRun.IsValidArgs(text));

    [Fact]
    public void Arguments_ride_in_run_presets_and_the_history_text()
    {
        Assert.False(PythonRun.IsValidArgs(new string('x', PythonRun.MaxArgsLength + 1)));
        var s = new Settings();
        RunPreset.Set(s, "tools\\a.py", "window", new Dictionary<string, object> { [PythonRun.ArgsKey] = "--n \"Анна\"" });
        var p = RunPreset.Get(s, "tools\\a.py")!;
        Assert.Equal("window", p.Mode);
        Assert.Equal("--n \"Анна\"", PythonRun.ArgsFrom(p.Values));
        Assert.Equal("", PythonRun.ArgsFrom(null));
        Assert.Equal(" --n \"Анна\"", PythonRun.Display(" --n \"Анна\" "));
        Assert.Equal("", PythonRun.Display("  "));
    }

    // ---------- New script, rename, drop ----------

    [Fact]
    public void A_name_ending_in_py_creates_a_python_script()
    {
        string scripts = TestUtil.TempDir();
        var r = ScriptCatalog.ResolveNewScript(scripts, "Tools\\report.py")!;
        Assert.Equal("", r.Error);
        Assert.Equal("report", r.Name);
        Assert.Equal(".py", r.Ext);
        Assert.Equal(Path.Combine(scripts, "Tools"), r.Dir);
        Assert.Equal(".ps1", ScriptCatalog.ResolveNewScript(scripts, "report")!.Ext);
        Assert.Equal(".ps1", ScriptCatalog.ResolveNewScript(scripts, "report.ps1")!.Ext);
        Assert.Equal("new", ScriptCatalog.CheckRename("new.py")!.Name);
        Assert.Equal("x", ScriptCatalog.SafeName("x.PY"));
        string t = ScriptCatalog.Template("Мой \"скрипт\"", ".py");
        Assert.Contains("print(", t);
        Assert.Contains("\\\"скрипт\\\"", t);
        Assert.DoesNotContain("Write-Host", t);
        Assert.Contains("Write-Host", ScriptCatalog.Template("x"));
    }

    [Fact]
    public void Dropped_py_files_are_copied_with_their_extension()
    {
        string root = TestUtil.TempDir();
        string scripts = Path.Combine(root, "Scripts");
        Directory.CreateDirectory(scripts);
        string src = Path.Combine(root, "src");
        Write(src, "tool.py");
        Write(src, "tool.ps1", "Write-Host 1");
        Write(src, "_private.py");
        Write(src, "cache.pyc");
        Write(scripts, "same.py", "x");
        Write(src, "same.py", "y");
        var plan = ScriptCatalog.PlanDrop(scripts, new[] { "tool.py", "tool.ps1", "_private.py", "cache.pyc", "same.py" }.Select(n => Path.Combine(src, n)));
        Assert.Equal(new[] { Path.Combine(scripts, "tool.py"), Path.Combine(scripts, "tool.ps1"), Path.Combine(scripts, "same (2).py") }, plan.Copy.Select(c => c.Target).ToArray());
        Assert.Equal(2, plan.Ignored);
    }

    [Fact]
    public void Python_path_is_kept_in_the_settings_file()
    {
        var s = new Settings { PythonPath = @"C:\Python312\python.exe" };
        var back = new Settings();
        back.LoadJson(s.ToJson());
        Assert.Equal(@"C:\Python312\python.exe", back.PythonPath);
        var q = new Settings();
        q.LoadJson("{\"pythonPath\": \"  \\\"C:\\\\Py\\\\python.exe\\\"  \"}");
        Assert.Equal(@"C:\Py\python.exe", q.PythonPath);
        Assert.Equal("", new Settings().PythonPath);
        Assert.Contains("\"pythonPath\"", new Settings().ToJson());
    }

    [Fact]
    public void New_strings_exist_in_both_languages_without_long_dashes()
    {
        string[] keys = { "runHeaderPy", "pyWinExit", "pyMissingTitle", "pyMissingText", "pyBadPathText", "pyNoScan", "pyNoBuild", "pyMarkTip",
            "pySumCustom", "pySumAuto", "pySumNone", "pySumBad", "pyPick", "pyReset", "setShell", "setUseVenv", "pyVenvBrokenText", "rsPyArgs", "rsPyArgsBad", "cPython", "cmdAddScripts", "scriptsFilter", "edScanPython" };
        string old = Loc.Language;
        try
        {
            foreach (var lang in new[] { "en", "ru" })
            {
                Loc.SetLanguage(lang);
                foreach (var k in keys)
                {
                    Assert.True(Loc.Has(k), k);
                    string t = Loc.T(k);
                    Assert.False(string.IsNullOrWhiteSpace(t) || t == k, k);
                    Assert.DoesNotContain("\u2014", t);
                }
                // 1.3.0: the filter takes the extensions ({0}), the custom interpreters' ones included
                Assert.EndsWith("|*.ps1;*.py", Loc.F("scriptsFilter", "*.ps1;*.py"));
                Assert.Contains("(*.ps1;*.py)", Loc.F("scriptsFilter", "*.ps1;*.py"));
            }
            Assert.StartsWith("Скрипты", Loc.T("scriptsFilter"));
        }
        finally { Loc.SetLanguage(old); }
    }

    // ---------- Interpreter search on a fake machine ----------

    sealed class FakeMachine : IPythonEnvironment
    {
        public readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase)
        {
            ["LOCALAPPDATA"] = @"C:\Users\v\AppData\Local", ["ProgramFiles"] = @"C:\Program Files", ["WINDIR"] = @"C:\Windows"
        };
        public readonly List<string> Path = new();
        /// <summary>exe|prefix -> probe output (null: the process fails, like the Store placeholder).</summary>
        public readonly Dictionary<string, string?> Answers = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Probed = new();

        public FakeMachine Python(string exe, string version, string? executable = null, string prefix = "")
        {
            Files.Add(exe);
            Answers[exe + "|" + prefix] = version + "\n" + (executable ?? exe);
            return this;
        }

        public bool FileExists(string path) => Files.Contains(path);
        public string? GetVariable(string name) => Vars.TryGetValue(name, out var v) ? v : null;
        public IEnumerable<string> PathDirectories() => Path;
        public IEnumerable<string> GetDirectories(string parent, string pattern)
        {
            string pre = parent.TrimEnd('\\') + "\\";
            string start = pattern.TrimEnd('*');
            return Files.Where(f => f.StartsWith(pre, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(pre.Length).Split('\\')[0])
                .Where(d => d.StartsWith(start, StringComparison.OrdinalIgnoreCase) && Files.Any(f => f.StartsWith(pre + d + "\\", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(d => pre + d).ToList();
        }
        public PythonProbe? Probe(string exe, IReadOnlyList<string> prefix)
        {
            string key = exe + "|" + string.Join(" ", prefix);
            Probed.Add(key);
            return Answers.TryGetValue(key, out var a) && a != null ? PythonLocator.ParseProbe(a) : null;
        }
    }

    const string Local = @"C:\Users\v\AppData\Local";
    const string Store = Local + @"\Microsoft\WindowsApps";

    [Fact]
    public void Configured_path_wins_and_is_the_only_one_tried()
    {
        var m = new FakeMachine().Python(@"D:\Tools\py311\python.exe", "3.11.9").Python(@"C:\Windows\py.exe", "3.13.0", @"C:\Python313\python.exe", "-3");
        m.Files.Add(@"C:\Python313\python.exe");
        var py = PythonLocator.Find(@"D:\Tools\py311\python.exe", m, out var err);
        Assert.NotNull(py);
        Assert.Equal(@"D:\Tools\py311\python.exe", py!.Path);
        Assert.Equal("3.11.9", py.Version);
        Assert.Equal("setting", py.Source);
        Assert.Equal("", err);

        Assert.Null(PythonLocator.Find(@"D:\nope\python.exe", m, out err));
        Assert.Equal("pyBadPath", err);   // no silent fallback to another Python
        Assert.DoesNotContain(m.Probed, p => p.StartsWith(@"C:\Windows\py.exe"));
    }

    [Fact]
    public void Py_launcher_comes_first_and_resolves_to_the_real_python()
    {
        var m = new FakeMachine();
        m.Path.AddRange(new[] { @"C:\Tools", Store, @"C:\Windows" });
        m.Python(@"C:\Windows\py.exe", "3.12.4", Local + @"\Programs\Python\Python312\python.exe", "-3");
        m.Python(Local + @"\Programs\Python\Python312\python.exe", "3.12.4");
        m.Python(@"C:\Tools\python.exe", "3.10.0");
        var py = PythonLocator.Find("", m, out var err);
        Assert.Equal("py", py!.Source);
        Assert.Equal(Local + @"\Programs\Python\Python312\python.exe", py.Path);
        Assert.Empty(py.Prefix);
        Assert.Equal("3.12.4", py.Version);
        Assert.Equal("", err);
    }

    [Fact]
    public void Store_placeholder_is_skipped_and_the_install_folder_is_used()
    {
        var m = new FakeMachine();
        m.Path.AddRange(new[] { Store, @"C:\Other" });
        m.Files.Add(Store + @"\python.exe");
        m.Answers[Store + @"\python.exe|"] = null;   // "Python was not found; run without arguments to install from the Microsoft Store"
        m.Python(Local + @"\Programs\Python\Python39\python.exe", "3.9.13");
        m.Python(Local + @"\Programs\Python\Python313\python.exe", "3.13.1");
        m.Python(Local + @"\Programs\Python\Python310\python.exe", "3.10.11");
        var c = PythonLocator.Candidates(m).Select(x => x.Exe).ToList();
        Assert.Equal(new[] { Store + @"\python.exe", Local + @"\Programs\Python\Python313\python.exe", Local + @"\Programs\Python\Python310\python.exe", Local + @"\Programs\Python\Python39\python.exe" }, c);
        var py = PythonLocator.Find(null, m, out _);
        Assert.Equal(Local + @"\Programs\Python\Python313\python.exe", py!.Path);
        Assert.Equal("common", py.Source);
    }

    [Fact]
    public void Path_python_is_tried_before_the_store_alias_and_the_install_folders()
    {
        var m = new FakeMachine();
        m.Path.AddRange(new[] { Store, @"C:\Python311" });
        m.Python(Store + @"\python.exe", "3.12.0", @"C:\Program Files\WindowsApps\PythonSoftwareFoundation.Python.3.12_x\python.exe");
        m.Python(@"C:\Python311\python.exe", "3.11.2");
        Assert.Equal(new[] { @"C:\Python311\python.exe", Store + @"\python.exe" }, PythonLocator.Candidates(m).Select(x => x.Exe).ToArray());
        Assert.Equal(@"C:\Python311\python.exe", PythonLocator.Find(null, m, out _)!.Path);
        // A working Store Python: its real exe in Program Files\WindowsApps cannot be started directly, the alias is kept
        m.Answers[@"C:\Python311\python.exe|"] = null;
        var py = PythonLocator.Find(null, m, out _);
        Assert.Equal(Store + @"\python.exe", py!.Path);
        Assert.Equal("path", py.Source);
    }

    [Fact]
    public void Launcher_keeps_its_prefix_when_the_real_exe_is_unknown()
    {
        var m = new FakeMachine();
        m.Python(Local + @"\Programs\Python\Launcher\py.exe", "3.13.0", "", "-3");
        var py = PythonLocator.Find(null, m, out _);
        Assert.Equal(Local + @"\Programs\Python\Launcher\py.exe", py!.Path);
        Assert.Equal(new[] { "-3" }, py.Prefix);
        Assert.Equal("\"-3\" \"-u\" \"s.py\"", PythonRun.InlineArgs(py, "s.py", ""));
    }

    [Fact]
    public void Python_2_and_nothing_at_all_mean_not_found()
    {
        var m = new FakeMachine();
        m.Path.Add(@"C:\Python27");
        m.Python(@"C:\Python27\python.exe", "2.7.18");
        Assert.Null(PythonLocator.Find(null, m, out var err));
        Assert.Equal("pyNotFound", err);
        Assert.Null(PythonLocator.Find(null, new FakeMachine(), out err));
        Assert.Equal("pyNotFound", err);
    }

    [Theory]
    [InlineData("3.12.1\nC:\\Py\\python.exe\n", "3.12.1", "C:\\Py\\python.exe")]
    [InlineData("3.13.0rc1\r\n/usr/bin/python3\r\n", "3.13.0rc1", "/usr/bin/python3")]
    [InlineData("3.11.2", "3.11.2", "")]
    public void Probe_output_is_parsed(string output, string version, string exe)
    {
        var p = PythonLocator.ParseProbe(output)!;
        Assert.Equal(version, p.Version);
        Assert.Equal(exe, p.Executable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Python was not found; run without arguments to install from the Microsoft Store.")]
    public void Garbage_probe_output_is_not_a_python(string output) => Assert.Null(PythonLocator.ParseProbe(output));

    [Theory]
    [InlineData(@"C:\Users\v\AppData\Local\Programs\Python\Python312", 12)]
    [InlineData(@"C:\Program Files\Python39-32", 9)]
    [InlineData(@"C:\Program Files\PythonX", -1)]
    public void Install_folders_are_ordered_by_version(string dir, int minor) => Assert.Equal(minor, PythonLocator.MinorOf(dir));

    // ---------- A real run (when a Python 3 is on this machine) ----------

    /// <summary>A Python 3 on this machine for the run tests: SHELLY_TEST_PYTHON, else python3 / python on PATH (not the
    /// Store placeholder); null when there is none.</summary>
    internal static string? HostPython()
    {
        string? env = Environment.GetEnvironmentVariable("SHELLY_TEST_PYTHON");
        var tries = new List<string>();
        if (!string.IsNullOrEmpty(env)) tries.Add(env);
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            if (d.Trim().Length == 0) continue;
            foreach (var n in OperatingSystem.IsWindows() ? new[] { "python.exe", "python3.exe" } : new[] { "python3", "python" })
                tries.Add(System.IO.Path.Combine(d.Trim(), n));
        }
        foreach (var p in tries)
        {
            if (!File.Exists(p)) continue;
            try { if (SystemPythonEnvironment.Instance.Probe(p, Array.Empty<string>()) is { } r && PythonLocator.IsPython3(r.Version)) return p; } catch { }
        }
        return null;
    }

    static InlineRunner RunPy(string code, string args, out List<string> lines, out string dir)
    {
        string exe = HostPython()!;
        dir = TestUtil.TempDir();
        string script = System.IO.Path.Combine(dir, "t.py");
        File.WriteAllText(script, code, new System.Text.UTF8Encoding(false));
        var py = new PythonInfo { Path = exe, Version = "3" };
        var r = new InlineRunner { StreamGrace = TimeSpan.FromHours(1) };
        using var done = new ManualResetEventSlim();
        r.Finished += _ => done.Set();
        r.Start(exe, PythonRun.InlineArgs(py, script, args), dir, PythonRun.EnvFor(py, Environment.GetEnvironmentVariable("PATH")));
        Assert.True(done.Wait(TimeSpan.FromSeconds(60)));
        lines = new List<string>();
        while (r.Lines.TryDequeue(out var l)) lines.Add(l);
        return r;
    }

    [PythonFact]
    public void Real_run_keeps_cyrillic_passes_arguments_and_the_exit_code()
    {
        using var r = RunPy("import os, sys\nprint('Привет, мир')\nprint(os.getcwd())\nprint('|'.join(sys.argv[1:]))\nprint('ошибка', file=sys.stderr)\nsys.exit(3)\n",
            "--name \"Анна Смит\" -v", out var lines, out var dir);
        Assert.Contains("Привет, мир", lines);
        Assert.Contains("--name|Анна Смит|-v", lines);
        Assert.Contains("! ошибка", lines);
        Assert.Contains(lines, l => string.Equals(System.IO.Path.GetFullPath(l.Trim()).TrimEnd(System.IO.Path.DirectorySeparatorChar), System.IO.Path.GetFullPath(dir).TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || l.Trim().EndsWith(System.IO.Path.GetFileName(dir), StringComparison.Ordinal));
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("ошибка", r.ErrorText());
    }

    [PythonFact]
    public void Real_run_can_be_stopped()
    {
        string exe = HostPython()!;
        string dir = TestUtil.TempDir();
        string script = System.IO.Path.Combine(dir, "sleep.py");
        File.WriteAllText(script, "import time\nprint('start', flush=True)\ntime.sleep(60)\n");
        using var r = new InlineRunner();
        using var done = new ManualResetEventSlim();
        r.Finished += _ => done.Set();
        r.Start(exe, PythonRun.InlineArgs(new PythonInfo { Path = exe }, script, ""), dir, PythonRun.EnvFor(new PythonInfo { Path = exe }, null));
        Thread.Sleep(800);
        r.Stop();
        Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(r.StoppedByUser);
        Assert.Equal("stopped", RunResult.KindFor(r.ExitCode, r.StoppedByUser));
    }

    [PythonFact]
    public void Real_interpreter_answers_the_probe()
    {
        var p = SystemPythonEnvironment.Instance.Probe(HostPython()!, Array.Empty<string>());
        Assert.NotNull(p);
        Assert.True(PythonLocator.IsPython3(p!.Version));
        Assert.NotEqual("", p.Executable);
    }
}

/// <summary>A test that needs a real Python 3 (SHELLY_TEST_PYTHON or python3 / python on PATH); skipped with the reason otherwise.</summary>
public sealed class PythonFactAttribute : FactAttribute
{
    public PythonFactAttribute()
    {
        if (PythonTests.HostPython() == null) Skip = "Python 3 not found (install it or set SHELLY_TEST_PYTHON)";
    }
}
