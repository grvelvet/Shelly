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
    public static event Action<string, bool>? StatusChanged;
    public static event Action? HistoryUpdated;
    static SingleInstance? single;
    public static bool Exiting { get; private set; }
    readonly Stopwatch startClock = Stopwatch.StartNew();

    protected override void OnStartup(StartupEventArgs e)
    {
        Perf.Begin();
        base.OnStartup(e);
        var args = CommandLine.Parse(e.Args);
        if (args.Perf) Perf.On = true;
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        var paths = AppPaths.FromExeFolder(exeDir);
        try { Directory.CreateDirectory(paths.CacheDir); } catch { }
        AppLog.FilePath = Path.Combine(paths.CacheDir, "shelly.log");
        Perf.FilePath = Path.Combine(paths.CacheDir, "perf.log");
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => AppLog.Write("! fatal: " + ev.ExceptionObject);

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

        bool firstNet = settings.MigratedFromPs || string.IsNullOrEmpty(settings.LastVersion);
        settings.LastVersion = AppState.Version;
        S = new AppState(paths, settings);
        Perf.Mark("state (history, engines)");
        S.RefreshScripts();
        Perf.Mark($"scan ({S.Scripts.Count} scripts)");
        S.SaveSettingsNow();
        if (firstNet && settings.MigratedFromPs) AppLog.Write("Settings of the PowerShell edition taken over: " + paths.Settings);

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
        if (args.Run != null) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => RunByRel(args.Run, "jumplist"));
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            await Task.Delay(4000);
            await Updates.CheckAsync(false);
        });
    }

    void HandleMessage(string msg)
    {
        var a = CommandLine.FromMessage(msg);
        if (a.Run != null) RunByRel(a.Run, "jumplist");
        else ShowMain();
    }

    public static void RunByRel(string rel, string origin)
    {
        rel = rel.Trim().Trim('"').Replace('/', '\\');
        var f = S.Find(rel);
        if (f == null) { S.RefreshScripts(); f = S.Find(rel); }
        if (f == null) { Status(Loc.F("singleArgUnknown", rel), true); ShowMain(); return; }
        _ = S.Runs.RunAsync(f, new RunOptions { Origin = origin, Mode = origin == "tray" ? "window" : null });
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("ui", e.Exception);
        Status(Loc.F("unexpected", e.Exception.Message), true);
        e.Handled = true;
    }

    // ---------- Helpers for the windows ----------

    public static void Status(string text, bool important = false) => StatusChanged?.Invoke(text, important);
    public static void HistoryChanged() => HistoryUpdated?.Invoke();

    public static Window? OwnerWindow()
    {
        foreach (Window w in Current.Windows) if (w.IsActive && w.IsVisible && w is not DialogWindow) return w;
        return Win != null && Win.IsVisible ? Win : null;
    }

    public static bool IsForeground()
    {
        foreach (Window w in Current.Windows) if (w.IsActive) return true;
        return false;
    }

    public static void ShowMain() => Win?.ShowAndActivate();

    public static void Toast(string title, string text, bool ok, Action? onClick) => Tray?.Balloon(title, text, ok, onClick);

    public static void OpenEditor(string path, int line = 0)
    {
        if (!File.Exists(path)) { S.RefreshScripts(); Status(Loc.F("fileMissing", Path.GetFileName(path)), true); return; }
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

/// <summary>Command line: --run "Folder\Script.ps1" (jump list), --minimized (autostart), --show.</summary>
public sealed class CommandLine
{
    public string? Run;
    public bool Minimized;
    public bool Restarted;
    public bool Perf;

    public static CommandLine Parse(string[] args)
    {
        var c = new CommandLine();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if ((a == "--run" || a == "-run" || a == "/run") && i + 1 < args.Length) c.Run = args[++i];
            else if (a == "--restarted") c.Restarted = true;
            else if (a == "--perf") c.Perf = true;
            else if (a == "--minimized" || a == "-minimized" || a == "/minimized" || a == "--tray") c.Minimized = true;
        }
        return c;
    }

    public string ToMessage() => Run != null ? "run\t" + Run : "show";
    public static CommandLine FromMessage(string m) => m.StartsWith("run\t") ? new CommandLine { Run = m.Substring(4) } : new CommandLine();
}
