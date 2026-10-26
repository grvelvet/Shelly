using System.Diagnostics;
using Shelly.Core;
using WinForms = System.Windows.Forms;

namespace Shelly.Services;

/// <summary>Unattended runs (1.2.0). Nobody can answer a dialog, so anything that would ask (administrator rights, Mark of
/// the Web, findings that are not trusted for this exact content, an unverified script, missing parameters) skips the run
/// instead: a failed history entry and a notification say why. The checks are RunPlanner.PlanScheduledAsync (1.3.1).
/// Used by the running Shelly and by a Shelly started by Task Scheduler while none was running (headless).</summary>
public static class Unattended
{
    /// <summary>The history entry of a skipped run: failed, exit code -1, the reason as its output.</summary>
    public static RunEntry BlockedEntry(HistoryStore h, PlanResult r, string preset, bool window)
    {
        var e = new RunEntry
        {
            Id = HistoryStore.NewId(), Rel = r.Rel, Name = r.Name, Engine = r.Plan?.Engine ?? EngineOfRel(r.Rel), Args = "",
            Mode = window ? RunModes.Window : RunModes.Inline, Origin = RunOrigins.Schedule, Start = DateTime.Now, Kind = RunKinds.Fail, Code = -1,
            HasOutput = true, Preset = preset
        };
        h.SaveOutput(e, Loc.F("schedBlockedOut", r.Reason));
        return e;
    }

    /// <summary>The engine of a skipped run's history entry, from the file name.</summary>
    static string EngineOfRel(string rel) => ScriptKinds.Lang(rel) switch
    {
        "py" => EngineInfo.Python, "bat" => EngineInfo.Batch, "custom" => EngineInfo.Custom, _ => "5.1"
    };

    // ---------- Headless: Task Scheduler started Shelly and no Shelly of this folder is running ----------

    /// <summary>Runs one schedule (or --run --headless) without a window and returns the process exit code (Task Scheduler
    /// shows it as the last run result): 0 = done, the script's exit code when it failed, 1 = skipped, 2 = no such schedule.
    /// The settings file is only read; the result goes to the history (merged with what a Shelly started meanwhile wrote)
    /// and that Shelly is told about it.</summary>
    public static async Task<int> RunHeadlessAsync(AppPaths paths, Cli cli)
    {
        var settings = Settings.Load(paths.Settings);
        if (!settings.LanguageSaved) settings.Language = Loc.SystemLanguage();
        Loc.SetLanguage(settings.Language);
        string rel, preset;
        bool window;
        if (cli.Scheduled != null)
        {
            var s = ScheduleList.Get(settings, cli.Scheduled);
            if (s == null) { AppLog.Write("Scheduled task " + cli.Scheduled + ": no such schedule in the settings (a leftover task)"); return 2; }
            if (!s.Enabled) { AppLog.Write("Scheduled task " + cli.Scheduled + ": the schedule is off"); return 0; }
            (rel, preset, window) = (s.Rel, s.Preset, s.Window);
        }
        else (rel, preset, window) = (cli.Run!, cli.Preset ?? "", false);
        AppLog.Write("Unattended run of " + rel + (preset.Length > 0 ? " (preset " + preset + ")" : "") + (cli.Scheduled != null ? ", schedule " + cli.Scheduled : ""));

        var st = new AppState(paths, settings) { ReadOnlySettings = true };
        st.History.Max = settings.HistoryMaxValue;
        try
        {
            st.ScanOnly();
            var r = await RunPlanner.PlanScheduledAsync(new AppRunEnvironment(st), rel, preset, window);
            RunEntry e;
            int exit;
            if (r.Plan is not { } plan)
            {
                e = BlockedEntry(st.History, r, preset, window);
                AppLog.Write("Unattended run skipped: " + r.Reason);
                exit = 1;
            }
            else
            {
                if (plan.Ignored.Count > 0) AppLog.Write("Preset " + plan.Preset + " of " + rel + ": skipped " + string.Join(", ", plan.Ignored));
                e = plan.Mode == RunModes.Window ? await RunWindowAsync(st, plan) : await RunInlineAsync(st, plan);
                exit = e.Kind == RunKinds.Ok ? 0 : e.Code is 0 or int.MinValue ? 1 : e.Code;
                if (e.Kind != RunKinds.Ok) AppLog.Write(Loc.F("logRunError", e.Name, e.Code));
            }
            st.History.Add(e);
            if (!st.History.Save()) AppLog.Write("History could not be saved: " + st.History.LastError);
            // A Shelly started meanwhile shows it at once
            if (SingleInstance.IsRunning(paths.Root)) SingleInstance.SendTo(paths.Root, "hist\t" + e.Id);
            if (Notifications.Should(settings.Notify, settings.ToastAfterSeconds, e.Kind, e.Code, e.Ms, true, true, false))
            {
                var (title, text, ok) = r.Plan == null ? NotifyText.ForBlocked(r.Name, r.Reason) : NotifyText.ForRun(e, true);
                await ShowBalloonAsync(title, text, ok, e.Id);
            }
            return exit;
        }
        finally
        {
            try { st.Analysis.SaveCache(p => System.IO.File.Exists(p)); } catch (Exception ex) { AppLog.Write("Analysis cache not saved: " + ex.Message); }
            st.Dispose();
        }
    }

    static RunEntry NewEntry(RunPlan p) => new()
    {
        Id = HistoryStore.NewId(), Rel = p.File.Rel, Name = p.File.Name, Engine = p.Engine, Args = p.ArgText, Mode = p.Mode,
        Origin = RunOrigins.Schedule, Start = DateTime.Now, Kind = RunKinds.Running, Preset = p.Preset ?? ""
    };

    static async Task<RunEntry> RunInlineAsync(AppState st, RunPlan p)
    {
        var e = NewEntry(p);
        using var runner = new InlineRunner { MixedFallback = p.Mixed };
        runner.Log.MaxChars = st.Settings.HistoryOutputChars;
        runner.Note(p.Header);
        var done = new TaskCompletionSource();
        runner.Finished += _ => done.TrySetResult();
        var clock = Stopwatch.StartNew();
        try { runner.Start(p.Exe, p.InlineArgs, p.WorkDir ?? Path.GetDirectoryName(p.File.Path)!, p.Env); }
        catch (Exception ex)
        {
            runner.Note(Loc.F("startFail", ex.Message));
            e.Kind = RunKinds.Fail; e.Code = -1; e.HasOutput = true;
            st.History.SaveOutput(e, runner.Log.GetText(Loc.T("outSkipped")));
            return e;
        }
        // The lines queue is for a live view; nobody watches here
        while (!done.Task.IsCompleted)
        {
            while (runner.Lines.TryDequeue(out _)) { }
            await Task.WhenAny(done.Task, Task.Delay(500));
        }
        while (runner.Lines.TryDequeue(out _)) { }
        double ms = clock.Elapsed.TotalMilliseconds;
        runner.Note(Loc.F("runFooter", runner.ExitCode, RunResult.FormatDuration(ms)));
        e.Ms = (long)ms;
        e.Code = runner.ExitCode;
        e.Truncated = runner.Log.Truncated;
        e.HasOutput = true;
        st.History.SaveOutput(e, runner.Log.GetText(Loc.T("outSkipped")));
        e.Kind = RunResult.KindFor(runner.ExitCode, false);
        return e;
    }

    static async Task<RunEntry> RunWindowAsync(AppState st, RunPlan p)
    {
        var e = NewEntry(p);
        string doneFile = RunManager.DoneFileFor(st.Paths, e.Id);
        string logFile = RunManager.LogFileFor(st.Paths, e.Id);
        var clock = Stopwatch.StartNew();
        Process? proc;
        try
        {
            proc = Process.Start(new ProcessStartInfo(p.Exe, p.GetWindowArgs(doneFile, logFile)) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(p.File.Path)! });
        }
        catch (Exception ex)
        {
            e.Kind = RunKinds.Fail; e.Code = -1; e.HasOutput = true;
            st.History.SaveOutput(e, Loc.F("startFail", ex.Message));
            return e;
        }
        // Until the script ends (the end marker) or the window is closed; the window itself stays open for the user
        int? code;
        while (true)
        {
            code = RunArgs.ReadDone(doneFile);
            if (code != null) break;
            bool exited = true;
            try { exited = proc == null || proc.HasExited; } catch (Exception ex) { AppLog.Write("Window run not tracked: " + ex.Message); }
            if (exited) { code = RunArgs.ReadDone(doneFile); break; }
            await Task.Delay(1000);
        }
        e.Ms = (long)clock.Elapsed.TotalMilliseconds;
        if (code != null) { e.Code = code.Value; e.Kind = code == 0 ? RunKinds.Ok : RunKinds.Fail; }
        else
        {
            int c = 0;
            try { if (proc != null) c = proc.ExitCode; } catch (Exception ex) { AppLog.Write("Exit code not read: " + ex.Message); }
            e.Code = c; e.Kind = RunKinds.Closed;
        }
        string cleaned = TranscriptCleaner.ReadRawLogFile(logFile);
        if (cleaned.Length > 0)
        {
            var (capped, trunc) = TranscriptCleaner.Cap(cleaned, st.Settings.HistoryOutputKBValue, Loc.F("outSkipped", "…"));
            if (capped.Length > 0) { e.Truncated = trunc; st.History.SaveOutput(e, capped); }


        }
        try { System.IO.File.Delete(doneFile); } catch (Exception ex) { AppLog.Write("Run marker not deleted: " + ex.Message); }
        try { if (System.IO.File.Exists(logFile)) System.IO.File.Delete(logFile); } catch (Exception ex) { AppLog.Write("Run log not deleted: " + ex.Message); }
        try { proc?.Dispose(); } catch (Exception ex) { AppLog.Write("Process handle not released: " + ex.Message); }
        return e;
    }

    /// <summary>A notification without a tray icon of its own: a temporary NotifyIcon that is removed again after the
    /// balloon (Windows 10/11 show it as a native toast). A click starts Shelly at the run in History.</summary>
    static async Task ShowBalloonAsync(string title, string text, bool ok, string entryId)
    {
        try
        {
            using var own = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            using var icon = new WinForms.NotifyIcon { Text = "Shelly", Icon = own ?? System.Drawing.SystemIcons.Application };
            try { await ShowAsync(icon); }
            finally { icon.Visible = false; }
        }
        catch (Exception ex) { AppLog.Error("notify", ex); }

        async Task ShowAsync(WinForms.NotifyIcon icon)
        {
            var closed = new TaskCompletionSource();
            icon.BalloonTipClicked += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--history " + entryId) { UseShellExecute = false }); }
                catch (Exception ex) { AppLog.Error("notify", ex); }
                closed.TrySetResult();
            };
            // Not on BalloonTipClosed: the toast moves to the notification center then, and stays clickable while the icon is there
            icon.Visible = true;
            icon.ShowBalloonTip(10000, title, text, ok ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning);
            // Windows decides how long the toast stays; it goes when the icon goes, so the icon stays a while
            await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(25)));
        }
    }
}
