namespace Shelly.Core;

/// <summary>Everything Shelly shows and checks for a script, for the engine it will run on.</summary>
public sealed class ScriptMeta
{
    public string Desc = "";
    public List<ParamInfo> Params = new();
    public List<DangerFinding> Danger = new();
    public List<ScriptIssue> Issues = new();
    public EngineInfo Engine = new();
    public RequiresInfo Req = new();
    /// <summary>An engine has not reported yet (parameters and safety findings may change).</summary>
    public bool Pending;
    /// <summary>No PowerShell result at all: only the built-in parser and scanner were used.</summary>
    public bool FallbackOnly;
    /// <summary>The safety scan: "ok" (PowerShell's parser checked the script), "checking" (it has not reported yet;
    /// Danger holds the built-in scanner's preliminary findings) or "unverified" (no PowerShell could check it).
    /// Only "ok" can be shown as "nothing found".</summary>
    public string Scan = "ok";
    public bool Verified => Scan == "ok";
    /// <summary>Mark of the Web of the file (downloaded from the internet), or null.</summary>
    public ZoneInfo? Zone;
    public string DangerLevel => Safety.Level(Danger);
}

/// <summary>Analyzes scripts with every installed engine (Windows PowerShell 5.1 and PowerShell 7) in the background,
/// one process per engine and batch, and caches the results by path + file time on disk.</summary>
public sealed class AnalysisService : IDisposable
{
    public sealed class Engines
    {
        public string? Ps51;
        public string? Pwsh;
        public string? Exe(string engine) => engine == "7" ? Pwsh : Ps51;
        public IEnumerable<string> Available() { if (Ps51 != null) yield return "5.1"; if (Pwsh != null) yield return "7"; }
    }

    readonly AnalysisCache cache;
    readonly string tempDir;
    readonly object gate = new();
    readonly Dictionary<string, (string Path, string Ticks)> pending = new(StringComparer.OrdinalIgnoreCase);   // key path|ticks|engine
    readonly HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AnalysisRecord> failed = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AnalysisRecord> fallback = new(StringComparer.OrdinalIgnoreCase);
    readonly CancellationTokenSource cts = new();
    readonly SemaphoreSlim[] slots;
    public Engines Eng { get; private set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);
    /// <summary>Raised (on a worker thread) when new results arrived.</summary>
    public event Action? Updated;

    public AnalysisService(Engines engines, string cacheFile, string tempDir)
    {
        Eng = engines;
        cache = new AnalysisCache(cacheFile);
        cache.Load();
        this.tempDir = tempDir;
        slots = new[] { new SemaphoreSlim(1, 1), new SemaphoreSlim(1, 1) };
    }

    public void SetEngines(Engines engines)
    {
        lock (gate) { Eng = engines; failed.Clear(); }
    }

    public AnalysisRecord? Get(ScriptFile f, string engine)
    {
        var r = cache.Get(f.Path, f.Ticks, engine);
        if (r != null) return r;
        lock (gate) return failed.TryGetValue(AnalysisCache.Key(f.Path, f.Ticks, engine), out var x) ? x : null;
    }

    public bool IsPending(ScriptFile f)
    {
        foreach (var e in Eng.Available())
            if (Get(f, e) == null) return true;
        return false;
    }

    AnalysisRecord Fallback(ScriptFile f)
    {
        string k = f.Path + "|" + f.Ticks;
        lock (gate)
        {
            if (fallback.TryGetValue(k, out var r)) return r;
        }
        var rec = FallbackParser.ParseFile(f.Path);
        rec.Ticks = f.Ticks;
        lock (gate) fallback[k] = rec;
        return rec;
    }

    /// <summary>Queues every file that has no result for an installed engine and starts the batches.</summary>
    public void Request(IEnumerable<ScriptFile> files)
    {
        bool any = false;
        lock (gate)
        {
            foreach (var f in files)
                foreach (var e in Eng.Available())
                {
                    string k = AnalysisCache.Key(f.Path, f.Ticks, e);
                    if (cache.Get(f.Path, f.Ticks, e) != null || failed.ContainsKey(k) || running.Contains(k) || pending.ContainsKey(k)) continue;
                    pending[k] = (f.Path, f.Ticks);
                    any = true;
                }
        }
        if (any) foreach (var e in Eng.Available()) _ = PumpAsync(e);
    }

    async Task PumpAsync(string engine)
    {
        var slot = slots[engine == "7" ? 1 : 0];
        if (!await slot.WaitAsync(0)) return;   // a batch of this engine is running; it pumps again when done
        try
        {
            while (!cts.IsCancellationRequested)
            {
                List<KeyValuePair<string, (string Path, string Ticks)>> batch;
                string? exe;
                lock (gate)
                {
                    exe = Eng.Exe(engine);
                    batch = pending.Where(p => p.Key.EndsWith("|" + engine)).Take(200).ToList();
                    foreach (var p in batch) { pending.Remove(p.Key); running.Add(p.Key); }
                }
                if (batch.Count == 0 || exe == null) break;
                List<AnalysisRecord> res = new();
                try { res = await AnalyzerRunner.RunAsync(exe, batch.Select(b => b.Value.Path).Distinct().ToList(), tempDir, Timeout, cts.Token); }
                catch { }
                lock (gate)
                {
                    foreach (var r in res) { r.Engine = engine; cache.Put(r); }
                    foreach (var b in batch)
                    {
                        running.Remove(b.Key);
                        // Not reported (process failed or file changed meanwhile): marked, so it is not parsed again in a loop
                        if (cache.Get(b.Value.Path, b.Value.Ticks, engine) == null)
                            failed[b.Key] = new AnalysisRecord { Path = b.Value.Path, Ticks = b.Value.Ticks, Engine = engine, Failed = true };
                    }
                }
                Updated?.Invoke();
            }
        }
        finally { slot.Release(); }
        bool more;
        lock (gate) more = pending.Keys.Any(k => k.EndsWith("|" + engine));
        if (more && !cts.IsCancellationRequested) _ = PumpAsync(engine);
    }

    /// <summary>Waits until every installed engine reported for this file (or the timeout).</summary>
    public async Task<bool> WaitAsync(ScriptFile f, TimeSpan timeout)
    {
        Request(new[] { f });
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (!IsPending(f)) return true;
            await Task.Delay(100);
        }
        return !IsPending(f);
    }

    public void SaveCache(Func<string, bool>? keep = null) => cache.Save(keep);
    public void ClearCache() { cache.Clear(); lock (gate) { failed.Clear(); fallback.Clear(); } }

    /// <summary>Description, parameters, safety findings, engine and issues for the engine the script runs on.</summary>
    public ScriptMeta Meta(ScriptFile f, Settings s, bool pwshOk, bool ps51Ok, string? choiceOverride = null)
    {
        var r51 = Get(f, "5.1");
        var r7 = Get(f, "7");
        if (r51 != null && r51.Failed) r51 = null;
        if (r7 != null && r7.Failed) r7 = null;
        var any = r51 ?? r7;
        var fb = any == null ? Fallback(f) : null;
        var bas = any ?? fb!;
        string choice = choiceOverride ?? s.GetEngineChoice(f.Rel);
        var ei = EngineResolver.Resolve(choice, s.EngineDefault, pwshOk, bas.Req,
            (r51?.Ps7 ?? new()).Concat(r7?.Ps7 ?? new()), (r51?.WinPs ?? new()).Concat(r7?.WinPs ?? new()), r51?.Errors, r7?.Errors);
        ei.Errors = ei.Engine == "7" ? r7?.Errors : r51?.Errors;
        ei.Pending = IsPending(f);
        var own = ei.Engine == "7" ? (r7 ?? r51) : (r51 ?? r7);
        var m = new ScriptMeta
        {
            Desc = bas.Desc,
            Params = own?.Params ?? fb?.Params ?? new(),
            // Until PowerShell's parser reports, the built-in scanner's findings stand in (shown as "checking")
            Danger = Safety.Sorted(Safety.ApplySources(own?.Danger ?? fb?.Danger ?? new(), s)),
            Scan = own != null ? "ok" : ei.Pending ? "checking" : "unverified",
            Zone = MotW.Read(f.Path),
            Engine = ei,
            Req = bas.Req,
            Pending = ei.Pending,
            FallbackOnly = any == null
        };
        bool installed = ei.Engine == "7" ? pwshOk : ps51Ok;
        m.Issues = ScriptIssue.ForEngine(ei, bas.Req, installed);
        if (bas.Req.Elevation) m.Issues.Add(new ScriptIssue { Kind = "admin", Level = "info" });
        var missing = (own ?? bas).MissingModules;
        if (missing.Count > 0) m.Issues.Add(new ScriptIssue { Kind = "module", Level = "warn", Data = string.Join(", ", missing) });
        return m;
    }

    /// <summary>The engine and exe a script runs with: the resolved one, else the other one when that is not installed.</summary>
    public static (string Engine, string Exe, bool Fallback) RunEngine(EngineInfo ei, Engines eng)
    {
        string e = ei.Engine;
        string? exe = eng.Exe(e);
        if (exe != null) return (e, exe, false);
        string o = e == "7" ? "5.1" : "7";
        string? x = eng.Exe(o);
        if (x != null) return (o, x, true);
        return ("5.1", "powershell.exe", false);
    }

    public void Dispose() { cts.Cancel(); }
}
