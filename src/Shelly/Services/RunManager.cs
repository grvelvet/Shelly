using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Shelly.Core;
using Shelly.Dialogs;

namespace Shelly.Services;

/// <summary>A run in progress: inline (output captured) or in a separate / administrator window.</summary>
public sealed class RunSession : INotifyPropertyChanged
{
    public required RunEntry Entry { get; init; }
    public required ScriptFile Script { get; init; }
    public InlineRunner? Runner { get; init; }
    public Process? Proc { get; set; }
    public DateTime Started { get; } = DateTime.Now;
    /// <summary>The output shown so far (inline runs).</summary>
    public StringBuilder Text { get; } = new();
    public bool TextCapped;
    public int LineCount;
    public bool IsInline => Runner != null;
    public bool Ended { get; set; }
    /// <summary>Window runs (1.2.0): the file the window writes the exit code to when the script ends (the window stays open).</summary>
    public string? DoneFile { get; init; }
    /// <summary>Window runs (1.4.0): the transcript log file while the script runs.</summary>
    public string? LogFile { get; init; }
    public string WindowOutput { get; internal set; } = "";
    public string ElapsedText => RunResult.FormatElapsed((DateTime.Now - Started).TotalSeconds);
    public event Action<string>? Appended;
    public event Action<string>? WindowOutputChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RaiseAppended(string s) => Appended?.Invoke(s);
    internal void RaiseWindowOutput(string s) { WindowOutput = s; WindowOutputChanged?.Invoke(s); }
    internal void Tick() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ElapsedText)));
}

/// <summary>Starts and tracks runs. Several scripts can run at the same time; the same script inline only once.</summary>
public sealed class RunManager
{
    readonly AppState state;
    readonly DispatcherTimer pump;
    readonly DispatcherTimer tick;
    public List<RunSession> Active { get; } = new();
    /// <summary>A run started or ended (lists, tray, status).</summary>
    public event Action? Changed;
    public event Action<RunSession>? Started;
    public event Action<RunSession>? Finished;
    /// <summary>Show this run in a viewer (a new run, a toast click).</summary>
    public event Action<RunEntry>? ShowRequested;
    // The output panel keeps at most 4 MB / 100 000 lines of a run, then notes that the rest was skipped; output arrives in one batch per 100 ms pump tick, so a chatty script costs at most 10 inserts a second.
    const int DisplayCap = 4 * 1024 * 1024, DisplayLineCap = 100_000;

    public RunManager(AppState state)
    {
        this.state = state;
        prompts = new Prompts(this);
        pump = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        pump.Tick += (_, _) => Pump();
        tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) =>
        {
            foreach (var s in Active.ToList())
            {
                s.Tick();
                if (s.LogFile != null && !s.Ended && File.Exists(s.LogFile))
                {
                    string live = TranscriptCleaner.ReadLogFile(s.LogFile);
                    if (live.Length > 0 && live != s.WindowOutput)
                        s.RaiseWindowOutput(live);
                }
                // A window run whose script has ended (the window may stay open)
                if (s.DoneFile != null && !s.Ended && File.Exists(s.DoneFile)) CompleteWindowDone(s);
            }
        };
        // Both timers run only while something runs: an idle Shelly has no periodic wake-ups
        Changed += () =>
        {
            bool on = Active.Count > 0;
            if (on && !pump.IsEnabled) { pump.Start(); tick.Start(); }
            else if (!on) { pump.Stop(); tick.Stop(); }
        };
    }

    public RunSession? SessionOf(string entryId) => Active.FirstOrDefault(s => s.Entry.Id == entryId);
    public bool IsRunning(string rel) => Active.Any(s => string.Equals(s.Script.Rel, rel, StringComparison.OrdinalIgnoreCase));
    public RunSession? LatestOf(string rel) => Active.LastOrDefault(s => string.Equals(s.Script.Rel, rel, StringComparison.OrdinalIgnoreCase));
    public bool InlineRunning(string rel) => Active.Any(s => s.IsInline && string.Equals(s.Script.Rel, rel, StringComparison.OrdinalIgnoreCase));

    void Pump()
    {
        foreach (var s in Active.ToList())
        {
            if (s.Runner == null) continue;
            if (s.Runner.Lines.IsEmpty) continue;
            var sb = new StringBuilder();
            while (s.Runner.Lines.TryDequeue(out var line)) sb.Append(line).Append("\r\n");
            Append(s, sb.ToString());
        }
    }

    void Append(RunSession s, string chunk)
    {
        if (chunk.Length == 0) return;
        if (s.TextCapped) return;
        int lines = 0;
        foreach (char ch in chunk) if (ch == '\n') lines++;
        s.LineCount += lines;
        if (s.Text.Length + chunk.Length > DisplayCap || s.LineCount > DisplayLineCap)
        {
            s.TextCapped = true;
            chunk = "\r\n" + Loc.F("outSkipped", "…") + "\r\n";
        }
        s.Text.Append(chunk);
        s.RaiseAppended(chunk);
    }

    // ---------- Starting ----------

    /// <summary>The file changed between the check (analysis, dialogs, trust) and the start.</summary>
    sealed class ScriptChangedException : Exception { }

    /// <summary>What RunPlanner reads (1.3.1).</summary>
    IRunEnvironment Env => env ??= new AppRunEnvironment(state, InlineRunning);
    IRunEnvironment? env;
    readonly Prompts prompts;

    public async Task RunAsync(ScriptFile f, RunOptions o)
    {
        try
        {
            // A script that changed after it was checked is checked again (analysis and confirmation) up to two more times
            for (int attempt = 0; ; attempt++)
            {
                try { await RunCore(f, o); return; }
                catch (ScriptChangedException) when (attempt < 2) { App.Status(Loc.F("runChanged", f.Name), true); }
            }
        }
        catch (ScriptChangedException) { App.Status(Loc.F("runChangedFail", f.Name), true); }
        catch (Exception ex) { AppLog.Error("run", ex); App.Status(Loc.F("unexpected", ex.Message), true); }
    }

    async Task RunCore(ScriptFile f, RunOptions o)
    {
        if (!File.Exists(f.Path))
        {
            state.RefreshScripts();
            App.Status(Loc.F("fileMissing", f.Name), true);
            return;
        }
        // The file may have changed since the list was scanned
        f = state.Find(f.Rel) is { } fresh && File.Exists(fresh.Path) ? fresh : f;
        // Every check and question is RunPlanner's (shared with scheduled runs since 1.3.1)
        var r = await RunPlanner.PlanInteractiveAsync(Env, prompts, f, o);
        if (r.Status == PlanStatus.Changed) throw new ScriptChangedException();
        if (r.Plan is not { } p) return;
        if (p.Mode == RunModes.Inline)
        {
            if (InlineRunning(p.File.Rel)) { App.Status(Loc.F("busySame", p.File.Name), true); return; }
            StartInline(p);
        }
        else StartInWindow(p);
    }

    /// <summary>A scheduled run while Shelly is open (Task Scheduler started Shelly.exe --scheduled, which handed it over),
    /// or "Run now" in Settings > Schedules: the same checks as a headless run, no dialogs, and the window the user works
    /// in is left alone. window: in a console window instead of hidden.</summary>
    public async Task RunUnattendedAsync(string rel, string preset, bool window)
    {
        try
        {
            if (state.Find(rel) is not { } f0 || !File.Exists(f0.Path)) state.RefreshScripts();
            if (!window && InlineRunning(rel))
            {
                string n = state.Find(rel)?.Name ?? rel;
                AppLog.Write("Scheduled run skipped: " + Loc.F("schedBlockBusy", n));
                App.Status(Loc.F("schedBlockBusy", n), true);
                return;
            }
            var r = await RunPlanner.PlanScheduledAsync(Env, rel, preset, window);
            if (r.Plan is not { } p)
            {
                var e = Unattended.BlockedEntry(state.History, r, preset, window);
                AppLog.Write("Scheduled run skipped: " + r.Reason);
                AddHistory(e);
                state.SetLastRun(new LastRun { Rel = e.Rel, Kind = RunKinds.Fail, Code = -1, Time = e.Start, Ms = 0, Mode = e.Mode });
                state.NotifyScriptsChanged();
                App.Status(Loc.F("notifBlockedTitle", r.Name), true);
                NotifyEnd(e, true, !window, r);
                return;
            }
            if (p.Ignored.Count > 0) AppLog.Write("Preset " + p.Preset + " of " + p.File.Rel + ": skipped " + string.Join(", ", p.Ignored));
            if (p.Mode == RunModes.Inline)
            {
                if (InlineRunning(p.File.Rel)) return;
                StartInline(p);
            }
            else StartInWindow(p);
        }
        catch (Exception ex) { AppLog.Error("schedule", ex); }
    }

    /// <summary>Asks before a flagged script runs or is built, and before every administrator run. true = go ahead.</summary>
    public bool ConfirmSafety(ScriptFile f, ScriptMeta meta, string action)
    {
        if (!Safety.Needed(state.Settings, meta.Danger, action, f.Rel, f.Path, f.Hash)) return true;
        var r = SafetyDialog.Show(f.Name, meta.Danger, action, meta.Scan, Safety.SuggestSources(meta.Danger));
        if (r.Choice == "source" && r.Source != null)
        {
            // "Trust this source": remembered in Settings > Safety, then the check runs again with it
            TrustedSources.Add(state.Settings, r.Source);
            state.NotifySettingsChanged();
            state.NotifyScriptsChanged();
            App.Status(Loc.F("srcAdded", r.Source));
            return ConfirmSafety(f, state.Meta(f), action);
        }
        if (r.Choice == "edit") { App.OpenEditor(f.Path); App.Status(Loc.F("dzOpened", f.Name)); return false; }
        if (r.Choice != "run") { App.Status(action == "build" ? Loc.T("buildCancelled") : Loc.F("cxSafety", f.Name)); return false; }
        if (r.Trust) { Safety.AddTrust(state.Settings, f.Rel, Safety.TrustKeyOf(f.Rel, f.Hash)); state.NotifySettingsChanged(); state.NotifyScriptsChanged(); }
        return true;
    }

    /// <summary>Mark of the Web on the script or on a local script it loads: shows the Unblock dialog. true = not blocked.</summary>
    public bool MotwGate(ScriptFile f, ScriptMeta meta)
    {
        if (RunPlanner.MotwBlock(Env, f, meta) is not { } others) return true;
        ShowMotw(f, meta, others);
        return false;
    }

    void ShowMotw(ScriptFile f, ScriptMeta meta, List<(string Path, ZoneInfo Zone)> others)
    {
        var zone = meta.Zone;
        var shown = zone != null && zone.Blocks ? zone : others[0].Zone;
        string c = MotwDialog.Show(f.Name, shown, zone != null && zone.Blocks ? others : others.Skip(1).ToList(), meta.Danger, meta.Scan);
        if (c == "edit") { App.OpenEditor(f.Path); App.Status(Loc.F("dzOpened", f.Name)); return; }
        if (c != "unblock") { App.Status(Loc.F("motwBlocked", f.Name)); return; }
        bool ok = true;
        if (zone != null && zone.Blocks) ok &= MotW.Unblock(f.Path);
        foreach (var (p, _) in others) ok &= MotW.Unblock(p);
        state.RefreshScripts();
        state.NotifyScriptsChanged();
        // Unblocked: the user starts it again, now through the normal safety check of the re-scanned script
        App.Status(ok ? Loc.F("motwUnblocked", f.Name) : Loc.F("motwUnblockFail", f.Name), !ok);
    }

    /// <summary>RunPlanner's questions as Shelly's dialogs.</summary>
    sealed class Prompts : IRunPrompts
    {
        readonly RunManager runs;
        public Prompts(RunManager runs) => this.runs = runs;

        public void Status(string text, bool important = false) => App.Status(text, important);

        public RunAnswer? AskRunSettings(ScriptFile f) =>
            RunSettingsWindow.Ask(runs.state, f) is { } rs ? new RunAnswer(rs.Mode, rs.Values, rs.Preset) : null;

        public bool ConfirmUnverified(ScriptFile f, bool engineAvailable) =>
            Dlg.Confirm(Loc.T("dzTitle"), Loc.F(engineAvailable ? "analyzeFail" : "analyzeNone", f.Name), Loc.T("dzUnverified"), Loc.T("runAnyway"), true);

        public bool ConfirmSafety(ScriptFile f, ScriptMeta meta, string action) => runs.ConfirmSafety(f, meta, action);

        public void MotwBlocked(ScriptFile f, ScriptMeta meta, List<(string Path, ZoneInfo Zone)> others) => runs.ShowMotw(f, meta, others);

        public string? ChooseCompat(ScriptFile f, string text, bool warn, IReadOnlyList<RunChoice> choices) =>
            Dlg.Choose(Loc.T("issTitle"), f.Name, text, warn ? Glyphs.Warning : Glyphs.Shield,
                warn ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush", choices.Select(c => new Dlg.Choice(c.Id, c.Label, c.Default)).ToArray());

        public void InstallPwsh() => App.InstallPwsh();

        public void VenvBroken(ScriptFile f, PythonVenv venv)
        {
            string c = Dlg.Choose(Loc.T("pyVenvBrokenTitle"), f.Name, Loc.F("pyVenvBrokenText", f.Name, venv.Root), Glyphs.Warning, "SystemFillColorCautionBrush",
                new Dlg.Choice("folder", Loc.T("pyVenvOpen"), true), new Dlg.Choice("settings", Loc.T("pyOpenSettings")), new Dlg.Choice("cancel", Loc.T("close")));
            if (c == "folder") ShellOps.OpenFolder(venv.Root);
            else if (c == "settings") { Views.SettingsPage.ShowPythonNext(); App.Win?.Navigate("settings"); }
        }

        public void PythonMissing(ScriptFile f, string text)
        {
            string c = Dlg.Choose(Loc.T("pyMissingTitle"), f.Name, text, Glyphs.Warning, "SystemFillColorCautionBrush",
                new Dlg.Choice("settings", Loc.T("pyOpenSettings"), true), new Dlg.Choice("download", Loc.T("pyDownload")), new Dlg.Choice("cancel", Loc.T("close")));
            if (c == "settings") { Views.SettingsPage.ShowPythonNext(); App.Win?.Navigate("settings"); }
            else if (c == "download") ShellOps.Open("https://www.python.org/downloads/windows/");
        }

        public void CustomMissing(ScriptFile f, string text)
        {
            string c = Dlg.Choose(Loc.T("engCustom"), f.Name, text, Glyphs.Warning, "SystemFillColorCautionBrush",
                new Dlg.Choice("settings", Loc.T("pyOpenSettings"), true), new Dlg.Choice("cancel", Loc.T("close")));
            if (c == "settings") { Views.SettingsPage.ShowCustomNext(); App.Win?.Navigate("settings"); }
        }
    }


    static RunEntry NewEntry(RunPlan p) => new()
    {
        Id = HistoryStore.NewId(), Rel = p.File.Rel, Name = p.File.Name, Engine = p.Engine, Args = p.ArgText, Mode = p.Mode, Origin = p.Origin,
        Start = DateTime.Now, Kind = RunKinds.Running, Preset = p.Preset ?? ""
    };

    void AddHistory(RunEntry e)
    {
        state.History.Max = state.Settings.HistoryMaxValue;
        state.History.Add(e);
        SaveHistory();
        App.HistoryChanged();
    }

    /// <summary>The history file; a failure is shown (1.3.1: also after window runs, which ignored it).</summary>
    void SaveHistory()
    {
        if (!state.History.Save() && state.History.LastError != null) App.Status(Loc.F("histSaveFail", state.History.LastError), true);
    }

    /// <summary>A failed run in Shelly's own log (the log adds the time): script, exit code, then the error text.</summary>
    static void LogRunError(string script, int code, string text) =>
        AppLog.Write(Loc.F("logRunError", script, code) + (text.Length > 0 ? "\n    " + text.Replace("\n", "\n    ") : ""));

    void StartInline(RunPlan p)
    {
        var f = p.File;
        var entry = NewEntry(p);
        // Mixed: batch files and custom interpreters write UTF-8 or the OEM code page, decided line by line
        var runner = new InlineRunner { MixedFallback = p.Mixed };
        runner.Log.MaxChars = state.Settings.HistoryOutputChars;
        runner.Note(p.Header);
        var s = new RunSession { Entry = entry, Script = f, Runner = runner };
        // Subscribed before the start: a script that ends at once must not lose its end
        runner.Finished += r => Application.Current.Dispatcher.BeginInvoke(() => CompleteInline(s));
        try
        {
            runner.Start(p.Exe, p.InlineArgs, p.WorkDir ?? Path.GetDirectoryName(f.Path)!, p.Env);
        }
        catch (Exception ex)
        {
            // Could not start: the entry is kept as a failed run with the reason as its output
            runner.Note(Loc.F("startFail", ex.Message));
            LogRunError(f.Name, -1, ex.Message);
            entry.Kind = RunKinds.Fail; entry.Code = -1;
            state.History.SaveOutput(entry, runner.Log.GetText(Loc.T("outSkipped")));
            runner.Dispose();
            AddHistory(entry);
            state.SetLastRun(new LastRun { Rel = f.Rel, Kind = RunKinds.Fail, Code = -1, Time = DateTime.Now, Ms = 0, Mode = RunModes.Inline });
            App.Status(Loc.F("startFailWin", f.Name, ex.Message), true);
            state.NotifyScriptsChanged();
            if (p.Origin == RunOrigins.Schedule) NotifyEnd(entry, true, true);
            else ShowRequested?.Invoke(entry);
            return;
        }
        Active.Add(s);
        AddHistory(entry);
        Pump();
        Started?.Invoke(s);
        Changed?.Invoke();
        // A scheduled run does not take over the window the user is working in
        if (p.Origin != RunOrigins.Schedule) ShowRequested?.Invoke(entry);
        else App.Status(Loc.F("schedStarted", f.Name));
    }

    void CompleteInline(RunSession s)
    {
        if (s.Ended) return;
        s.Ended = true;
        var r = s.Runner!;
        Pump();
        var e = s.Entry;
        double ms = (DateTime.Now - s.Started).TotalMilliseconds;
        string kind = RunResult.KindFor(r.ExitCode, r.StoppedByUser);
        string footer = kind == "stopped" ? Loc.F("runFooterStopped", RunResult.FormatDuration(ms))
                                          : Loc.F("runFooter", r.ExitCode, RunResult.FormatDuration(ms));
        r.Note(footer);
        Pump();
        e.Ms = (long)ms;
        e.Code = r.ExitCode;
        e.Truncated = r.Log.Truncated;
        e.HasOutput = true;
        state.History.SaveOutput(e, r.Log.GetText(Loc.T("outSkipped")));
        e.Kind = kind;
        r.Dispose();
        if (kind != RunKinds.Stopped)
        {
            string err = r.ErrorText();
            if (r.ExitCode != 0 || err.Length > 0) LogRunError(s.Script.Name, r.ExitCode, err);   // stderr or a non-zero exit code
        }
        Active.Remove(s);
        SaveHistory();
        state.SetLastRun(new LastRun { Rel = s.Script.Rel, Kind = kind, Code = r.ExitCode, Time = s.Started, Ms = (long)ms, Mode = RunModes.Inline });
        Finished?.Invoke(s);
        Changed?.Invoke();
        App.HistoryChanged();
        state.NotifyScriptsChanged();
        NotifyEnd(e, e.Origin == RunOrigins.Schedule, true);
    }

    /// <summary>The notification for a finished run (Settings > Running > Notifications; see Notifications.Should).
    /// A click opens Shelly at the run in History.</summary>
    void NotifyEnd(RunEntry e, bool scheduled, bool inline, PlanResult? blocked = null)
    {
        var w = App.Win;
        bool mainActive = w != null && w.IsVisible && w.IsActive && w.WindowState != WindowState.Minimized;
        var st = state.Settings;
        if (!Notifications.Should(st.Notify, st.ToastAfterSeconds, e.Kind, e.Code, e.Ms, scheduled, inline, mainActive)) return;
        var (title, text, ok) = blocked != null ? NotifyText.ForBlocked(blocked.Name, blocked.Reason) : NotifyText.ForRun(e, scheduled);
        App.Toast(title, text, ok, () => { App.ShowMain(); App.Win?.ShowHistory(e); });
    }

    /// <summary>The end marker of a window run (see RunArgs.WindowArgs).</summary>
    public static string DoneFileFor(AppPaths paths, string entryId)
    {
        string dir = Path.Combine(paths.CacheDir, "runs");
        try { Directory.CreateDirectory(dir); } catch (Exception ex) { AppLog.Write("Run marker folder not created: " + ex.Message); }
        return Path.Combine(dir, entryId + ".exit");
    }

    /// <summary>The transcript log file of a window run (1.4.0).</summary>
    public static string LogFileFor(AppPaths paths, string entryId)
    {
        string dir = Path.Combine(paths.CacheDir, "runs");
        try { Directory.CreateDirectory(dir); } catch (Exception ex) { AppLog.Write("Run marker folder not created: " + ex.Message); }
        return Path.Combine(dir, entryId + ".log");
    }

    /// <summary>End markers older than this belong to runs no Shelly tracks any more.</summary>
    static readonly TimeSpan DoneFileMaxAge = TimeSpan.FromDays(2);
    /// <summary>ERROR_CANCELLED: the user said no to the UAC prompt.</summary>
    const int UacCancelled = 1223;

    /// <summary>End markers and transcript logs left by runs that Shelly no longer tracks (it was closed meanwhile).</summary>
    public static void CleanDoneFiles(AppPaths paths)
    {
        try
        {
            string dir = Path.Combine(paths.CacheDir, "runs");
            if (!Directory.Exists(dir)) return;
            foreach (var pat in new[] { "*.exit", "*.log" })
                foreach (var f in Directory.EnumerateFiles(dir, pat))
                    if (File.GetLastWriteTime(f) < DateTime.Now - DoneFileMaxAge) { try { File.Delete(f); } catch (Exception ex) { AppLog.Write("Old run marker not deleted: " + ex.Message); } }
        }
        catch (Exception ex) { AppLog.Write("Old run markers not cleaned: " + ex.Message); }
    }

    /// <summary>A window run (the plan's command line writes the end marker file; an administrator run also checks the hash after UAC).</summary>
    void StartInWindow(RunPlan plan)
    {
        var f = plan.File;
        bool admin = plan.Admin;
        string dir = Path.GetDirectoryName(f.Path)!;
        string mode = plan.Mode;
        var entry = NewEntry(plan);
        string done = DoneFileFor(state.Paths, entry.Id);
        string log = LogFileFor(state.Paths, entry.Id);
        string args = plan.GetWindowArgs(done, log);
        Process? p;
        try
        {
            var psi = new ProcessStartInfo(plan.Exe, args) { UseShellExecute = true, WorkingDirectory = dir };
            if (admin) psi.Verb = "runas";
            p = Process.Start(psi);
            App.Status(Loc.F(admin ? "startedAdmin" : "startedWindow", f.Name));
        }
        catch (Win32Exception ex) when (admin && ex.NativeErrorCode == UacCancelled)
        {
            App.Status(Loc.F("cxUac", f.Name));
            return;
        }
        catch (Exception ex)
        {
            App.Status(Loc.F("startFailWin", f.Name, ex.Message), true);
            LogRunError(f.Name, -1, ex.Message);
            entry.Kind = RunKinds.Fail; entry.Code = -1;
            AddHistory(entry);
            return;
        }
        entry.Start = DateTime.Now;
        if (p == null)
        {
            entry.Kind = RunKinds.Started;
            AddHistory(entry);
            state.SetLastRun(new LastRun { Rel = f.Rel, Kind = RunKinds.Started, Time = DateTime.Now, Mode = mode });
            state.NotifyScriptsChanged();
            return;
        }
        var s = new RunSession { Entry = entry, Script = f, Proc = p, DoneFile = done, LogFile = log };
        try
        {
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => CompleteWindow(s));
        }
        catch (Exception ex)
        {
            // An elevated process may not be trackable: kept as "started"
            AppLog.Write("Window run of " + f.Rel + " not tracked: " + ex.Message);
            try { p.Dispose(); } catch (Exception dx) { AppLog.Write("Process handle not released: " + dx.Message); }
            entry.Kind = RunKinds.Started;
            AddHistory(entry);
            state.SetLastRun(new LastRun { Rel = f.Rel, Kind = RunKinds.Started, Time = DateTime.Now, Mode = mode });
            state.NotifyScriptsChanged();
            return;
        }
        Active.Add(s);
        AddHistory(entry);
        Started?.Invoke(s);
        Changed?.Invoke();
        state.NotifyScriptsChanged();
        if (p.HasExited) CompleteWindow(s);
    }

    /// <summary>The script of a window run has ended (its end marker is there); the window itself may stay open. The run
    /// gets a real result (ok / fail with the exit code) like an inline run.</summary>
    void SaveWindowLog(RunSession s)
    {
        if (s.LogFile == null) return;
        string cleaned = TranscriptCleaner.ReadLogFile(s.LogFile);
        try { if (File.Exists(s.LogFile)) File.Delete(s.LogFile); } catch (Exception ex) { AppLog.Write("Run log not deleted: " + ex.Message); }
        if (cleaned.Length == 0) cleaned = s.WindowOutput;
        if (cleaned.Length > 0)
        {
            var (capped, trunc) = TranscriptCleaner.Cap(cleaned, state.Settings.HistoryOutputChars, Loc.F("outSkipped", "…"));
            s.Entry.Truncated = trunc;
            state.History.SaveOutput(s.Entry, capped);
        }
    }

    void CompleteWindowDone(RunSession s)
    {
        if (s.Ended) return;
        int? read = RunArgs.ReadDone(s.DoneFile!);
        if (read == null) return;   // still being written: the next tick
        s.Ended = true;
        int code = read.Value;
        try { File.Delete(s.DoneFile!); } catch (Exception ex) { AppLog.Write("Run marker not deleted: " + ex.Message); }
        SaveWindowLog(s);
        try { if (s.Proc != null) { s.Proc.EnableRaisingEvents = false; s.Proc.Dispose(); } } catch (Exception ex) { AppLog.Write("Process handle not released: " + ex.Message); }
        double ms = (DateTime.Now - s.Started).TotalMilliseconds;
        var e = s.Entry;
        e.Ms = (long)ms;
        e.Code = code;
        e.Kind = code == 0 ? RunKinds.Ok : RunKinds.Fail;
        if (code != 0) LogRunError(s.Script.Name, code, Loc.T("logRunWindow"));
        Active.Remove(s);
        SaveHistory();
        state.SetLastRun(new LastRun { Rel = s.Script.Rel, Kind = e.Kind, Code = code, Time = s.Started, Ms = (long)ms, Mode = e.Mode });
        Finished?.Invoke(s);
        Changed?.Invoke();
        App.HistoryChanged();
        state.NotifyScriptsChanged();
        NotifyEnd(e, e.Origin == RunOrigins.Schedule, false);
    }

    void CompleteWindow(RunSession s)
    {
        if (s.Ended) return;
        // The script ended just before the window was closed: its result counts
        if (s.DoneFile != null && RunArgs.ReadDone(s.DoneFile) != null) { CompleteWindowDone(s); return; }
        s.Ended = true;
        if (s.DoneFile != null) { try { File.Delete(s.DoneFile); } catch (Exception ex) { AppLog.Write("Run marker not deleted: " + ex.Message); } }
        SaveWindowLog(s);
        int code = 0;
        // An elevated process may not give its exit code: 0 (a closed window)
        try { code = s.Proc!.ExitCode; } catch (Exception ex) { AppLog.Write("Exit code of " + s.Script.Rel + " not read: " + ex.Message); }
        try { s.Proc!.Dispose(); } catch (Exception ex) { AppLog.Write("Process handle not released: " + ex.Message); }
        double ms = (DateTime.Now - s.Started).TotalMilliseconds;
        var e = s.Entry;
        e.Ms = (long)ms;
        e.Code = code;
        e.Kind = RunKinds.Closed;
        if (code != 0) LogRunError(s.Script.Name, code, Loc.T("logRunWindow"));
        Active.Remove(s);
        SaveHistory();
        state.SetLastRun(new LastRun { Rel = s.Script.Rel, Kind = RunKinds.Closed, Code = code, Time = s.Started, Ms = (long)ms, Mode = e.Mode });
        Finished?.Invoke(s);
        Changed?.Invoke();
        App.HistoryChanged();
        state.NotifyScriptsChanged();
        NotifyEnd(e, e.Origin == RunOrigins.Schedule, false);
    }

    public void Stop(RunSession s)
    {
        if (s.Runner != null) { s.Runner.Stop(); return; }
        try { if (s.Proc != null && !s.Proc.HasExited) s.Proc.Kill(true); }
        catch (Exception ex) { App.Status(ex.Message, true); }
    }

    public void StopAll()
    {
        foreach (var s in Active.ToList()) if (s.IsInline) Stop(s);
    }

    public string RunningText()
    {
        var names = Active.Select(s => s.Script.Name).Distinct().ToList();
        if (names.Count == 0) return "";
        return names.Count == 1 ? Loc.F("trayRunning", names[0]) : Loc.F("trayRunningN", names.Count);
    }
}
