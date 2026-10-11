using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Shelly.Core;

namespace Shelly.Services;

/// <summary>Everything the windows share: folders, settings, scripts, engines, analysis, history and runs.</summary>
public sealed class AppState
{
    public AppPaths Paths { get; }
    public Settings Settings { get; }
    public HistoryStore History { get; }
    public AnalysisService Analysis { get; }
    public RunManager Runs { get; }
    public List<ScriptFile> Scripts { get; private set; } = new();
    public List<string> Folders { get; private set; } = new();
    public Dictionary<string, LastRun> LastRuns { get; }
    public string? Ps51 { get; private set; }
    public PwshInfo? Pwsh { get; private set; }
    public bool PwshOk => Pwsh != null;
    public bool Ps51Ok => Ps51 != null;
    public static string Version { get; } = typeof(AppState).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    /// <summary>Author profile linked from Settings > About.</summary>
    public const string AuthorUrl = "https://github.com/grvelvet";
    public static string Repo { get; } = typeof(AppState).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ShellyGitHubRepo")?.Value ?? "";

    /// <summary>Scripts list, favorites, icons or last runs changed.</summary>
    public event Action? ScriptsChanged;
    /// <summary>Analysis results arrived (descriptions, parameters, engines, safety).</summary>
    public event Action? MetaChanged;
    public event Action? EnginesChanged;
    public event Action? SettingsChanged;

    readonly DispatcherTimer saveTimer;
    readonly DispatcherTimer metaTimer;
    FileSystemWatcher? watcher;
    readonly DispatcherTimer watchTimer;

    public AppState(AppPaths paths, Settings settings)
    {
        Paths = paths;
        Settings = settings;
        LastRuns = LastRun.Import(settings);
        History = new HistoryStore(paths.HistoryDir) { Max = settings.HistoryMaxValue };
        try { History.Load(); } catch (Exception ex) { AppLog.Error("history", ex); }
        DetectEngines();
        Analysis = new AnalysisService(new AnalysisService.Engines { Ps51 = Ps51, Pwsh = Pwsh?.Path }, Path.Combine(paths.CacheDir, "analysis-cache.json"), paths.CacheDir);
        Analysis.Updated += () => Application.Current.Dispatcher.BeginInvoke(() => { metaTimer!.Stop(); metaTimer.Start(); });
        Runs = new RunManager(this);
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSettingsNow(); };
        metaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        metaTimer.Tick += (_, _) => { metaTimer.Stop(); MetaChanged?.Invoke(); };
        watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        watchTimer.Tick += (_, _) => { watchTimer.Stop(); RefreshScripts(); };
    }

    public void DetectEngines()
    {
        try { Ps51 = EngineLocator.FindPs51(); } catch { Ps51 = null; }
        try { Pwsh = EngineLocator.FindPwsh(); } catch { Pwsh = null; }
    }

    /// <summary>Look for PowerShell 7 again (after an install); true when something changed.</summary>
    public bool RecheckEngines()
    {
        string? before = Pwsh?.Path, before51 = Ps51;
        DetectEngines();
        if (before == Pwsh?.Path && before51 == Ps51) return false;
        Analysis.SetEngines(new AnalysisService.Engines { Ps51 = Ps51, Pwsh = Pwsh?.Path });
        Analysis.Request(Scripts);
        EnginesChanged?.Invoke();
        MetaChanged?.Invoke();
        return true;
    }

    public void RefreshScripts()
    {
        try
        {
            Paths.EnsureFolders();
            Scripts = ScriptCatalog.Scan(Paths);
            Folders = ScriptCatalog.Folders(Paths);
        }
        catch (Exception ex) { AppLog.Error("scan", ex); }
        Analysis.Request(Scripts);
        ScriptsChanged?.Invoke();
    }

    public void StartWatching()
    {
        try
        {
            watcher = new FileSystemWatcher(Paths.Scripts) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            FileSystemEventHandler h = (_, _) => Application.Current.Dispatcher.BeginInvoke(() => { watchTimer.Stop(); watchTimer.Start(); });
            watcher.Created += h; watcher.Deleted += h; watcher.Changed += h;
            watcher.Renamed += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => { watchTimer.Stop(); watchTimer.Start(); });
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { AppLog.Error("watch", ex); }
    }

    public ScriptFile? Find(string rel) => Scripts.FirstOrDefault(s => string.Equals(s.Rel, rel, StringComparison.OrdinalIgnoreCase));

    public ScriptMeta Meta(ScriptFile f, string? choiceOverride = null) => Analysis.Meta(f, Settings, PwshOk, Ps51Ok, choiceOverride);

    public LastRun? LastRunOf(string rel) => LastRuns.TryGetValue(rel, out var r) ? r : null;

    public void SetLastRun(LastRun r)
    {
        LastRun.Set(Settings, LastRuns, r);
        SaveSettings();
    }

    /// <summary>A script was renamed (newRel) or deleted (null): its last run follows.</summary>
    public void MoveLastRun(string oldRel, string? newRel)
    {
        if (LastRuns.Remove(oldRel, out var r) && newRel != null) { r.Rel = newRel; LastRuns[newRel] = r; }
        Settings.LastRuns = LastRuns.Values.OrderByDescending(x => x.Time).Take(200).Select(x => x.ToLine()).ToList();
    }

    public void ReloadLastRuns()
    {
        LastRuns.Clear();
        foreach (var kv in LastRun.Import(Settings)) LastRuns[kv.Key] = kv.Value;
    }

    /// <summary>Save soon (several changes in a row are written once).</summary>
    public void SaveSettings()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void SaveSettingsNow()
    {
        saveTimer.Stop();
        try { Settings.Save(Paths.Settings); }
        catch (Exception ex) { App.Status(Loc.F("settingsFail", ex.Message), true); }
    }

    public void NotifySettingsChanged() { SaveSettings(); SettingsChanged?.Invoke(); }
    public void NotifyScriptsChanged() => ScriptsChanged?.Invoke();

    public string? IconPath(string rel)
    {
        string? ic = Settings.GetIcon(rel);
        if (string.IsNullOrEmpty(ic)) return null;
        string p = Path.IsPathRooted(ic) ? ic : Path.Combine(Paths.Root, ic);
        return File.Exists(p) ? p : null;
    }

    public void Shutdown()
    {
        try { watcher?.Dispose(); } catch { }
        SaveSettingsNow();
        try { Analysis.SaveCache(p => File.Exists(p)); } catch { }
        try { History.Save(); } catch { }
        Analysis.Dispose();
    }
}
