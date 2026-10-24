using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;
using Shelly.Views;

namespace Shelly;

public partial class App : Application
{
    public static AppState S { get; private set; } = null!;
    public static MainWindow? Win { get; private set; }
    public static TrayService? Tray { get; private set; }
    public static HotkeyService? Hotkey { get; private set; }
    public static SchedulerService? Sched { get; private set; }
    public static event Action<string, bool>? StatusChanged;
    public static event Action? HistoryUpdated;
    static SingleInstance? single;
    public static bool Exiting { get; private set; }
    readonly Stopwatch startClock = Stopwatch.StartNew();

    protected override void OnStartup(StartupEventArgs e)
    {
        Perf.Begin();
        base.OnStartup(e);
        var args = Cli.Parse(e.Args);
        if (args.Perf) Perf.On = true;
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        var paths = new AppPaths(exeDir);
        try { Directory.CreateDirectory(paths.CacheDir); } catch { }
        AppLog.FilePath = Path.Combine(paths.CacheDir, "shelly.log");
        Perf.FilePath = Path.Combine(paths.CacheDir, "perf.log");
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => AppLog.Write("! fatal: " + ev.ExceptionObject);
        // A task nobody awaited (fire and forget) failed: logged instead of lost (1.3.1)
        TaskScheduler.UnobservedTaskException += (_, ev) => { AppLog.Error("task", ev.Exception); ev.SetObserved(); };

        // A scheduled run (Task Scheduler) or --run --headless: handed to the running Shelly of this folder, else run here
        // without any window (the Shelly started later is the normal one)
        if (args.IsUnattended)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(async () =>
            {
                int code = 0;
                try
                {
                    bool handed = false;
                    // A Shelly that is just starting listens a moment later
                    for (int i = 0; i < 10 && !handed && SingleInstance.IsRunning(paths.Root); i++)
                    {
                        handed = SingleInstance.SendTo(paths.Root, args.ToMessage());
                        if (!handed) await Task.Delay(2000);
                    }
                    if (handed) AppLog.Write("Unattended run handed to the running Shelly: " + args.ToMessage().Replace('\t', ' '));
                    else code = await Unattended.RunHeadlessAsync(paths, args);
                }
                catch (Exception ex) { AppLog.Error("unattended", ex); code = 1; }
                Shutdown(code);
            });
            return;
        }

        // One Shelly per folder: a second start hands its arguments to the first one
        single = new SingleInstance(paths.Root, args.Restarted);
        if (!single.IsFirst)
        {
            single.Send(args.ToMessage());
            Shutdown();
            return;
        }

        paths.EnsureFolders();
        Perf.Mark("single instance + folders");
        var settings = Settings.Load(paths.Settings);
        if (settings.Unknown.TryGetValue("perfLog", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.True) Perf.On = true;
        Perf.Mark("settings");
        if (!settings.LanguageSaved) settings.Language = Loc.SystemLanguage();   // first start: the system language; a saved choice wins
        Loc.SetLanguage(settings.Language);
        Loc.Language = settings.Language == "ru" ? "ru" : "en";
        ThemeManager.Apply(settings);
        FocusRing.Init();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Shelly;component/Styles.xaml") });
        ThemeManager.UpdateTexts();
        Perf.Mark("theme + styles");

        // One-time disclaimer: Shelly runs scripts with your rights and is not a sandbox
        if (string.IsNullOrEmpty(settings.DisclaimerAccepted))
        {
            if (!DisclaimerDialog.Show(true))
            {
                try { single.Dispose(); } catch { }
                Shutdown();
                return;
            }
            settings.DisclaimerAccepted = AppState.Version;
        }

        settings.LastVersion = AppState.Version;
        S = new AppState(paths, settings);
        Perf.Mark("state (history, engines)");
        // Scheduled runs made while Shelly was closed
        S.SyncLastRunsFromHistory();
        RunManager.CleanDoneFiles(paths);
        S.RefreshScripts();
        Perf.Mark($"scan ({S.Scripts.Count} scripts)");
        // A settings file that could not be read stays as it is until the user changes something (a copy was kept)
        if (!settings.LoadFailed) S.SaveSettingsNow();
        else AppLog.Write("Settings file could not be read; defaults are used and the file is left as it is" + (settings.CorruptCopy != null ? ", a copy: " + settings.CorruptCopy : ""));

        Win = new MainWindow();
        Perf.Mark("main window built");
        MainWindow = Win;
        Tray = new TrayService();
        Perf.Mark("tray");
        bool hidden = settings.StartMinimized || args.Minimized;
        if (Perf.On) Win.ContentRendered += (_, _) => Perf.Mark("first frame");
        if (!hidden) Win.Show();
        else Win.InitHidden();
        Perf.Mark(hidden ? "hidden window handle" : "window shown");
        Hotkey = new HotkeyService(Win);
        Hotkey.Apply(settings.Hotkey, quiet: true);
        single.MessageReceived += msg => Dispatcher.BeginInvoke(() => HandleMessage(msg));
        single.Listen();
        S.StartWatching();
        JumpLists.Update();
        AppLog.Write($"Started {AppState.Version} in {startClock.ElapsedMilliseconds} ms, .NET {Environment.Version}");
        Perf.Mark("startup done");
        if (Perf.On)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                Perf.Mark("idle");
                Perf.Flush($"scripts {S.Scripts.Count}, history {S.History.Entries.Count}");
            });
        if (args.Run != null) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => RunByRel(args.Run, "jumplist", args.Preset));
        if (args.History != null) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => ShowHistoryEntry(args.History));
        Sched = new SchedulerService(S);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            await Task.Delay(4000);
            await Updates.CheckAsync(false);
            // Schedules against Task Scheduler: missing or outdated tasks and leftover ones are shown in Settings > Schedules
            await Sched.RefreshAsync();
            if (Sched.Attention > 0) Status(Loc.F("schedAttention", Sched.Attention), true);
        });
    }

    void HandleMessage(string msg)
    {
        // "hist": a headless scheduled run finished and wrote the history
        if (msg.StartsWith("hist\t"))
        {
            if (S.History.MergeFromDisk() > 0) HistoryChanged();
            if (S.SyncLastRunsFromHistory()) S.NotifyScriptsChanged();
            return;
        }
        var a = Cli.FromMessage(msg);
        if (a.Scheduled != null)
        {
            var s = ScheduleList.Get(S.Settings, a.Scheduled);
            if (s == null) AppLog.Write("Scheduled task " + a.Scheduled + ": no such schedule in the settings (a leftover task)");
            else if (s.Enabled) _ = S.Runs.RunUnattendedAsync(s.Rel, s.Preset, s.Window);
        }
        else if (a.Run != null && a.Headless) _ = S.Runs.RunUnattendedAsync(a.Run, a.Preset ?? "", false);
        else if (a.Run != null) RunByRel(a.Run, "jumplist", a.Preset);
        else if (a.History != null) ShowHistoryEntry(a.History);
        else ShowMain();
    }

    /// <summary>A click on the notification of a headless run: Shelly at that run in History.</summary>
    static void ShowHistoryEntry(string id)
    {
        S.History.MergeFromDisk();
        ShowMain();
        var e = S.History.Get(id);
        if (e != null) Win?.ShowHistory(e);
        else Win?.Navigate("history");
    }

    public static void RunByRel(string rel, string origin, string? preset = null)
    {
        rel = rel.Trim().Trim('"').Replace('/', '\\');
        var f = S.Find(rel);
        if (f == null) { S.RefreshScripts(); f = S.Find(rel); }
        if (f == null) { Status(Loc.F("singleArgUnknown", rel), true); ShowMain(); return; }
        _ = S.Runs.RunAsync(f, new RunOptions { Origin = origin, Mode = origin == "tray" ? "window" : null, Preset = preset });
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("ui", e.Exception);
        Status(Loc.F("unexpected", e.Exception.Message), true);
        e.Handled = true;
    }

    // ---------- Helpers for the windows ----------

    public static void Status(string text, bool important = false) => StatusChanged?.Invoke(text, important);

    /// <summary>An exception of an action nobody waits for: logged and shown in the status bar (1.3.1).</summary>
    public static void Fail(string where, Exception ex)
    {
        AppLog.Error(where, ex);
        Status(Loc.F("unexpected", ex.Message), true);
    }

    /// <summary>Starts an async UI action without waiting for it (a click handler); an exception goes to <see cref="Fail"/>.</summary>
    public static async void Fire(Func<Task> work, string where)
    {
        try { await work(); }
        catch (Exception ex) { Fail(where, ex); }
    }
    public static void HistoryChanged() => HistoryUpdated?.Invoke();

    public static Window? OwnerWindow()
    {
        foreach (Window w in Current.Windows) if (w.IsActive && w.IsVisible && w is not DialogWindow) return w;
        return Win != null && Win.IsVisible ? Win : null;
    }

    public static void ShowMain() => Win?.ShowAndActivate();

    public static void Toast(string title, string text, bool ok, Action? onClick) => Tray?.Balloon(title, text, ok, onClick);

    public static void OpenEditor(string path, int line = 0, bool forceBuiltin = false)
    {
        if (!File.Exists(path)) { S.RefreshScripts(); Status(Loc.F("fileMissing", Path.GetFileName(path)), true); return; }
        if (!forceBuiltin && S.Settings.DefaultEditor == "external")
        {
            ShellOps.ExternalEditor(path, line);
            return;
        }
        try { EditorWindow.Open(path, line); }
        catch (Exception ex) { Status(Loc.F("editorFail", ex.Message), true); }
    }

    public static void InstallPwsh() => PwshInstall.Show();

    public static void Quit()
    {
        if (S.Runs.Active.Any(r => r.IsInline))
        {
            var names = string.Join(", ", S.Runs.Active.Where(r => r.IsInline).Select(r => r.Script.Name).Distinct());
            ShowMain();
            if (!Dlg.Confirm(Loc.T("exitRunningTitle"), Loc.T("exitRunningTitle"), Loc.F("exitRunningText", names), Loc.T("exitStop"), true)) return;
            S.Runs.StopAll();
        }
        foreach (Window w in Current.Windows.Cast<Window>().ToList())
            if (w is EditorWindow ed && !ed.ConfirmClose()) return;
        Exiting = true;
        try { S.Shutdown(); } catch { }
        try { Hotkey?.Dispose(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { single?.Dispose(); } catch { }
        Current.Shutdown();
    }
}
