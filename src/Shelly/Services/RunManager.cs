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
    public string ElapsedText => RunResult.FormatElapsed((DateTime.Now - Started).TotalSeconds);
    public event Action<string>? Appended;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RaiseAppended(string s) => Appended?.Invoke(s);
    internal void Tick() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ElapsedText)));
}

public sealed class RunOptions
{
    /// <summary>inline | window | admin; null = the remembered mode (or inline).</summary>
    public string? Mode;
    /// <summary>Parameter values from the Run settings dialog.</summary>
    public IDictionary<string, object>? Values;
    /// <summary>Ready argument text (re-run from the history): no dialog.</summary>
    public string? RawArgs;
    /// <summary>Engine of the earlier run (re-run).</summary>
    public string? Engine;
    public string Origin = "list";
    /// <summary>Open the Run settings dialog first.</summary>
    public bool Ask;
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
        pump = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        pump.Tick += (_, _) => Pump();
        tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => { foreach (var s in Active) s.Tick(); };
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
    public void RequestShow(RunEntry e) => ShowRequested?.Invoke(e);

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

    public async Task RunAsync(ScriptFile f, RunOptions o)
    {
        try { await RunCore(f, o); }
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
        var meta = state.Meta(f);
        if (meta.Pending && (state.PwshOk || state.Ps51Ok))
        {
            App.Status(Loc.F("analyzeWait", f.Name));
            await state.Analysis.WaitAsync(f, TimeSpan.FromSeconds(30));
            meta = state.Meta(f);
            App.Status("");
        }
        // Downloaded from the internet (Mark of the Web): no run from any entry point until the user unblocks it
        if (!MotwGate(f, meta)) return;
        if (o.Ask)
        {
            var rs = RunSettingsWindow.Ask(state, f);
            if (rs == null) { App.Status(Loc.F("cxParams", f.Name)); return; }
            o = new RunOptions { Mode = rs.Mode, Values = rs.Values, Origin = o.Origin };
            meta = state.Meta(f);
        }
        string mode;
        string argText;
        if (o.RawArgs != null)
        {
            argText = o.RawArgs;
            mode = o.Mode ?? "inline";
        }
        else
        {
            var preset = RunPreset.Get(state.Settings, f.Rel);
            var values = o.Values ?? preset?.Values;
            if (RunArgs.MissingMandatory(meta.Params, values).Count > 0)
            {
                App.Status(Loc.F("rsMissing", f.Name));
                var rs = RunSettingsWindow.Ask(state, f);
                if (rs == null) { App.Status(Loc.F("cxParams", f.Name)); return; }
                values = rs.Values;
                o.Mode = rs.Mode;
                meta = state.Meta(f);
            }
            mode = o.Mode ?? (o.Origin == "list" || o.Origin == "jumplist" ? preset?.Mode ?? ScriptHeader.ModeOf(f) : null) ?? "inline";
            argText = RunArgs.Format(meta.Params, values);
        }
        if (mode == "inline" && InlineRunning(f.Rel)) { App.Status(Loc.F("busySame", f.Name), true); return; }

        // PowerShell's parser did not check the script (no engine, or it failed): never "safe" without asking
        if (!meta.Verified && state.Settings.SafetyWarnings && Safety.Blocking(meta.Danger).Count == 0)
        {
            if (!Dlg.Confirm(Loc.T("dzTitle"), Loc.F(state.PwshOk || state.Ps51Ok ? "analyzeFail" : "analyzeNone", f.Name), Loc.T("dzUnverified"), Loc.T("runAnyway"), true)) { App.Status(Loc.F("cxSafety", f.Name)); return; }
        }
        // Safety first (dangerous commands, administrator runs), then compatibility
        if (!ConfirmSafety(f, meta, mode)) return;
        var rm = ResolveRunMode(f, meta, mode, o.Engine);
        if (rm == null) { App.Status(Loc.F("cxCompat", f.Name)); return; }
        if (rm.Value.Mode == "inline")
        {
            if (InlineRunning(f.Rel)) { App.Status(Loc.F("busySame", f.Name), true); return; }
            StartInline(f, argText, rm.Value.Exe, rm.Value.Engine, o.Origin);
        }
        else StartInWindow(f, argText, rm.Value.Exe, rm.Value.Mode == "admin", rm.Value.Engine, o.Origin);
    }

    /// <summary>Asks before a flagged script runs or is built, and before every administrator run. true = go ahead.</summary>
    public bool ConfirmSafety(ScriptFile f, ScriptMeta meta, string action)
    {
        if (!Safety.Needed(state.Settings, meta.Danger, action, f.Rel, f.Path)) return true;
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
        if (r.Trust) { Safety.AddTrust(state.Settings, f.Rel, Safety.TrustKey(f.Rel, f.Path)); state.NotifySettingsChanged(); }
        return true;
    }

    /// <summary>Mark of the Web on the script or on a local script it loads: shows the Unblock dialog. true = not blocked.</summary>
    public bool MotwGate(ScriptFile f, ScriptMeta meta)
    {
        var zone = meta.Zone;
        List<(string Path, ZoneInfo Zone)> others;
        try { others = MotW.MarkedReferences(f.Path, File.ReadAllText(f.Path)); } catch { others = new(); }
        if ((zone == null || !zone.Blocks) && others.Count == 0) return true;
        var shown = zone != null && zone.Blocks ? zone : others[0].Zone;
        string c = MotwDialog.Show(f.Name, shown, zone != null && zone.Blocks ? others : others.Skip(1).ToList(), meta.Danger, meta.Scan);
        if (c == "edit") { App.OpenEditor(f.Path); App.Status(Loc.F("dzOpened", f.Name)); return false; }
        if (c != "unblock") { App.Status(Loc.F("motwBlocked", f.Name)); return false; }
        bool ok = true;
        if (zone != null && zone.Blocks) ok &= MotW.Unblock(f.Path);
        foreach (var (p, _) in others) ok &= MotW.Unblock(p);
        state.RefreshScripts();
        state.NotifyScriptsChanged();
        // Unblocked: the user starts it again, now through the normal safety check of the re-scanned script
        App.Status(ok ? Loc.F("motwUnblocked", f.Name) : Loc.F("motwUnblockFail", f.Name), !ok);
        return false;
    }

    (string Mode, string Exe, string Engine)? ResolveRunMode(ScriptFile f, ScriptMeta meta, string mode, string? engineOverride)
    {
        var eng = state.Analysis.Eng;
        var ei = meta.Engine;
        if (engineOverride != null && (engineOverride == "7" || engineOverride == "5.1") && eng.Exe(engineOverride) != null)
            ei = new EngineInfo { Engine = engineOverride, Auto = false };
        var re = AnalysisService.RunEngine(ei, eng);
        var res = (Mode: mode, Exe: re.Exe, Engine: re.Engine);
        var issues = meta.Issues.Where(i => !(i.Kind == "admin" && mode == "admin")).ToList();
        if (engineOverride != null) issues = issues.Where(i => i.Kind is "admin" or "module").ToList();
        if (issues.Count == 0) return res;
        string text = string.Join("\n\n", issues.Select(i => "•  " + i.Text())) + "\n\n" + Loc.T("issQuestion");
        var kinds = issues.Select(i => i.Kind).ToHashSet();
        bool needAdmin = kinds.Contains("admin");
        bool missing7 = kinds.Contains("need7") || (kinds.Contains("nengine") && ei.Engine == "7");
        bool to7 = res.Engine == "5.1" && state.PwshOk && (kinds.Contains("core") || kinds.Contains("syntax7") || kinds.Contains("parse"));
        bool to51 = res.Engine == "7" && state.Ps51Ok && (kinds.Contains("desktop") || kinds.Contains("winps") || kinds.Contains("parse"));
        var buttons = new List<Dlg.Choice>();
        if (needAdmin) buttons.Add(new("admin", Loc.T("runAdmin"), true));
        if (missing7) buttons.Add(new("install7", Loc.T("p7Install"), !needAdmin));
        if (to7) buttons.Add(new("pwsh", Loc.T("runPwsh"), !needAdmin));
        if (to51) buttons.Add(new("ps51", Loc.T("runWith51"), !needAdmin));
        buttons.Add(new("anyway", missing7 && res.Engine == "5.1" ? Loc.T("runWith51") : Loc.T("runAnyway")));
        buttons.Add(new("cancel", Loc.T("cancel")));
        bool warn = issues.Any(i => i.Level != "info");
        string choice = Dlg.Choose(Loc.T("issTitle"), f.Name, text, warn ? Glyphs.Warning : Glyphs.Shield,
            warn ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush", buttons.ToArray());
        switch (choice)
        {
            case "admin":
                // The administrator confirmation is part of the safety check
                if (!ConfirmSafety(f, meta, "admin")) return null;
                res.Mode = "admin"; break;
            case "pwsh": res.Exe = state.Pwsh!.Path; res.Engine = "7"; break;
            case "ps51": res.Exe = state.Ps51!; res.Engine = "5.1"; break;
            case "install7": App.InstallPwsh(); return null;
            case "anyway": break;
            default: return null;
        }
        return res;
    }

    RunEntry NewEntry(ScriptFile f, string engine, string argText, string mode, string origin) => new()
    {
        Id = HistoryStore.NewId(), Rel = f.Rel, Name = f.Name, Engine = engine, Args = argText, Mode = mode, Origin = origin,
        Start = DateTime.Now, Kind = "running"
    };

    void AddHistory(RunEntry e)
    {
        state.History.Max = state.Settings.HistoryMaxValue;
        state.History.Add(e);
        if (!state.History.Save() && state.History.LastError != null) App.Status(Loc.F("histSaveFail", state.History.LastError), true);
        App.HistoryChanged();
    }

    void StartInline(ScriptFile f, string argText, string exe, string engine, string origin)
    {
        var entry = NewEntry(f, engine, argText, "inline", origin);
        var runner = new InlineRunner { Id = entry.Id };
        runner.Log.MaxChars = state.Settings.HistoryOutputChars;
        string header = Loc.F("runHeader", f.Name, argText, EngineInfo.Short(engine), DateTime.Now.ToString("HH:mm:ss"));
        runner.Note(header);
        var s = new RunSession { Entry = entry, Script = f, Runner = runner };
        try
        {
            runner.Start(exe, RunArgs.InlineArgs(f.Path, argText), Path.GetDirectoryName(f.Path)!);
        }
        catch (Exception ex)
        {
            // Could not start: the entry is kept as a failed run with the reason as its output
            runner.Note(Loc.F("startFail", ex.Message));
            entry.Kind = "fail"; entry.Code = -1;
            state.History.SaveOutput(entry, runner.Log.GetText(Loc.T("outSkipped")));
            AddHistory(entry);
            state.SetLastRun(new LastRun { Rel = f.Rel, Kind = "fail", Code = -1, Time = DateTime.Now, Ms = 0, Mode = "inline" });
            App.Status(Loc.F("startFailWin", f.Name, ex.Message), true);
            state.NotifyScriptsChanged();
            ShowRequested?.Invoke(entry);
            return;
        }
        runner.Finished += r => Application.Current.Dispatcher.BeginInvoke(() => CompleteInline(s));
        Active.Add(s);
        AddHistory(entry);
        Pump();
        Started?.Invoke(s);
        Changed?.Invoke();
        ShowRequested?.Invoke(entry);
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
        Active.Remove(s);
        if (!state.History.Save() && state.History.LastError != null) App.Status(Loc.F("histSaveFail", state.History.LastError), true);
        state.SetLastRun(new LastRun { Rel = s.Script.Rel, Kind = kind, Code = r.ExitCode, Time = s.Started, Ms = (long)ms, Mode = "inline" });
        Finished?.Invoke(s);
        Changed?.Invoke();
        App.HistoryChanged();
        state.NotifyScriptsChanged();
        // A toast when a long run ends while Shelly is not in front
        if (state.Settings.ToastOnFinish && kind != "stopped" && ms >= state.Settings.ToastAfterSeconds * 1000.0 && !App.IsForeground())
        {
            string title = Loc.F("toastDoneTitle", s.Script.Name, RunResult.Format(kind, r.ExitCode).Text);
            App.Toast(title, Loc.F("toastDoneText", RunResult.FormatDuration(ms)), kind == "ok", () => { App.ShowMain(); ShowRequested?.Invoke(e); });
        }
    }

    void StartInWindow(ScriptFile f, string argText, string exe, bool admin, string engine, string origin)
    {
        string dir = Path.GetDirectoryName(f.Path)!;
        string mode = admin ? "admin" : "window";
        var entry = NewEntry(f, engine, argText, mode, origin);
        Process? p;
        try
        {
            var psi = new ProcessStartInfo(exe, RunArgs.WindowArgs(f.Path, argText, admin)) { UseShellExecute = true, WorkingDirectory = dir };
            if (admin) psi.Verb = "runas";
            p = Process.Start(psi);
            App.Status(Loc.F(admin ? "startedAdmin" : "startedWindow", f.Name));
        }
        catch (Win32Exception ex) when (admin && ex.NativeErrorCode == 1223)
        {
            App.Status(Loc.F("cxUac", f.Name));
            return;
        }
        catch (Exception ex)
        {
            App.Status(Loc.F("startFailWin", f.Name, ex.Message), true);
            entry.Kind = "fail"; entry.Code = -1;
            AddHistory(entry);
            return;
        }
        entry.Start = DateTime.Now;
        if (p == null)
        {
            entry.Kind = "started";
            AddHistory(entry);
            state.SetLastRun(new LastRun { Rel = f.Rel, Kind = "started", Time = DateTime.Now, Mode = mode });
            state.NotifyScriptsChanged();
            return;
        }
        var s = new RunSession { Entry = entry, Script = f, Proc = p };
        try
        {
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => CompleteWindow(s));
        }
        catch
        {
            // An elevated process may not be trackable: kept as "started"
            entry.Kind = "started";
            AddHistory(entry);
            state.SetLastRun(new LastRun { Rel = f.Rel, Kind = "started", Time = DateTime.Now, Mode = mode });
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

    void CompleteWindow(RunSession s)
    {
        if (s.Ended) return;
        s.Ended = true;
        int code = 0;
        try { code = s.Proc!.ExitCode; } catch { }
        double ms = (DateTime.Now - s.Started).TotalMilliseconds;
        var e = s.Entry;
        e.Ms = (long)ms;
        e.Code = code;
        e.Kind = "closed";
        Active.Remove(s);
        state.History.Save();
        state.SetLastRun(new LastRun { Rel = s.Script.Rel, Kind = "closed", Code = code, Time = s.Started, Ms = (long)ms, Mode = e.Mode });
        Finished?.Invoke(s);
        Changed?.Invoke();
        App.HistoryChanged();
        state.NotifyScriptsChanged();
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
