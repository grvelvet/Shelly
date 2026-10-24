using System.Text;

namespace Shelly.Core;

/// <summary>How a run was asked for (moved to Shelly.Core in 1.3.1, with the planner).</summary>
public sealed class RunOptions
{
    /// <summary>inline | window | admin; null = the remembered mode or header, else the default mode of Settings (RunModes.Resolve).</summary>
    public string? Mode;
    /// <summary>Run with this parameter preset (1.2.0): its values instead of the remembered ones.</summary>
    public string? Preset;
    /// <summary>Parameter values from the Run settings dialog.</summary>
    public IDictionary<string, object>? Values;
    /// <summary>Ready argument text (re-run from the history): no dialog.</summary>
    public string? RawArgs;
    /// <summary>Engine of the earlier run (re-run).</summary>
    public string? Engine;
    public string Origin = RunOrigins.List;
    /// <summary>Open the Run settings dialog first.</summary>
    public bool Ask;
}

/// <summary>Where a run was started from (RunEntry.Origin).</summary>
public static class RunOrigins
{
    public const string List = "list", JumpList = "jumplist", Editor = "editor", Schedule = "schedule";
}

/// <summary>What the Run settings dialog answered.</summary>
public sealed record RunAnswer(string Mode, IDictionary<string, object> Values, string? Preset);

/// <summary>A button of the compatibility question.</summary>
public sealed record RunChoice(string Id, string Label, bool Default = false);

/// <summary>What the planner reads: the settings, the scripts and their analysis, the engines and interpreters. AppState
/// in the app, a fake in the tests. The default members are the real file system.</summary>
public interface IRunEnvironment
{
    Settings Settings { get; }
    ScriptFile? Find(string rel);
    ScriptMeta Meta(ScriptFile f);
    bool PwshOk { get; }
    bool Ps51Ok { get; }
    string? PwshPath { get; }
    string? Ps51Path { get; }
    AnalysisService.Engines Engines { get; }
    /// <summary>Until PowerShell's parser has reported on the script, or the timeout.</summary>
    Task WaitForAnalysisAsync(ScriptFile f, TimeSpan timeout);
    /// <summary>The script's virtual environment, looked up now.</summary>
    PythonVenv? VenvFor(ScriptFile f);
    /// <summary>The Python lookup has not finished yet (or never ran): EnsurePythonAsync may take a moment.</summary>
    bool PythonLookupPending { get; }
    /// <summary>Why no Python was found ("pyBadPath": the path in Settings is wrong).</summary>
    string PythonError { get; }
    Task<PythonInfo?> EnsurePythonAsync();
    /// <summary>The script already runs inline (one inline run per script).</summary>
    bool InlineRunning(string rel);
    /// <summary>A line in Shelly's log.</summary>
    void Log(string text);

    string FileSha256(string path) => Safety.FileSha256(path);
    bool FileExists(string path) => File.Exists(path);
    /// <summary>Local scripts with a blocking Mark of the Web that a PowerShell script loads.</summary>
    List<(string Path, ZoneInfo Zone)> MarkedReferences(string path) => MotW.MarkedReferences(path, File.ReadAllText(path));
    OtherRunPlan? PlanOther(ScriptFile f, string userArgs, out string? error, out string detail) => OtherRun.Plan(f, userArgs, out error, out detail);
    string? PathVariable => Environment.GetEnvironmentVariable("PATH");
    /// <summary>The PowerShell that hosts the console window of Python, batch and custom scripts.</summary>
    string WindowHostExe => Ps51Path ?? PwshPath ?? "powershell.exe";
    string Now => DateTime.Now.ToString("HH:mm:ss");
}

/// <summary>The questions of an interactive run (dialogs in the app, answers scripted in the tests). Scheduled runs ask nothing.</summary>
public interface IRunPrompts
{
    void Status(string text, bool important = false);
    /// <summary>The Run settings dialog; null = cancelled.</summary>
    RunAnswer? AskRunSettings(ScriptFile f);
    /// <summary>PowerShell's parser did not check the script. true = run anyway.</summary>
    bool ConfirmUnverified(ScriptFile f, bool engineAvailable);
    /// <summary>Asked only when Safety.Needed: findings to confirm, or an administrator run. true = go ahead.</summary>
    bool ConfirmSafety(ScriptFile f, ScriptMeta meta, string action);
    /// <summary>The script (or a script it loads) has a blocking Mark of the Web; the run does not start either way.</summary>
    void MotwBlocked(ScriptFile f, ScriptMeta meta, List<(string Path, ZoneInfo Zone)> others);
    /// <summary>The compatibility question; the id of the chosen button (null = closed).</summary>
    string? ChooseCompat(ScriptFile f, string text, bool warn, IReadOnlyList<RunChoice> choices);
    void InstallPwsh();
    void VenvBroken(ScriptFile f, PythonVenv venv);
    void PythonMissing(ScriptFile f, string text);
    void CustomMissing(ScriptFile f, string text);
}

/// <summary>A run that passed every check: what to start and how.</summary>
public sealed class RunPlan
{
    public required ScriptFile File { get; init; }
    /// <summary>inline | window | admin (admin only for interactive runs).</summary>
    public required string Mode { get; init; }
    public required string ArgText { get; init; }
    /// <summary>The program of an inline run, or of the window (an engine for PowerShell scripts, else the window host).</summary>
    public required string Exe { get; init; }
    public required string Engine { get; init; }
    public required string InlineArgs { get; init; }
    /// <summary>The window command line for an end marker file.</summary>
    public required Func<string, string> WindowArgs { get; init; }
    /// <summary>The window command line for an end marker file and optional transcript log file (1.4.0).</summary>
    public Func<string, string?, string>? WindowArgsWithLog { get; init; }
    public string GetWindowArgs(string doneFile, string? logFile = null) =>
        logFile != null && WindowArgsWithLog != null ? WindowArgsWithLog(doneFile, logFile) : WindowArgs(doneFile);
    public IEnumerable<(string Key, string? Value)>? Env { get; init; }
    /// <summary>null = the script's folder.</summary>
    public string? WorkDir { get; init; }
    /// <summary>Batch files and custom interpreters: output decoded line by line, UTF-8 or this code page (InlineRunner.MixedFallback).</summary>
    public Encoding? Mixed { get; init; }
    public required string Header { get; init; }
    /// <summary>The preset the values came from; null = none.</summary>
    public string? Preset { get; init; }
    /// <summary>Preset values of parameters the script no longer has (scheduled runs log them).</summary>
    public List<string> Ignored { get; init; } = new();
    public required string Origin { get; init; }
    public bool Admin => Mode == RunModes.Admin;
}

public enum PlanStatus { Ready, Cancelled, Changed, Blocked }

/// <summary>Ready: Plan. Cancelled: the user said no (the prompts told why). Changed: the file changed after the checks
/// (an interactive run checks it again). Blocked: a scheduled run is skipped, Block is the Loc key of the reason
/// ({0} = Name, {1} = Arg).</summary>
public sealed record PlanResult(PlanStatus Status, RunPlan? Plan, string? Block, string Arg, string Name, string Rel)
{
    public static PlanResult Ready(RunPlan p) => new(PlanStatus.Ready, p, null, "", p.File.Name, p.File.Rel);
    public static PlanResult Cancelled(ScriptFile f) => new(PlanStatus.Cancelled, null, null, "", f.Name, f.Rel);
    public static PlanResult Changed(ScriptFile f) => new(PlanStatus.Changed, null, null, "", f.Name, f.Rel);
    public static PlanResult Blocked(string block, string arg, string name, string rel) => new(PlanStatus.Blocked, null, block, arg, name, rel);
    /// <summary>The reason a scheduled run was skipped, as the user reads it.</summary>
    public string Reason => Loc.F(Block ?? "schedBlockConfirm", Name, Arg);
}

/// <summary>Every check between "run this script" and the process start (1.3.1; before, RunManager and Unattended each had
/// their own copy). Interactive runs ask through IRunPrompts; scheduled runs ask nothing and are skipped wherever an
/// interactive run would ask (administrator rights, Mark of the Web, findings not trusted for this exact content, an
/// unverified script, missing parameters). Both end with the hash check: what was checked is what starts.</summary>
public static class RunPlanner
{
    /// <summary>How long a run waits for PowerShell's analysis of a script that is not analyzed yet (1.3.1: 60 s everywhere;
    /// interactive runs and builds waited 30 s, scheduled runs 60 s). A timeout means "unverified": one more question for
    /// an interactive run, a skipped scheduled run. A cold PowerShell 7 start on a slow machine can take longer than 30 s.</summary>
    public static readonly TimeSpan AnalysisWait = TimeSpan.FromSeconds(60);

    /// <summary>The checked content: the file's hash read now (not the one from the last scan).</summary>
    static ScriptFile Rehash(IRunEnvironment env, ScriptFile f)
    {
        string now = env.FileSha256(f.Path);
        return now.Length > 0 && now != f.Hash ? f.WithHash(now) : f;
    }

    static async Task<ScriptMeta> AnalyzedMeta(IRunEnvironment env, IRunPrompts? ui, ScriptFile f)
    {
        var meta = env.Meta(f);
        if (!meta.Pending || !(env.PwshOk || env.Ps51Ok)) return meta;
        ui?.Status(Loc.F("analyzeWait", f.Name));
        await env.WaitForAnalysisAsync(f, AnalysisWait);
        ui?.Status("");
        return env.Meta(f);
    }

    /// <summary>Mark of the Web on the script, or on a local script a PowerShell script loads (a Python script is checked on
    /// its own). null = not blocked; else the marked scripts it loads (empty when only the script itself is marked).</summary>
    public static List<(string Path, ZoneInfo Zone)>? MotwBlock(IRunEnvironment env, ScriptFile f, ScriptMeta meta)
    {
        List<(string Path, ZoneInfo Zone)> others;
        try { others = !f.IsPowerShell ? new() : env.MarkedReferences(f.Path); }
        catch (Exception ex) { env.Log("Mark of the Web of the scripts " + f.Rel + " loads not read: " + ex.Message); others = new(); }
        return meta.Zone is { Blocks: true } || others.Count > 0 ? others : null;
    }

    static bool Safe(IRunEnvironment env, IRunPrompts ui, ScriptFile f, ScriptMeta meta, string action) =>
        !Safety.Needed(env.Settings, meta.Danger, action, f.Rel, f.Path, f.Hash) || ui.ConfirmSafety(f, meta, action);

    // ---------- Interactive ----------

    /// <summary>An interactive run of f (already the fresh entry of the list). o may be changed (preset values, the mode of
    /// the Run settings dialog), as before 1.3.1: a re-check after a Changed result starts from it.</summary>
    public static async Task<PlanResult> PlanInteractiveAsync(IRunEnvironment env, IRunPrompts ui, ScriptFile f, RunOptions o)
    {
        f = Rehash(env, f);
        var meta = await AnalyzedMeta(env, ui, f);
        // Downloaded from the internet (Mark of the Web): no run from any entry point until the user unblocks it
        if (MotwBlock(env, f, meta) is { } others) { ui.MotwBlocked(f, meta, others); return PlanResult.Cancelled(f); }
        if (!f.IsPowerShell) return await PlanFreeArgsAsync(env, ui, f, o, meta);
        // Arguments kept in the history are plain text anyone can edit: only the shape RunArgs.Format writes goes into the command
        // line; anything else is dropped and the Run settings dialog opens instead
        if (o.RawArgs != null && !RunArgs.IsValid(o.RawArgs))
        {
            env.Log("History arguments rejected for " + f.Rel + ": not in the form Shelly writes");
            ui.Status(Loc.F("histArgsInvalid", f.Name), true);
            o = new RunOptions { Mode = o.Mode, Engine = o.Engine, Origin = o.Origin, Ask = true };
        }
        if (o.Ask)
        {
            var rs = ui.AskRunSettings(f);
            if (rs == null) { ui.Status(Loc.F("cxParams", f.Name)); return PlanResult.Cancelled(f); }
            o = new RunOptions { Mode = rs.Mode, Values = rs.Values, Origin = o.Origin, Preset = rs.Preset };
            meta = env.Meta(f);
        }
        else if (o.Preset != null && o.Values == null && o.RawArgs == null && !ApplyPreset(env, ui, f, meta, o)) return PlanResult.Cancelled(f);
        string mode;
        string argText;
        if (o.RawArgs != null)
        {
            argText = o.RawArgs;
            mode = o.Mode ?? RunModes.Inline;
        }
        else
        {
            var preset = RunPreset.Get(env.Settings, f.Rel);
            var values = o.Values ?? preset?.Values;
            if (RunArgs.MissingMandatory(meta.Params, values).Count > 0)
            {
                ui.Status(Loc.F("rsMissing", f.Name));
                var rs = ui.AskRunSettings(f);
                if (rs == null) { ui.Status(Loc.F("cxParams", f.Name)); return PlanResult.Cancelled(f); }
                values = rs.Values;
                o.Mode = rs.Mode;
                meta = env.Meta(f);
            }
            mode = RunModes.Resolve(o.Mode, o.Origin, preset?.Mode, ScriptHeader.ModeOf(f), env.Settings.DefaultMode);
            argText = RunArgs.Format(meta.Params, values);
        }
        if (mode == RunModes.Inline && env.InlineRunning(f.Rel)) { ui.Status(Loc.F("busySame", f.Name), true); return PlanResult.Cancelled(f); }

        // PowerShell's parser did not check the script (no engine, or it failed): never "safe" without asking
        if (!meta.Verified && env.Settings.SafetyWarnings && Safety.Blocking(meta.Danger).Count == 0 && !ui.ConfirmUnverified(f, env.PwshOk || env.Ps51Ok))
        {
            ui.Status(Loc.F("cxSafety", f.Name));
            return PlanResult.Cancelled(f);
        }
        // Safety first (dangerous commands, administrator runs), then compatibility
        if (!Safe(env, ui, f, meta, mode)) return PlanResult.Cancelled(f);
        var rm = ResolveRunMode(env, ui, f, meta, mode, o.Engine);
        if (rm == null) { ui.Status(Loc.F("cxCompat", f.Name)); return PlanResult.Cancelled(f); }
        // Last check before the process starts: the file must still be the one that was checked (an administrator run
        // checks it once more inside the elevated PowerShell, after the UAC prompt)
        if (env.FileSha256(f.Path) != f.Hash) return PlanResult.Changed(f);
        var (m, exe, engine) = rm.Value;
        string hash = f.Hash, path = f.Path;
        bool admin = m == RunModes.Admin;
        return PlanResult.Ready(new RunPlan
        {
            File = f, Mode = m, ArgText = argText, Exe = exe, Engine = engine, Origin = o.Origin, Preset = o.Preset,
            InlineArgs = RunArgs.InlineArgs(path, argText),
            WindowArgs = done => RunArgs.WindowArgs(path, argText, admin ? hash : null, done),
            WindowArgsWithLog = (done, log) => RunArgs.WindowArgs(path, argText, admin ? hash : null, done, log),
            Header = Loc.F("runHeader", f.Name, argText, EngineInfo.Short(engine), env.Now)
        });
    }

    /// <summary>"Run with preset": the preset's values for the parameters the script has now; values of parameters that are
    /// gone are skipped with a note. false = the preset is not there.</summary>
    static bool ApplyPreset(IRunEnvironment env, IRunPrompts ui, ScriptFile f, ScriptMeta meta, RunOptions o)
    {
        var p = Presets.Get(env.Settings, f.Rel, o.Preset!);
        if (p == null) { ui.Status(Loc.F("presetMissing", o.Preset, f.Name), true); return false; }
        var (values, ignored) = Presets.Apply(meta.Params, f.UsesArgs, p.Values);
        if (ignored.Count > 0)
        {
            ui.Status(Loc.F("presetIgnored", p.Name, string.Join(", ", ignored)), true);
            env.Log("Preset " + p.Name + " of " + f.Rel + ": skipped " + string.Join(", ", ignored));
        }
        o.Values = values;
        o.Preset = p.Name;
        return true;
    }

    /// <summary>A .py, .bat/.cmd or custom interpreter's script: the free arguments (Run settings or the history), the
    /// interpreter, the safety and administrator confirmation, the hash check, then the same inline or window run as for
    /// PowerShell. PowerShell's analysis, parameters and engine choice do not apply.</summary>
    static async Task<PlanResult> PlanFreeArgsAsync(IRunEnvironment env, IRunPrompts ui, ScriptFile f, RunOptions o, ScriptMeta meta)
    {
        if (o.RawArgs != null && !PythonRun.IsValidArgs(o.RawArgs))
        {
            env.Log("History arguments rejected for " + f.Rel + ": not one line of text");
            ui.Status(Loc.F("histArgsInvalid", f.Name), true);
            o = new RunOptions { Mode = o.Mode, Origin = o.Origin, Ask = true };
        }
        if (o.Ask)
        {
            var rs = ui.AskRunSettings(f);
            if (rs == null) { ui.Status(Loc.F("cxParams", f.Name)); return PlanResult.Cancelled(f); }
            o = new RunOptions { Mode = rs.Mode, Values = rs.Values, Origin = o.Origin, Preset = rs.Preset };
        }
        else if (o.Preset != null && o.Values == null && o.RawArgs == null && !ApplyPreset(env, ui, f, meta, o)) return PlanResult.Cancelled(f);
        string userArgs, mode;
        if (o.RawArgs != null)
        {
            userArgs = o.RawArgs.Trim();
            mode = o.Mode ?? RunModes.Inline;
        }
        else
        {
            var preset = RunPreset.Get(env.Settings, f.Rel);
            userArgs = PythonRun.ArgsFrom(o.Values ?? preset?.Values);
            mode = RunModes.Resolve(o.Mode, o.Origin, preset?.Mode, ScriptHeader.ModeOf(f), env.Settings.DefaultMode);
        }
        if (!PythonRun.IsValidArgs(userArgs)) { ui.Status(Loc.F("rsPyArgsBad", PythonRun.MaxArgsLength), true); return PlanResult.Cancelled(f); }
        if (mode == RunModes.Inline && env.InlineRunning(f.Rel)) { ui.Status(Loc.F("busySame", f.Name), true); return PlanResult.Cancelled(f); }
        string argText = PythonRun.Display(userArgs);
        bool admin = mode == RunModes.Admin;
        if (!f.IsPython)
        {
            var plan = env.PlanOther(f, userArgs, out var err, out var detail);
            if (plan == null)
            {
                string text = Loc.F(err!, f.Name, detail);
                bool custom = err is "customGone" or "customNoProgram";
                ui.Status(custom ? text : f.Name + ": " + Loc.F(err!, detail), true);
                if (custom) ui.CustomMissing(f, text);
                return PlanResult.Cancelled(f);
            }
            if (!Safe(env, ui, f, meta, mode)) return PlanResult.Cancelled(f);
            if (env.FileSha256(f.Path) != f.Hash) return PlanResult.Changed(f);
            return PlanResult.Ready(OtherPlan(env, f, plan, mode, admin ? f.Hash : null, argText, o.Origin, o.Preset, new()));
        }

        // Interpreter: the script's virtual environment (.venv / venv / env next to it or above, up to the folder root),
        // else the path from Settings, else auto-detect. A broken venv is an error, not a silent switch to another Python.
        PythonInfo? py;
        var venv = env.VenvFor(f);
        if (venv is { Broken: true })
        {
            ui.Status(Loc.F("pyVenvBrokenStatus", f.Name), true);
            ui.VenvBroken(f, venv);
            return PlanResult.Cancelled(f);
        }
        if (venv != null) py = PythonInfo.FromVenv(venv);
        else
        {
            bool searching = env.PythonLookupPending;
            if (searching) ui.Status(Loc.T("pyChecking"));
            py = await env.EnsurePythonAsync();
            if (searching) ui.Status("");
        }
        if (py == null)
        {
            ui.Status(Loc.F("pyMissingStatus", f.Name), true);
            ui.PythonMissing(f, env.PythonError == "pyBadPath" ? Loc.F("pyBadPathText", f.Name, env.Settings.PythonPath) : Loc.F("pyMissingText", f.Name));
            return PlanResult.Cancelled(f);
        }
        if (venv != null) env.Log("venv for " + f.Rel + ": " + venv.Python);
        // Administrator runs always ask (the PowerShell danger scan does not read Python, so there are no findings to show)
        if (!Safe(env, ui, f, meta, mode)) return PlanResult.Cancelled(f);
        if (env.FileSha256(f.Path) != f.Hash) return PlanResult.Changed(f);
        return PlanResult.Ready(PythonPlan(env, f, py, mode, admin ? f.Hash : null, userArgs, o.Origin, o.Preset, new()));
    }

    /// <summary>A Python run: inline with the interpreter, or its own console window through PowerShell (it keeps the window
    /// open and, for administrator runs, checks the hash after UAC).</summary>
    static RunPlan PythonPlan(IRunEnvironment env, ScriptFile f, PythonInfo py, string mode, string? hash, string userArgs, string origin, string? preset, List<string> ignored)
    {
        string argText = PythonRun.Display(userArgs);
        string path = f.Path;
        return new RunPlan
        {
            File = f, Mode = mode, ArgText = argText, Engine = EngineInfo.Python, Origin = origin, Preset = preset, Ignored = ignored,
            Exe = mode == RunModes.Inline ? py.Path : env.WindowHostExe,
            InlineArgs = PythonRun.InlineArgs(py, path, userArgs),
            WindowArgs = done => PythonRun.WindowArgs(py, path, userArgs, hash, done),
            WindowArgsWithLog = (done, log) => PythonRun.WindowArgs(py, path, userArgs, hash, done, log),
            Env = PythonRun.EnvFor(py, env.PathVariable),
            Header = Loc.F("runHeaderPy", f.Name, argText, py.RunLabel, env.Now)
        };
    }

    /// <summary>A batch file (cmd.exe) or a custom interpreter's script; a window run goes through PowerShell as for Python.</summary>
    static RunPlan OtherPlan(IRunEnvironment env, ScriptFile f, OtherRunPlan plan, string mode, string? hash, string argText, string origin, string? preset, List<string> ignored) => new()
    {
        File = f, Mode = mode, ArgText = argText, Engine = plan.Engine, Origin = origin, Preset = preset, Ignored = ignored,
        Exe = mode == RunModes.Inline ? plan.Exe : env.WindowHostExe,
        InlineArgs = plan.InlineArgs,
        WindowArgs = done => plan.Window(hash, done),
        WindowArgsWithLog = (done, log) => plan.WindowWithLog != null ? plan.WindowWithLog(hash, done, log) : plan.Window(hash, done),
        WorkDir = plan.WorkDir, Mixed = ConsoleText.Oem(),
        Header = Loc.F("runHeaderPy", f.Name, argText, plan.Label, env.Now)
    };

    /// <summary>The engine of a PowerShell run and the compatibility question (switch the engine, run as administrator,
    /// install PowerShell 7). null = cancelled.</summary>
    static (string Mode, string Exe, string Engine)? ResolveRunMode(IRunEnvironment env, IRunPrompts ui, ScriptFile f, ScriptMeta meta, string mode, string? engineOverride)
    {
        var eng = env.Engines;
        var ei = meta.Engine;
        if (engineOverride is "7" or "5.1" && eng.Exe(engineOverride) != null)
            ei = new EngineInfo { Engine = engineOverride, Auto = false };
        var re = AnalysisService.RunEngine(ei, eng);
        var res = (Mode: mode, Exe: re.Exe, Engine: re.Engine);
        var issues = meta.Issues.Where(i => !(i.Kind == "admin" && mode == RunModes.Admin)).ToList();
        if (engineOverride != null) issues = issues.Where(i => i.Kind is "admin" or "module").ToList();
        if (issues.Count == 0) return res;
        string text = string.Join("\n\n", issues.Select(i => "•  " + i.Text())) + "\n\n" + Loc.T("issQuestion");
        var kinds = issues.Select(i => i.Kind).ToHashSet();
        bool needAdmin = kinds.Contains("admin");
        bool missing7 = kinds.Contains("need7") || (kinds.Contains("nengine") && ei.Engine == "7");
        bool to7 = res.Engine == "5.1" && env.PwshOk && (kinds.Contains("core") || kinds.Contains("syntax7") || kinds.Contains("parse"));
        bool to51 = res.Engine == "7" && env.Ps51Ok && (kinds.Contains("desktop") || kinds.Contains("winps") || kinds.Contains("parse"));
        var buttons = new List<RunChoice>();
        if (needAdmin) buttons.Add(new("admin", Loc.T("runAdmin"), true));
        if (missing7) buttons.Add(new("install7", Loc.T("p7Install"), !needAdmin));
        if (to7) buttons.Add(new("pwsh", Loc.T("runPwsh"), !needAdmin));
        if (to51) buttons.Add(new("ps51", Loc.T("runWith51"), !needAdmin));
        buttons.Add(new("anyway", missing7 && res.Engine == "5.1" ? Loc.T("runWith51") : Loc.T("runAnyway")));
        buttons.Add(new("cancel", Loc.T("cancel")));
        bool warn = issues.Any(i => i.Level != "info");
        switch (ui.ChooseCompat(f, text, warn, buttons))
        {
            case "admin":
                // The administrator confirmation is part of the safety check
                if (!Safe(env, ui, f, meta, RunModes.Admin)) return null;
                res.Mode = RunModes.Admin; break;
            case "pwsh": res.Exe = env.PwshPath!; res.Engine = "7"; break;
            case "ps51": res.Exe = env.Ps51Path!; res.Engine = "5.1"; break;
            case "install7": ui.InstallPwsh(); return null;
            case "anyway": break;
            default: return null;
        }
        return res;
    }

    // ---------- Scheduled ----------

    /// <summary>A scheduled run (or --run --headless): no questions; whatever would ask blocks it. window: in a console window
    /// instead of hidden. Never an administrator run (a schedule can't elevate without a UAC prompt).</summary>
    public static async Task<PlanResult> PlanScheduledAsync(IRunEnvironment env, string rel, string preset, bool window)
    {
        var f = env.Find(rel);
        string name = f?.Name ?? Path.GetFileNameWithoutExtension(rel);
        if (f == null || !env.FileExists(f.Path)) return PlanResult.Blocked("schedBlockScript", "", name, rel);
        f = Rehash(env, f);
        var meta = await AnalyzedMeta(env, null, f);
        var remembered = RunPreset.Get(env.Settings, f.Rel);
        bool motw = MotwBlock(env, f, meta) != null;
        string mode = window ? RunModes.Window : RunModes.Inline;
        IDictionary<string, object>? values = remembered?.Values;
        ParamPreset? p = null;
        var ignored = new List<string>();
        if (preset.Length > 0)
        {
            p = Presets.Get(env.Settings, f.Rel, preset);
            if (p != null) (values, ignored) = Presets.Apply(meta.Params, f.UsesArgs, p.Values);
        }
        var missing = f.UsesArgs ? new List<string>() : RunArgs.MissingMandatory(meta.Params, values);
        string? block = ScheduleGate.Block(new ScheduleGate.Input
        {
            DisclaimerAccepted = env.Settings.DisclaimerAccepted.Length > 0,
            ExplicitMode = remembered?.Mode ?? ScriptHeader.ModeOf(f),
            RequiresElevation = f.IsPowerShell && meta.Req.Elevation,
            Motw = motw,
            Scan = f.IsPython ? "none" : meta.Scan,   // batch: "basic", custom interpreters: "custom" (neither blocks)
            SafetyWarnings = env.Settings.SafetyWarnings,
            NeedsConfirmation = Safety.Needed(env.Settings, meta.Danger, mode, f.Rel, f.Path, f.Hash),
            PresetMissing = preset.Length > 0 && p == null,
            MissingMandatory = missing
        });
        if (block != null) return PlanResult.Blocked(block, block == "schedBlockPreset" ? preset : string.Join(", ", missing), f.Name, f.Rel);
        string? pr = p?.Name;
        RunPlan plan;
        if (f.IsPython)
        {
            string userArgs = PythonRun.ArgsFrom(values);
            if (!PythonRun.IsValidArgs(userArgs)) return PlanResult.Blocked("schedBlockArgs", "", f.Name, f.Rel);
            var venv = env.VenvFor(f);
            if (venv is { Broken: true }) return PlanResult.Blocked("schedBlockPython", "", f.Name, f.Rel);
            var py = venv != null ? PythonInfo.FromVenv(venv) : await env.EnsurePythonAsync();
            if (py == null) return PlanResult.Blocked("schedBlockPython", "", f.Name, f.Rel);
            plan = PythonPlan(env, f, py, mode, null, userArgs, RunOrigins.Schedule, pr, ignored);
        }
        else if (!f.IsPowerShell)
        {
            // Batch files and custom interpreters: the free arguments of the preset, then the same plan as an interactive run
            string userArgs = PythonRun.ArgsFrom(values);
            var other = env.PlanOther(f, userArgs, out var err, out var detail);
            if (other == null)
                return err is "customGone" or "customNoProgram" ? PlanResult.Blocked(err, detail, f.Name, f.Rel) : PlanResult.Blocked("schedBlockArgs", "", f.Name, f.Rel);
            plan = OtherPlan(env, f, other, mode, null, PythonRun.Display(userArgs), RunOrigins.Schedule, pr, ignored);
        }
        else
        {
            var re = AnalysisService.RunEngine(meta.Engine, env.Engines);
            string args = RunArgs.Format(meta.Params, values);
            string path = f.Path;
            plan = new RunPlan
            {
                File = f, Mode = mode, ArgText = args, Engine = re.Engine, Exe = re.Exe, Origin = RunOrigins.Schedule, Preset = pr, Ignored = ignored,
                InlineArgs = RunArgs.InlineArgs(path, args),
                WindowArgs = done => RunArgs.WindowArgs(path, args, null, done),
                WindowArgsWithLog = (done, log) => RunArgs.WindowArgs(path, args, null, done, log),
                Header = Loc.F("runHeader", f.Name, args, EngineInfo.Short(re.Engine), env.Now)
            };
        }
        // Last check before the process starts: the file must still be the one that was checked and trusted
        if (env.FileSha256(f.Path) != f.Hash) return PlanResult.Blocked("schedBlockConfirm", "", f.Name, f.Rel);
        return PlanResult.Ready(plan);
    }
}
