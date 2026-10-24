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
    /// Danger holds the built-in scanner's preliminary findings), "unverified" (no PowerShell could check it) or "none"
    /// (a Python script: the PowerShell scan does not apply), "basic" (a batch file: the light built-in check of its
    /// commands, 1.3.0) or "custom" (a custom interpreter's script: not checked). Only "ok" can be shown as "nothing found".</summary>
    public string Scan = "ok";
    public bool Verified => Scan == "ok";
    /// <summary>Mark of the Web of the file (downloaded from the internet), or null.</summary>
    public ZoneInfo? Zone;
    public string DangerLevel => Safety.Level(Danger);
    /// <summary>A Python script (no PowerShell analysis, engine "py").</summary>
    internal bool IsPython => Engine.Engine == EngineInfo.Python;
}

/// <summary>The note a dialog shows about the safety check of a script (<see cref="ScriptMeta.Scan"/>, 1.3.0).</summary>
public static class ScanNotes
{
    /// <summary>The Loc key, or null for "ok" (<paramref name="clean"/>: the key to use for "ok", when there is one).</summary>
    public static string? Key(string scan, string? clean = null) => scan switch
    {
        "ok" => clean,
        "checking" => "dzChecking",
        "none" => "pyNoScan",
        "basic" => "batScanNote",
        "custom" => "customNoScan",
        _ => "dzUnverified"
    };
}

/// <summary>Analyzes scripts with every installed engine (Windows PowerShell 5.1 and PowerShell 7) in the background,
/// one process per engine and batch, and caches the results by path + content hash (SHA-256) on disk. The analyzer reads a
/// private copy of the exact bytes that were hashed, so a verdict always belongs to the content it is stored under.</summary>
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
    readonly Dictionary<string, (string Path, string Hash)> pending = new(StringComparer.OrdinalIgnoreCase);   // key path|hash|engine
    readonly HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AnalysisRecord> failed = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AnalysisRecord> fallback = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> pythonDesc = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (string Desc, List<DangerFinding> Danger, bool Bom)> otherMeta = new(StringComparer.OrdinalIgnoreCase);
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
        var r = cache.Get(f.Path, f.Hash, engine);
        if (r != null) return r;
        lock (gate) return failed.TryGetValue(AnalysisCache.Key(f.Path, f.Hash, engine), out var x) ? x : null;
    }

    public bool IsPending(ScriptFile f)
    {
        if (!f.IsPowerShell) return false;
        foreach (var e in Eng.Available())
            if (Get(f, e) == null) return true;
        return false;
    }

    AnalysisRecord Fallback(ScriptFile f)
    {
        string k = f.Path + "|" + f.Hash;
        lock (gate)
        {
            if (fallback.TryGetValue(k, out var r)) return r;
        }
        var rec = FallbackParser.ParseFile(f.Path);
        rec.Ticks = f.Hash;
        lock (gate) fallback[k] = rec;
        return rec;
    }

    /// <summary>Queues every file that has no result for an installed engine and starts the batches.</summary>
    public void Request(IEnumerable<ScriptFile> files)
    {
        bool any = false;
        lock (gate)
        {
            foreach (var f in files.Where(f => f.IsPowerShell))
                foreach (var e in Eng.Available())
                {
                    string k = AnalysisCache.Key(f.Path, f.Hash, e);
                    if (cache.Get(f.Path, f.Hash, e) != null || failed.ContainsKey(k) || running.Contains(k) || pending.ContainsKey(k)) continue;
                    pending[k] = (f.Path, f.Hash);
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
                List<KeyValuePair<string, (string Path, string Hash)>> batch;
                string? exe;
                lock (gate)
                {
                    exe = Eng.Exe(engine);
                    batch = pending.Where(p => p.Key.EndsWith("|" + engine)).Take(200).ToList();
                    foreach (var p in batch) { pending.Remove(p.Key); running.Add(p.Key); }
                }
                if (batch.Count == 0 || exe == null) break;
                List<AnalysisRecord> res = new();
                // The analyzer gets a copy of the bytes that were hashed (a file that no longer matches its hash is skipped)
                var copies = new Dictionary<string, (string Path, string Hash)>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var b in batch)
                    {
                        string? snap = Snapshot(b.Value.Path, b.Value.Hash);
                        if (snap != null) copies[snap] = b.Value;
                    }
                    if (copies.Count > 0) res = await AnalyzerRunner.RunAsync(exe, copies.Keys.ToList(), tempDir, Timeout, cts.Token);
                }
                catch { }
                finally { foreach (var c in copies.Keys) { try { File.Delete(c); } catch { } } }
                lock (gate)
                {
                    foreach (var r in res)
                    {
                        if (!copies.TryGetValue(r.Path, out var orig)) continue;
                        r.Path = orig.Path; r.Ticks = orig.Hash; r.Engine = engine;
                        cache.Put(r);
                    }
                    foreach (var b in batch)
                    {
                        running.Remove(b.Key);
                        // Not reported (process failed or the file changed since it was hashed): marked, so it is not parsed again in a loop
                        if (cache.Get(b.Value.Path, b.Value.Hash, engine) == null)
                            failed[b.Key] = new AnalysisRecord { Path = b.Value.Path, Ticks = b.Value.Hash, Engine = engine, Failed = true };
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

    /// <summary>Copies the file into the working folder and returns the copy, or null when its content is not the one with this hash
    /// (it changed after the scan) or it cannot be read. The hash is taken from the same bytes that are written.</summary>
    string? Snapshot(string path, string hash)
    {
        try
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }
            if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase)) return null;
            Directory.CreateDirectory(tempDir);
            string copy = Path.Combine(tempDir, "snap-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllBytes(copy, bytes);
            return copy;
        }
        catch { return null; }
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

    /// <summary>Description, parameters, safety findings, engine and issues for the engine the script runs on.</summary>
    public ScriptMeta Meta(ScriptFile f, Settings s, bool pwshOk, bool ps51Ok, string? choiceOverride = null)
    {
        if (f.IsPython) return PythonMeta(f);
        if (!f.IsPowerShell) return OtherMeta(f, s);
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

    /// <summary>A Python script: the description from its docstring or first comment (cached by path + hash), no parameters,
    /// no PowerShell findings (Scan "none"), the Mark of the Web of the file.</summary>
    ScriptMeta PythonMeta(ScriptFile f)
    {
        string k = f.Path + "|" + f.Hash;
        string? desc;
        lock (gate) pythonDesc.TryGetValue(k, out desc);
        if (desc == null)
        {
            try { desc = PythonRun.Description(TextFile.ReadAllText(f.Path)); } catch { desc = ""; }
            lock (gate) pythonDesc[k] = desc;
        }
        return new ScriptMeta
        {
            Desc = desc,
            Scan = "none",
            Zone = MotW.Read(f.Path),
            Engine = new EngineInfo { Engine = EngineInfo.Python, Auto = false, Reason = "" }
        };
    }

    /// <summary>A batch file or a custom interpreter's script (1.3.0): the description from its first comment, for a batch
    /// file the findings of <see cref="BatchScanner"/> (Scan "basic"; cached by path + hash), for a custom one nothing
    /// (Scan "custom"); issues: a UTF-8 BOM in a batch file, a custom program that is not there.</summary>
    ScriptMeta OtherMeta(ScriptFile f, Settings s)
    {
        string k = f.Path + "|" + f.Hash;
        bool bat = f.IsBatch;
        (string Desc, List<DangerFinding> Danger, bool Bom) v;
        bool have;
        lock (gate) have = otherMeta.TryGetValue(k, out v);
        if (!have)
        {
            try
            {
                var bytes = File.ReadAllBytes(f.Path);
                string text = bat ? BatchRun.Decode(bytes) : TextFile.Decode(bytes, out _);
                v = (CommentHeader.Description(text, f.Lang), bat ? BatchScanner.Scan(text) : new(), bat && BatchRun.HasBom(bytes));
            }
            catch { v = ("", new(), false); }
            lock (gate) otherMeta[k] = v;
        }
        var m = new ScriptMeta
        {
            Desc = v.Desc,
            Danger = Safety.Sorted(Safety.ApplySources(v.Danger, s)),
            Scan = bat ? "basic" : "custom",
            Zone = MotW.Read(f.Path),
            Engine = new EngineInfo { Engine = bat ? EngineInfo.Batch : EngineInfo.Custom, Auto = false, Reason = "" }
        };
        if (v.Bom) m.Issues.Add(new ScriptIssue { Kind = "batBom", Level = "warn" });
        if (!bat)
        {
            var ci = ScriptKinds.CustomFor(f.Path);
            if (ci == null || CustomInterpreters.ResolveProgram(ci.Program, File.Exists, PathDirs()) == null)
                m.Issues.Add(new ScriptIssue { Kind = "customMissing", Level = "error", Data = ci?.Program ?? "" });
        }
        return m;
    }

    /// <summary>The folders of PATH.</summary>
    public static IEnumerable<string> PathDirs() => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

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

    bool disposed;

    /// <summary>Stops the analysis; a batch in progress ends on the cancellation (its loop only reads the token inside a catch).</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cts.Cancel();
        cts.Dispose();
    }
}
