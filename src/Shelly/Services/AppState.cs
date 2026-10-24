using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Shelly.Core;

namespace Shelly.Services;

/// <summary>Everything the windows share: folders, settings, scripts, engines, analysis, history and runs.</summary>
public sealed class AppState : IDisposable
{
    public AppPaths Paths { get; }
    public Settings Settings { get; }
    public HistoryStore History { get; }
    public AnalysisService Analysis { get; }
    public RunManager Runs { get; }
    public List<ScriptFile> Scripts { get; private set; } = new();
    public List<string> Folders { get; private set; } = new();
    /// <summary>The external script folders in use (existing, no overlap) and their groups on the Scripts page.</summary>
    public List<string> ExternalRoots { get; private set; } = new();
    public List<ExternalScripts.Group> ExternalGroups { get; private set; } = new();
    public Dictionary<string, LastRun> LastRuns { get; }
    public string? Ps51 { get; private set; }
    public PwshInfo? Pwsh { get; private set; }
    public bool PwshOk => Pwsh != null;
    public bool Ps51Ok => Ps51 != null;
    /// <summary>The Python 3 interpreter for .py scripts (null: not found, or not looked for yet).</summary>
    public PythonInfo? Python { get; private set; }
    /// <summary>Why <see cref="Python"/> is null after a search: pyBadPath (the path in Settings) or pyNotFound.</summary>
    public string PythonError { get; private set; } = "";
    /// <summary>A search has finished at least once.</summary>
    public bool PythonChecked { get; private set; }
    /// <summary>A search is running. A flag, not the task state: the task is still running while its result is handed out
    /// (PythonChanged), and the page built then must already show the result.</summary>
    public bool PythonSearching { get; private set; }
    Task<PythonInfo?>? pythonTask;
    string? pythonTaskPath;   // the pythonPath the running search was started with
    int pythonGen;            // only the newest search may set the result
    public static string Version { get; } = typeof(AppState).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    /// <summary>Author profile linked from Settings > About.</summary>
    public static string Repo { get; } = typeof(AppState).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ShellyGitHubRepo")?.Value ?? "";

    /// <summary>Scripts list, favorites, icons or last runs changed.</summary>
    public event Action? ScriptsChanged;
    /// <summary>Analysis results arrived (descriptions, parameters, engines, safety).</summary>
    public event Action? MetaChanged;
    public event Action? EnginesChanged;
    public event Action? SettingsChanged;
    /// <summary>A Python search started or finished (Settings, script rows).</summary>
    public event Action? PythonChanged;

    readonly DispatcherTimer saveTimer;
    readonly DispatcherTimer metaTimer;
    FileSystemWatcher? watcher;
    readonly List<FileSystemWatcher> externalWatchers = new();
    readonly DispatcherTimer watchTimer;

    public AppState(AppPaths paths, Settings settings)
    {
        Paths = paths;
        Settings = settings;
        // Custom interpreters (Settings > Environment): which extensions are scripts, before the first scan
        ScriptKinds.SetCustom(CustomInterpreters.Load(settings));
        LastRuns = LastRun.Import(settings);
        History = new HistoryStore(paths.HistoryDir) { Max = settings.HistoryMaxValue };
        try { History.Load(); } catch (Exception ex) { AppLog.Error("history", ex); }
        if (History.RecoveredTo != null) AppLog.Write("history.json could not be read; the file and the run logs were moved to " + History.RecoveredTo);
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

    readonly Dictionary<string, PythonVenv?> venvCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The virtual environment of a .py script (null: none, or "Use .venv" is off). Cached per folder until the
    /// next scan, so the list can ask for every row.</summary>
    public PythonVenv? VenvFor(ScriptFile f, bool fresh = false)
    {
        if (!f.IsPython || !Settings.UseVenv) return null;
        string root = f.IsExternal ? f.ExternalRoot : Paths.Scripts;
        string key = (Path.GetDirectoryName(f.Path) ?? "") + "|" + root;
        if (!fresh && venvCache.TryGetValue(key, out var v)) return v;
        try { v = VenvLocator.Find(f.Path, root, SystemVenvFileSystem.Instance); }
        catch (Exception ex) { v = null; AppLog.Error("venv", ex); }
        venvCache[key] = v;
        return v;
    }

    public void RefreshScripts()
    {
        venvCache.Clear();
        try
        {
            Paths.EnsureFolders();
            ExternalRoots = ExternalScripts.Usable(Paths.Scripts, Settings.ExternalFolders);
            ExternalGroups = ExternalScripts.Groups(ExternalRoots);
            Scripts = ScriptCatalog.Scan(Paths, ExternalRoots);
            Folders = ScriptCatalog.Folders(Paths).Concat(ExternalScripts.FolderKeys(ExternalRoots, Scripts)).ToList();
        }
        catch (Exception ex) { AppLog.Error("scan", ex); }
        // Python is looked for (in the background) only once there is a .py script, or the path is set
        if (!PythonChecked && pythonTask == null && (Scripts.Any(s => s.IsPython) || Settings.PythonPath.Length > 0)) _ = FindPythonAsync();
        Analysis.Request(Scripts);
        ScriptsChanged?.Invoke();
    }

    /// <summary>Looks for Python in the background (the probe starts the interpreter, so never on the UI thread). A search
    /// that is already running is shared.</summary>
    public Task<PythonInfo?> FindPythonAsync()
    {
        string configured = Settings.PythonPath;
        // The same search is shared; a new path (Choose… / Reset during a search) starts a new one and the old result is dropped
        if (pythonTask is { IsCompleted: false } && pythonTaskPath == configured) return pythonTask;
        int gen = ++pythonGen;
        PythonSearching = true;
        var task = Task.Run(() =>
        {
            PythonInfo? py = null;
            string err;
            try { py = PythonLocator.Find(configured, SystemPythonEnvironment.Instance, out err); }
            catch (Exception ex) { err = "pyNotFound"; AppLog.Error("python", ex); }
            return (py, err);
        }).ContinueWith(t =>
        {
            var (py, err) = t.Result;
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (gen != pythonGen) return;
                    Python = py;
                    PythonError = err;
                    PythonChecked = true;
                    PythonSearching = false;
                    if (py != null) AppLog.Write("Python " + py.Version + ": " + py.Path + (py.Prefix.Length > 0 ? " " + string.Join(" ", py.Prefix) : "") + " (" + py.Source + ")");
                    PythonChanged?.Invoke();
                    ScriptsChanged?.Invoke();
                });
            }
            catch (Exception ex) { AppLog.Error("python", ex); }
            return py;
        }, TaskScheduler.Default);
        pythonTask = task;
        pythonTaskPath = configured;
        // Later, not from inside the caller (Settings rebuilds itself on this event)
        Application.Current?.Dispatcher.BeginInvoke(() => PythonChanged?.Invoke());
        return task;
    }

    /// <summary>The interpreter for a run: the one found before if it is still there, else a new search.</summary>
    public async Task<PythonInfo?> EnsurePythonAsync()
    {
        if (PythonSearching && pythonTask != null) return await FindPythonAsync();
        if (Python != null && EngineLocator.FileExists(Python.Path)) return Python;
        return await FindPythonAsync();
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
        WatchExternal();
    }

    /// <summary>The external folders are watched for script files only (.ps1, .py, .bat, .cmd and the custom interpreters' extensions): other files change all the time in a folder that
    /// holds the data a script works on. A new subfolder shows up with the next refresh.</summary>
    void WatchExternal()
    {
        foreach (var w in externalWatchers) { try { w.Dispose(); } catch { } }
        externalWatchers.Clear();
        foreach (var root in ExternalRoots)
        {
            try
            {
                var w = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                foreach (var ext in ScriptKinds.AllExtensions) w.Filters.Add("*" + ext);
                void Changed(object? s, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() => { watchTimer.Stop(); watchTimer.Start(); });
                w.Created += Changed; w.Deleted += Changed; w.Changed += Changed; w.Renamed += Changed;
                w.EnableRaisingEvents = true;
                externalWatchers.Add(w);
            }
            catch (Exception ex) { AppLog.Error("watch", ex); }
        }
    }

    /// <summary>A custom interpreter was added, changed or deleted: save, register the extensions, scan again and watch them.</summary>
    public void CustomInterpretersChanged()
    {
        ScriptKinds.SetCustom(CustomInterpreters.Load(Settings));
        SaveSettings();
        RefreshScripts();
        WatchExternal();
        NotifySettingsChanged();
    }

    /// <summary>The external folders were added or removed: save, scan again and watch the new set.</summary>
    public void ExternalFoldersChanged()
    {
        SaveSettings();
        RefreshScripts();
        WatchExternal();
    }

    /// <summary>The folder on disk behind a folder group of the Scripts page ("" = Scripts itself).</summary>
    public string FolderPath(string folderRel)
    {
        if (folderRel.Length == 0) return Paths.Scripts;
        return ExternalScripts.IsExternalRel(folderRel) ? ExternalScripts.Resolve(ExternalRoots, folderRel) ?? Paths.Scripts : Paths.FullFromRel(folderRel);
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
        Settings.LastRuns = LastRun.Lines(LastRuns.Values);
    }

    public void ReloadLastRuns()
    {
        LastRuns.Clear();
        foreach (var kv in LastRun.Import(Settings)) LastRuns[kv.Key] = kv.Value;
    }

    /// <summary>An unattended run (1.2.0, no window): it never writes the settings file, which belongs to the Shelly that
    /// may be started meanwhile; its result goes to the history only.</summary>
    public bool ReadOnlySettings { get; set; }

    /// <summary>Just the script list, for an unattended run: no folders created, no watchers, no Python search, no
    /// analysis of the other scripts.</summary>
    public void ScanOnly()
    {
        try
        {
            ExternalRoots = ExternalScripts.Usable(Paths.Scripts, Settings.ExternalFolders);
            Scripts = Directory.Exists(Paths.Scripts) || ExternalRoots.Count > 0 ? ScriptCatalog.Scan(Paths, ExternalRoots) : new();
        }
        catch (Exception ex) { AppLog.Error("scan", ex); }
    }

    /// <summary>Runs another Shelly process did (scheduled runs while this one was closed or busy) become the last run of
    /// their scripts, so the list shows them. true = something changed.</summary>
    public bool SyncLastRunsFromHistory()
    {
        bool changed = false;
        foreach (var e in History.Entries.Where(x => x.Origin == "schedule" && x.Kind is "ok" or "fail" or "closed").Reverse())
        {
            var cur = LastRunOf(e.Rel);
            if (cur != null && cur.Time >= e.Start.AddSeconds(-2)) continue;   // stored to the second
            LastRun.Set(Settings, LastRuns, new LastRun { Rel = e.Rel, Kind = e.Kind, Code = e.Code, Time = e.Start, Ms = e.Ms, Mode = e.Mode });
            changed = true;
        }
        if (changed) SaveSettings();
        return changed;
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
        if (ReadOnlySettings) return;
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
        StopWatching();
        SaveSettingsNow();
        try { Analysis.SaveCache(p => File.Exists(p)); } catch { }
        try { History.Save(); } catch { }
        Dispose();
    }

    void StopWatching()
    {
        try { watcher?.Dispose(); } catch { }
        watcher = null;
        foreach (var w in externalWatchers) { try { w.Dispose(); } catch { } }
        externalWatchers.Clear();
    }

    /// <summary>The watchers and the analysis (Shutdown saves first, an unattended run only disposes).</summary>
    public void Dispose()
    {
        StopWatching();
        Analysis.Dispose();
    }
}
