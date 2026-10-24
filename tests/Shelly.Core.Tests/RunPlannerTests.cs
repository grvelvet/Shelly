using System.Text.Json;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.3.1: RunPlanner, the checks shared by interactive runs (RunManager) and scheduled runs (Unattended).</summary>
[Collection("CustomRegistry")]
public class RunPlannerTests : IDisposable
{
    const string Ps51 = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    const string Pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";
    const string Done = @"C:\Shelly\cache\runs\1.exit";
    readonly string dir = TestUtil.TempDir();

    public RunPlannerTests() => Loc.SetLanguage("en");
    public void Dispose() => ScriptKinds.SetCustom(null);

    sealed class Env : IRunEnvironment
    {
        public Settings Settings { get; } = new() { DisclaimerAccepted = "1.3.1", DefaultMode = "inline" };
        public Dictionary<string, ScriptFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Func<ScriptFile, ScriptMeta> MetaOf { get; set; } = _ => new ScriptMeta();
        public ScriptFile? Find(string rel) => Files.GetValueOrDefault(rel);
        public ScriptMeta Meta(ScriptFile f) => MetaOf(f);
        public bool PwshOk => PwshPath != null;
        public bool Ps51Ok => Ps51Path != null;
        public string? PwshPath { get; set; } = Pwsh;
        public string? Ps51Path { get; set; } = Ps51;
        public AnalysisService.Engines Engines => new() { Ps51 = Ps51Path, Pwsh = PwshPath };
        public TimeSpan? Waited { get; private set; }
        public Task WaitForAnalysisAsync(ScriptFile f, TimeSpan timeout) { Waited = timeout; return Task.CompletedTask; }
        public PythonVenv? Venv { get; set; }
        public PythonVenv? VenvFor(ScriptFile f) => Venv;
        public bool PythonLookupPending => false;
        public string PythonError => "";
        public PythonInfo? Python { get; set; } = new() { Path = @"C:\Py\python.exe", Version = "3.12.1" };
        public Task<PythonInfo?> EnsurePythonAsync() => Task.FromResult(Python);
        public HashSet<string> Busy { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool InlineRunning(string rel) => Busy.Contains(rel);
        public List<string> Logs { get; } = new();
        public void Log(string text) => Logs.Add(text);
        /// <summary>Replaces the file's hash from the n-th read on (a file that changes during the checks).</summary>
        public int ChangeAfterReads { get; set; } = int.MaxValue;
        int reads;
        public string FileSha256(string path) => ++reads > ChangeAfterReads ? new string('9', 64) : Safety.FileSha256(path);
        public List<(string Path, ZoneInfo Zone)> Marked { get; } = new();
        public List<(string Path, ZoneInfo Zone)> MarkedReferences(string path) => Marked;
        public string Now => "12:00:00";
    }

    sealed class Ui : IRunPrompts
    {
        public List<string> Calls { get; } = new();
        public List<string> Statuses { get; } = new();
        public RunAnswer? Answer { get; set; }
        public bool Unverified { get; set; } = true;
        public bool Safe { get; set; } = true;
        public string? Compat { get; set; } = "anyway";
        public IReadOnlyList<RunChoice>? Choices { get; private set; }
        public void Status(string text, bool important = false) { if (text.Length > 0) Statuses.Add(text); }
        public RunAnswer? AskRunSettings(ScriptFile f) { Calls.Add("ask"); return Answer; }
        public bool ConfirmUnverified(ScriptFile f, bool engineAvailable) { Calls.Add("unverified"); return Unverified; }
        public bool ConfirmSafety(ScriptFile f, ScriptMeta meta, string action) { Calls.Add("safety:" + action); return Safe; }
        public void MotwBlocked(ScriptFile f, ScriptMeta meta, List<(string Path, ZoneInfo Zone)> others) => Calls.Add("motw");
        public string? ChooseCompat(ScriptFile f, string text, bool warn, IReadOnlyList<RunChoice> choices) { Calls.Add("compat"); Choices = choices; return Compat; }
        public void InstallPwsh() => Calls.Add("install7");
        public void VenvBroken(ScriptFile f, PythonVenv venv) => Calls.Add("venv");
        public void PythonMissing(ScriptFile f, string text) => Calls.Add("python");
        public void CustomMissing(ScriptFile f, string text) => Calls.Add("custom");
    }

    ScriptFile Add(Env env, string rel, string content)
    {
        string path = Path.Combine(dir, rel);
        File.WriteAllText(path, content);
        var f = new ScriptFile { Path = path, Rel = rel, Name = Path.GetFileNameWithoutExtension(rel), Hash = Safety.FileSha256(path) };
        env.Files[rel] = f;
        return f;
    }

    static ParamInfo Param(string name, bool mandatory = false) => new() { Name = name, Type = "string", Kind = "text", Mandatory = mandatory };
    static DangerFinding Finding(string sev) => new() { Rule = "test", Sev = sev };

    static Task<PlanResult> Interactive(Env env, Ui ui, ScriptFile f, RunOptions? o = null) => RunPlanner.PlanInteractiveAsync(env, ui, f, o ?? new RunOptions());

    // ---------- PowerShell ----------

    [Fact]
    public async Task Interactive_powershell_run_gets_the_engine_the_arguments_and_the_header()
    {
        var env = new Env();
        var f = Add(env, "a.ps1", "param($X)\n'hi'\n");
        env.MetaOf = _ => new ScriptMeta { Params = { Param("X") } };
        RunPreset.Set(env.Settings, "a.ps1", "inline", new Dictionary<string, object> { ["X"] = "1" });
        var ui = new Ui();
        var r = await Interactive(env, ui, f);
        Assert.Equal(PlanStatus.Ready, r.Status);
        var p = r.Plan!;
        Assert.Equal("inline", p.Mode);
        Assert.Equal(Ps51, p.Exe);
        Assert.Equal("5.1", p.Engine);
        Assert.Contains("-X", p.ArgText);
        Assert.Equal(RunArgs.InlineArgs(f.Path, p.ArgText), p.InlineArgs);
        Assert.Equal(Loc.F("runHeader", "a", p.ArgText, EngineInfo.Short("5.1"), "12:00:00"), p.Header);
        Assert.Empty(ui.Calls);   // nothing to ask
        Assert.Null(p.Preset);
        Assert.Equal(RunArgs.WindowArgs(f.Path, p.ArgText, null, Done), p.WindowArgs(Done));
    }

    [Theory]
    [InlineData(null, "list", null, null, "inline")]             // the default mode of Settings
    [InlineData(null, "list", "window", null, "window")]         // the header
    [InlineData(null, "list", "admin", "window", "window")]      // the remembered mode before the header
    [InlineData("window", "list", "admin", "inline", "window")]  // asked for: always wins
    [InlineData(null, "editor", "window", "window", "inline")]   // Save and Run stays inline
    [InlineData(null, "tray", "window", "window", "inline")]     // other entry points: the default
    public async Task Interactive_mode_selection(string? requested, string origin, string? header, string? remembered, string expected)
    {
        var env = new Env();
        var f = Add(env, "m.ps1", (header != null ? "# Shelly-Mode: " + header + "\n" : "") + "'x'\n");
        if (remembered != null) RunPreset.Set(env.Settings, "m.ps1", remembered, new Dictionary<string, object>());
        var ui = new Ui();
        var r = await Interactive(env, ui, f, new RunOptions { Mode = requested, Origin = origin });
        Assert.Equal(PlanStatus.Ready, r.Status);
        Assert.Equal(expected, r.Plan!.Mode);
        Assert.Equal(expected == "admin", ui.Calls.Contains("safety:admin"));
    }

    [Fact]
    public async Task Rerun_from_the_history_keeps_its_arguments_and_runs_inline_without_a_mode()
    {
        var env = new Env();
        var f = Add(env, "h.ps1", "param($X)\n");
        env.MetaOf = _ => new ScriptMeta { Params = { Param("X") } };
        var ui = new Ui();
        var r = await Interactive(env, ui, f, new RunOptions { RawArgs = " -X 'y'", Engine = "7", Origin = "rerun" });
        Assert.Equal("inline", r.Plan!.Mode);
        Assert.Equal(" -X 'y'", r.Plan.ArgText);
        Assert.Equal(Pwsh, r.Plan.Exe);   // the engine of the earlier run
        Assert.Equal("7", r.Plan.Engine);
        // Arguments Shelly did not write: dropped, the Run settings dialog asks instead
        ui.Answer = new RunAnswer("window", new Dictionary<string, object> { ["X"] = "z" }, null);
        r = await Interactive(env, ui, f, new RunOptions { RawArgs = "; Remove-Item C:\\", Origin = "rerun" });
        Assert.Contains("ask", ui.Calls);
        Assert.Equal("window", r.Plan!.Mode);
        Assert.Equal(" -X 'z'", r.Plan.ArgText);
        Assert.Single(env.Logs, l => l.StartsWith("History arguments rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_mandatory_parameters_open_the_run_settings()
    {
        var env = new Env();
        var f = Add(env, "p.ps1", "param([Parameter(Mandatory)]$Name)\n");
        env.MetaOf = _ => new ScriptMeta { Params = { Param("Name", true) } };
        var ui = new Ui();
        var r = await Interactive(env, ui, f);
        Assert.Equal(PlanStatus.Cancelled, r.Status);
        Assert.Contains(Loc.F("cxParams", "p"), ui.Statuses);
        ui.Answer = new RunAnswer("window", new Dictionary<string, object> { ["Name"] = "Ann" }, null);
        r = await Interactive(env, ui, f);
        Assert.Equal("window", r.Plan!.Mode);
        Assert.Contains("Ann", r.Plan.ArgText);
        // Scheduled: nobody to ask
        var s = await RunPlanner.PlanScheduledAsync(env, "p.ps1", "", false);
        Assert.Equal(PlanStatus.Blocked, s.Status);
        Assert.Equal("schedBlockParams", s.Block);
        Assert.Equal("Name", s.Arg);
    }

    [Fact]
    public async Task A_preset_gives_its_values_and_a_missing_one_stops_the_run()
    {
        var env = new Env();
        var f = Add(env, "q.ps1", "param($X)\n");
        env.MetaOf = _ => new ScriptMeta { Params = { Param("X") } };
        Presets.Save(env.Settings, "q.ps1", "Prod", new Dictionary<string, object> { ["X"] = "prod", ["Gone"] = "1" });
        var ui = new Ui();
        var o = new RunOptions { Preset = "Prod" };
        var r = await Interactive(env, ui, f, o);
        Assert.Equal("Prod", r.Plan!.Preset);
        Assert.Contains("prod", r.Plan.ArgText);
        Assert.NotNull(o.Values);   // kept for a re-check after a change, as before
        Assert.Contains(ui.Statuses, s => s.Contains("Gone"));
        r = await Interactive(env, ui, f, new RunOptions { Preset = "Nope" });
        Assert.Equal(PlanStatus.Cancelled, r.Status);
        Assert.Contains(Loc.F("presetMissing", "Nope", "q"), ui.Statuses);
        var s = await RunPlanner.PlanScheduledAsync(env, "q.ps1", "Prod", true);
        Assert.Equal(PlanStatus.Ready, s.Status);
        Assert.Equal("Prod", s.Plan!.Preset);
        Assert.Equal(new[] { "Gone" }, s.Plan.Ignored);
        s = await RunPlanner.PlanScheduledAsync(env, "q.ps1", "Nope", true);
        Assert.Equal("schedBlockPreset", s.Block);
        Assert.Equal("Nope", s.Arg);
    }

    [Fact]
    public async Task A_script_that_is_still_analyzed_is_waited_for_60_seconds()
    {
        var env = new Env();
        var f = Add(env, "w.ps1", "'x'\n");
        bool pending = true;
        env.MetaOf = _ => new ScriptMeta { Pending = pending };
        await Interactive(env, new Ui(), f);
        Assert.Equal(TimeSpan.FromSeconds(60), env.Waited);
        Assert.Equal(TimeSpan.FromSeconds(60), RunPlanner.AnalysisWait);
        pending = false;
        var env2 = new Env();
        Add(env2, "w.ps1", "'x'\n");
        env2.MetaOf = _ => new ScriptMeta { Pending = true };
        await RunPlanner.PlanScheduledAsync(env2, "w.ps1", "", false);
        Assert.Equal(TimeSpan.FromSeconds(60), env2.Waited);
    }

    [Fact]
    public async Task Busy_script_does_not_start_a_second_inline_run()
    {
        var env = new Env();
        var f = Add(env, "b.ps1", "'x'\n");
        env.Busy.Add("b.ps1");
        var ui = new Ui();
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Contains(Loc.F("busySame", "b"), ui.Statuses);
        Assert.Equal(PlanStatus.Ready, (await Interactive(env, ui, f, new RunOptions { Mode = "window" })).Status);
    }

    // ---------- The hash: what was checked is what starts ----------

    [Fact]
    public async Task A_file_that_changes_during_the_checks_is_not_started()
    {
        var env = new Env { ChangeAfterReads = 1 };   // the first read (the content checked) is the real one
        var f = Add(env, "c.ps1", "'x'\n");
        var r = await Interactive(env, new Ui(), f);
        Assert.Equal(PlanStatus.Changed, r.Status);
        Assert.Null(r.Plan);
        var env2 = new Env { ChangeAfterReads = 1 };
        Add(env2, "c.ps1", "'x'\n");
        var s = await RunPlanner.PlanScheduledAsync(env2, "c.ps1", "", false);
        Assert.Equal(PlanStatus.Blocked, s.Status);
        Assert.Equal("schedBlockConfirm", s.Block);
        // The other kinds too
        foreach (var rel in new[] { "c.py", "c.bat" })
        {
            var e = new Env { ChangeAfterReads = 1 };
            var g = Add(e, rel, "x\n");
            Assert.Equal(PlanStatus.Changed, (await Interactive(e, new Ui(), g)).Status);
        }
    }

    [Fact]
    public async Task The_checked_content_is_the_one_read_now_not_the_scanned_one()
    {
        var env = new Env();
        var f = Add(env, "n.ps1", "'x'\n");
        var stale = f.WithHash(new string('1', 64));
        var r = await Interactive(env, new Ui(), stale);
        Assert.Equal(PlanStatus.Ready, r.Status);
        Assert.Equal(f.Hash, r.Plan!.File.Hash);
    }

    // ---------- Safety ----------

    [Fact]
    public async Task Findings_ask_before_an_interactive_run_and_block_a_scheduled_one()
    {
        var env = new Env();
        var f = Add(env, "d.ps1", "'x'\n");
        env.MetaOf = _ => new ScriptMeta { Danger = { Finding("medium") } };
        var ui = new Ui { Safe = false };
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Contains("safety:inline", ui.Calls);
        ui = new Ui();
        Assert.Equal(PlanStatus.Ready, (await Interactive(env, ui, f)).Status);
        var s = await RunPlanner.PlanScheduledAsync(env, "d.ps1", "", false);
        Assert.Equal("schedBlockConfirm", s.Block);
        // Trusted for this exact content: runs on schedule too, and the interactive run does not ask
        Safety.AddTrust(env.Settings, "d.ps1", Safety.TrustKeyOf("d.ps1", f.Hash));
        Assert.Equal(PlanStatus.Ready, (await RunPlanner.PlanScheduledAsync(env, "d.ps1", "", false)).Status);
        ui = new Ui();
        Assert.Equal(PlanStatus.Ready, (await Interactive(env, ui, f)).Status);
        Assert.Empty(ui.Calls);
        // Red findings always ask, trusted or not
        env.MetaOf = _ => new ScriptMeta { Danger = { Finding("high") } };
        ui = new Ui();
        await Interactive(env, ui, f);
        Assert.Contains("safety:inline", ui.Calls);
    }

    [Fact]
    public async Task An_unverified_script_asks_interactively_and_is_skipped_on_schedule()
    {
        var env = new Env();
        var f = Add(env, "u.ps1", "'x'\n");
        env.MetaOf = _ => new ScriptMeta { Scan = "unverified" };
        var ui = new Ui { Unverified = false };
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Contains(Loc.F("cxSafety", "u"), ui.Statuses);
        Assert.Equal("schedBlockUnverified", (await RunPlanner.PlanScheduledAsync(env, "u.ps1", "", false)).Block);
        env.Settings.SafetyWarnings = false;
        ui = new Ui { Unverified = false };
        Assert.Equal(PlanStatus.Ready, (await Interactive(env, ui, f)).Status);
        Assert.Equal(PlanStatus.Ready, (await RunPlanner.PlanScheduledAsync(env, "u.ps1", "", false)).Status);
    }

    [Fact]
    public async Task Mark_of_the_web_stops_every_run()
    {
        var env = new Env();
        var f = Add(env, "z.ps1", "'x'\n");
        env.MetaOf = _ => new ScriptMeta { Zone = new ZoneInfo { ZoneId = 3 } };
        var ui = new Ui();
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Equal(new[] { "motw" }, ui.Calls);
        Assert.Equal("schedBlockMotw", (await RunPlanner.PlanScheduledAsync(env, "z.ps1", "", false)).Block);
        // A marked script it loads counts too; intranet zones do not
        env.MetaOf = _ => new ScriptMeta { Zone = new ZoneInfo { ZoneId = 1 } };
        Assert.Equal(PlanStatus.Ready, (await Interactive(env, new Ui(), f)).Status);
        env.Marked.Add((Path.Combine(dir, "lib.ps1"), new ZoneInfo { ZoneId = 3 }));
        Assert.Equal("schedBlockMotw", (await RunPlanner.PlanScheduledAsync(env, "z.ps1", "", false)).Block);
        Assert.NotNull(RunPlanner.MotwBlock(env, f, env.Meta(f)));
    }

    // ---------- Administrator runs ----------

    [Fact]
    public async Task Administrator_run_always_confirms_and_checks_the_hash_after_uac()
    {
        var env = new Env();
        var f = Add(env, "adm.ps1", "'x'\n");
        var ui = new Ui();
        var r = await Interactive(env, ui, f, new RunOptions { Mode = "admin" });
        Assert.Equal(new[] { "safety:admin" }, ui.Calls);
        var p = r.Plan!;
        Assert.True(p.Admin);
        Assert.Equal(Ps51, p.Exe);
        string w = p.WindowArgs(Done);
        Assert.Contains("Get-FileHash", w);
        Assert.Contains(f.Hash, w);
        Assert.Equal(RunArgs.WindowArgs(f.Path, "", f.Hash, Done), w);
        // A plain window run has no hash check
        Assert.DoesNotContain("Get-FileHash", (await Interactive(env, new Ui(), f, new RunOptions { Mode = "window" })).Plan!.WindowArgs(Done));
        ui = new Ui { Safe = false };
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f, new RunOptions { Mode = "admin" })).Status);
    }

    [Fact]
    public async Task Administrator_scripts_are_never_scheduled()
    {
        var env = new Env();
        Add(env, "h.ps1", "# Shelly-Mode: admin\n'x'\n");
        Assert.Equal("schedBlockAdmin", (await RunPlanner.PlanScheduledAsync(env, "h.ps1", "", true)).Block);
        Add(env, "r.ps1", "#Requires -RunAsAdministrator\n'x'\n");
        env.MetaOf = f => new ScriptMeta { Req = new RequiresInfo { Elevation = f.Rel == "r.ps1" } };
        Assert.Equal("schedBlockAdmin", (await RunPlanner.PlanScheduledAsync(env, "r.ps1", "", false)).Block);
        Add(env, "m.ps1", "'x'\n");
        RunPreset.Set(env.Settings, "m.ps1", "admin", new Dictionary<string, object>());
        Assert.Equal("schedBlockAdmin", (await RunPlanner.PlanScheduledAsync(env, "m.ps1", "", false)).Block);
        Add(env, "a.py", "# Shelly-Mode: admin\nprint(1)\n");
        Assert.Equal("schedBlockAdmin", (await RunPlanner.PlanScheduledAsync(env, "a.py", "", false)).Block);
        // A scheduled plan is never an administrator run
        Add(env, "ok.ps1", "'x'\n");
        var s = await RunPlanner.PlanScheduledAsync(env, "ok.ps1", "", true);
        Assert.Equal("window", s.Plan!.Mode);
        Assert.False(s.Plan.Admin);
        Assert.DoesNotContain("Get-FileHash", s.Plan.WindowArgs(Done));
        Assert.Equal("schedule", s.Plan.Origin);
    }

    [Fact]
    public async Task Compatibility_question_can_switch_to_administrator_or_another_engine()
    {
        var env = new Env();
        var f = Add(env, "k.ps1", "'x'\n");
        env.MetaOf = _ => new ScriptMeta { Issues = { new ScriptIssue { Kind = "admin" }, new ScriptIssue { Kind = "core", Engine = "5.1" } } };
        var ui = new Ui { Compat = "admin" };
        var r = await Interactive(env, ui, f);
        Assert.Equal("admin", r.Plan!.Mode);
        Assert.Equal(new[] { "compat", "safety:admin" }, ui.Calls);
        Assert.Equal(new[] { "admin", "pwsh", "anyway", "cancel" }, ui.Choices!.Select(c => c.Id));
        Assert.True(ui.Choices![0].Default);
        ui = new Ui { Compat = "pwsh" };
        r = await Interactive(env, ui, f);
        Assert.Equal((Pwsh, "7", "inline"), (r.Plan!.Exe, r.Plan.Engine, r.Plan.Mode));
        ui = new Ui { Compat = "cancel" };
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Contains(Loc.F("cxCompat", "k"), ui.Statuses);
        // Already an administrator run: that issue is not asked about
        ui = new Ui { Compat = "anyway" };
        await Interactive(env, ui, f, new RunOptions { Mode = "admin" });
        Assert.DoesNotContain(ui.Choices!, c => c.Id == "admin");
        // PowerShell 7 is needed and missing: the installer
        env.PwshPath = null;
        env.MetaOf = _ => new ScriptMeta { Engine = new EngineInfo { Engine = "7" }, Issues = { new ScriptIssue { Kind = "need7" } } };
        ui = new Ui { Compat = "install7" };
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Contains("install7", ui.Calls);
    }

    // ---------- Python ----------

    [Fact]
    public async Task Python_runs_inline_with_the_interpreter_and_in_a_window_through_powershell()
    {
        var env = new Env();
        var f = Add(env, "t.py", "print(1)\n");
        RunPreset.Set(env.Settings, "t.py", "inline", new Dictionary<string, object> { [PythonRun.ArgsKey] = "--x 1" });
        var r = await Interactive(env, new Ui(), f);
        var p = r.Plan!;
        Assert.Equal((@"C:\Py\python.exe", EngineInfo.Python, "inline", " --x 1"), (p.Exe, p.Engine, p.Mode, p.ArgText));
        Assert.Equal(PythonRun.InlineArgs(env.Python!, f.Path, "--x 1"), p.InlineArgs);
        Assert.Contains(p.Env!, e => e.Key == "PYTHONUTF8");
        r = await Interactive(env, new Ui(), f, new RunOptions { Mode = "window" });
        Assert.Equal(Ps51, r.Plan!.Exe);   // the window host
        Assert.Equal(PythonRun.WindowArgs(env.Python!, f.Path, "--x 1", null, Done), r.Plan.WindowArgs(Done));
        env.Ps51Path = null;
        r = await Interactive(env, new Ui(), f, new RunOptions { Mode = "admin" });
        Assert.Equal(Pwsh, r.Plan!.Exe);
        Assert.Contains(f.Hash, r.Plan.WindowArgs(Done));
        var s = await RunPlanner.PlanScheduledAsync(env, "t.py", "", false);
        Assert.Equal(@"C:\Py\python.exe", s.Plan!.Exe);
        Assert.Equal(" --x 1", s.Plan.ArgText);
    }

    [Fact]
    public async Task Python_with_a_broken_venv_or_no_interpreter_does_not_run()
    {
        var env = new Env { Venv = new PythonVenv(@"C:\S\.venv", @"C:\S\.venv\Scripts\python.exe", "", true) };
        var f = Add(env, "v.py", "print(1)\n");
        var ui = new Ui();
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Equal(new[] { "venv" }, ui.Calls);
        Assert.Equal("schedBlockPython", (await RunPlanner.PlanScheduledAsync(env, "v.py", "", false)).Block);
        env.Venv = null;
        env.Python = null;
        ui = new Ui();
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Equal(new[] { "python" }, ui.Calls);
        Assert.Contains(Loc.F("pyMissingStatus", "v"), ui.Statuses);
        Assert.Equal("schedBlockPython", (await RunPlanner.PlanScheduledAsync(env, "v.py", "", false)).Block);
        // A good venv wins over the interpreter of Settings
        env.Venv = new PythonVenv(@"C:\S\.venv", @"C:\S\.venv\Scripts\python.exe", "3.12.4", false);
        var r = await Interactive(env, new Ui(), f);
        Assert.Equal(@"C:\S\.venv\Scripts\python.exe", r.Plan!.Exe);
        Assert.Contains(env.Logs, l => l.StartsWith("venv for v.py", StringComparison.Ordinal));
    }

    // ---------- Batch files and custom interpreters ----------

    [Fact]
    public async Task Batch_files_run_with_cmd_and_their_window_through_powershell()
    {
        var env = new Env();
        var f = Add(env, "job.bat", "@echo off\necho hi\n");
        var r = await Interactive(env, new Ui(), f);
        var p = r.Plan!;
        Assert.Equal(EngineInfo.Batch, p.Engine);
        Assert.Equal(BatchRun.ComSpec(), p.Exe);
        Assert.NotNull(p.Mixed);
        Assert.Equal(dir, p.WorkDir);
        var s = await RunPlanner.PlanScheduledAsync(env, "job.bat", "", true);
        Assert.Equal(Ps51, s.Plan!.Exe);
        Assert.Contains("job.bat", s.Plan.WindowArgs(Done));
        Assert.DoesNotContain("Get-FileHash", s.Plan.WindowArgs(Done));
        r = await Interactive(env, new Ui(), f, new RunOptions { Mode = "admin" });
        Assert.Contains(f.Hash, r.Plan!.WindowArgs(Done));
        // Arguments cmd.exe can't pass on: not started
        var ui = new Ui();
        r = await Interactive(env, ui, f, new RunOptions { RawArgs = "50%" });
        Assert.Equal(PlanStatus.Cancelled, r.Status);
        Assert.Equal("schedBlockArgs", (await RunPlanner.PlanScheduledAsync(Presetted(env, "job.bat", "50%"), "job.bat", "", false)).Block);
    }

    static Env Presetted(Env env, string rel, string args)
    {
        RunPreset.Set(env.Settings, rel, "inline", new Dictionary<string, object> { [PythonRun.ArgsKey] = args });
        return env;
    }

    [Fact]
    public async Task Custom_interpreter_scripts_need_their_program()
    {
        string node = Path.Combine(dir, "node.exe");
        File.WriteAllText(node, "");
        ScriptKinds.SetCustom(new[] { new CustomInterpreter { Id = "00000001", Name = "Node", Extensions = new() { ".js" }, Program = node } });
        var env = new Env();
        var f = Add(env, "app.js", "console.log(1)\n");
        var r = await Interactive(env, new Ui(), f);
        Assert.Equal((EngineInfo.Custom, node), (r.Plan!.Engine, r.Plan.Exe));
        Assert.Contains("app.js", r.Plan.InlineArgs);
        Assert.Equal(PlanStatus.Ready, (await RunPlanner.PlanScheduledAsync(env, "app.js", "", false)).Status);
        File.Delete(node);
        var ui = new Ui();
        Assert.Equal(PlanStatus.Cancelled, (await Interactive(env, ui, f)).Status);
        Assert.Equal(new[] { "custom" }, ui.Calls);
        var s = await RunPlanner.PlanScheduledAsync(env, "app.js", "", false);
        Assert.Equal(("customNoProgram", node), (s.Block, s.Arg));
        Assert.Equal(Loc.F("customNoProgram", "app", node), s.Reason);
    }

    // ---------- Scheduled runs: the rest of the gate ----------

    [Fact]
    public async Task Scheduled_run_needs_the_script_and_the_accepted_disclaimer()
    {
        var env = new Env();
        var s = await RunPlanner.PlanScheduledAsync(env, "gone.ps1", "", false);
        Assert.Equal(("schedBlockScript", "gone"), (s.Block, s.Name));
        Assert.Equal(Loc.F("schedBlockScript", "gone", ""), s.Reason);
        Add(env, "x.ps1", "'x'\n");
        env.Settings.DisclaimerAccepted = "";
        Assert.Equal("schedBlockDisclaimer", (await RunPlanner.PlanScheduledAsync(env, "x.ps1", "", false)).Block);
        env.Settings.DisclaimerAccepted = "1.3.1";
        s = await RunPlanner.PlanScheduledAsync(env, "x.ps1", "", false);
        Assert.Equal(PlanStatus.Ready, s.Status);
        Assert.Equal("inline", s.Plan!.Mode);
        Assert.Equal(Ps51, s.Plan.Exe);
        Assert.Null(s.Plan.Preset);
    }

    // ---------- 1.3.1: the 1.1 key toastOnFinish ----------

    [Theory]
    [InlineData("{\"toastOnFinish\": false}", "off")]
    [InlineData("{\"toastOnFinish\": true}", "always")]
    [InlineData("{}", "always")]
    [InlineData("{\"toastOnFinish\": false, \"notify\": \"errors\"}", "errors")]
    [InlineData("{\"notify\": \"off\", \"toastOnFinish\": true}", "off")]
    public void Old_toast_setting_becomes_notify(string json, string expected)
    {
        var s = new Settings();
        s.LoadJson(json);
        Assert.Equal(expected, s.Notify);
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("errors", true)]
    [InlineData("always", true)]
    public void Toast_key_is_still_written_from_notify_for_older_versions(string notify, bool toast)
    {
        string f = Path.Combine(dir, "s.json");
        var s = new Settings { Notify = notify };
        s.Save(f);
        using var doc = JsonDocument.Parse(File.ReadAllText(f).TrimStart('\uFEFF'));
        Assert.Equal(toast, doc.RootElement.GetProperty("toastOnFinish").GetBoolean());
        Assert.Equal(notify, Settings.Load(f).Notify);
    }
}
