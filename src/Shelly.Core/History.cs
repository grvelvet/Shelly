using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>One run: script, engine, parameters, start, duration, exit code / result, mode and origin.</summary>
public sealed class RunEntry : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    string rel = "", name = "";
    public string Rel { get => rel; set { rel = value; On(); } }
    public string Name { get => name; set { name = value; On(); } }
    public string Engine { get; set; } = "5.1";
    public string Args { get; set; } = "";
    /// <summary>inline | window | admin</summary>
    public string Mode { get; set; } = "inline";
    /// <summary>list | tray | editor | rerun | jumplist | schedule (1.2.0)</summary>
    public string Origin { get; set; } = "list";
    /// <summary>The parameter preset the run used (1.2.0), "" for none.</summary>
    public string Preset { get; set; } = "";
    public DateTime Start { get; set; }
    long ms;
    public long Ms { get => ms; set { ms = value; On(); On(nameof(Line2)); } }
    public int Code { get; set; }
    string kind = "running";
    /// <summary>running | ok | fail | stopped | closed | started | interrupted</summary>
    public string Kind { get => kind; set { kind = value; On(); On(nameof(IsRunning)); On(nameof(Glyph)); On(nameof(Line2)); On(nameof(ResultText)); } }
    public bool Truncated { get; set; }
    public bool HasOutput { get; set; }
    public bool IsRunning => Kind == "running";

    public string Glyph => Kind switch
    {
        "ok" => "\uE73E",
        "fail" => "\uE711",
        "stopped" => "\uE71A",
        "running" => "\uE916",
        "interrupted" => "\uE7BA",
        _ => "\uE81C"
    };

    public string ResultText => RunResult.Format(Kind, Code).Text;
    public string Line2 => RunResult.FormatRunTime(Start) + (Kind == "running" || Kind == "started" ? "" : "  ·  " + RunResult.FormatDuration(Ms)) + "  ·  " + ResultText;
    public void Refresh() { On(nameof(Line2)); On(nameof(ResultText)); On(nameof(Glyph)); }

    public event PropertyChangedEventHandler? PropertyChanged;
    void On([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    public override string ToString() => Name;
}

/// <summary>launcher-history\history.json (newest first) and &lt;id&gt;.log files, UTF-8 with BOM.
/// The newest historyMax runs are kept; running entries are never dropped.</summary>
public sealed class HistoryStore
{
    public static readonly string[] Kinds = { "running", "ok", "fail", "stopped", "closed", "started", "interrupted" };
    public static readonly string[] Modes = { "inline", "window", "admin" };
    public static readonly string[] Origins = { "list", "tray", "editor", "rerun", "jumplist", "schedule" };

    public string Dir { get; }
    public string IndexPath => Path.Combine(Dir, "history.json");
    public List<RunEntry> Entries { get; } = new();
    public int Max { get; set; } = 100;
    public string? LastError { get; private set; }
    /// <summary>The folder the broken index and the output logs were moved to by the last Load (null when the index was fine).</summary>
    public string? RecoveredTo { get; private set; }
    // The index exists but was not read (and not moved aside): saving would overwrite the only copy of the history
    bool indexUnreadable;
    // Runs removed in this session (deleted, cleared, over the limit): a merge from the file must not bring them back
    readonly HashSet<string> removed = new();

    public HistoryStore(string dir) { Dir = dir; }

    public static string NewId() => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
    public string OutputPath(string id) => Path.Combine(Dir, id + ".log");

    public static string ToJsonLine(RunEntry e)
    {
        string Q(string v) => Settings.JsonString(v);
        return "{\"id\":" + Q(e.Id) + ",\"rel\":" + Q(e.Rel) + ",\"name\":" + Q(e.Name) + ",\"engine\":" + Q(e.Engine) +
               ",\"args\":" + Q(e.Args) + ",\"mode\":" + Q(e.Mode) + ",\"origin\":" + Q(e.Origin) +
               ",\"start\":" + Q(e.Start.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)) +
               ",\"ms\":" + e.Ms + ",\"code\":" + e.Code + ",\"kind\":" + Q(e.Kind) +
               ",\"truncated\":" + (e.Truncated ? "true" : "false") + ",\"output\":" + (e.HasOutput ? "true" : "false") +
               (e.Preset.Length > 0 ? ",\"preset\":" + Q(e.Preset) : "") + "}";
    }

    public static RunEntry? FromJson(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;
        string S(string n) => r.TryGetProperty(n, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => ""
        } : "";
        string id = S("id");
        if (!Regex.IsMatch(id, @"^[\w-]{1,64}$")) return null;
        string kind = S("kind");
        if (!Kinds.Contains(kind)) return null;
        if (!DateTime.TryParseExact(S("start"), "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;
        var e = new RunEntry { Id = id, Rel = S("rel"), Name = S("name") };
        if (e.Name.Length == 0) e.Name = Path.GetFileNameWithoutExtension(e.Rel.Replace('\\', '/'));
        // 1.3.0: Python, batch and custom runs keep their engine on reload (before, a "py" entry came back as 5.1)
        e.Engine = S("engine") is "7" or EngineInfo.Python or EngineInfo.Batch or EngineInfo.Custom ? S("engine") : "5.1";
        e.Args = S("args");
        e.Mode = Modes.Contains(S("mode")) ? S("mode") : "inline";
        e.Origin = Origins.Contains(S("origin")) ? S("origin") : "list";
        e.Start = t;
        e.Ms = Math.Max(0, long.TryParse(S("ms"), out var ms) ? ms : 0);
        e.Code = int.TryParse(S("code"), out var c) ? c : 0;
        e.Kind = kind;
        e.Truncated = S("truncated") == "True";
        e.HasOutput = S("output") == "True";
        e.Preset = S("preset");
        return e;
    }

    /// <summary>Reads the list; runs still running when Shelly quit become interrupted (inline) or started (window).</summary>
    public void Load()
    {
        lock (Entries)
        {
            Entries.Clear();
            removed.Clear();
        }
        RecoveredTo = null;
        bool changed = false, broken = false, unreadable = false;
        if (File.Exists(IndexPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(IndexPath).TrimStart('\uFEFF'), new JsonDocumentOptions { AllowTrailingCommas = true });
                var seen = new HashSet<string>();
                IEnumerable<JsonElement> items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : Array.Empty<JsonElement>();
                lock (Entries)
                {
                    foreach (var x in items)
                    {
                        var e = FromJson(x);
                        if (e == null || !seen.Add(e.Id)) continue;
                        if (e.Kind == "running") { e.Kind = e.Mode == "inline" ? "interrupted" : "started"; changed = true; }
                        if (e.HasOutput && !File.Exists(OutputPath(e.Id))) e.HasOutput = false;
                        Entries.Add(e);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable = true; }   // locked or no access: nothing is touched
            catch
            {
                // A broken index is not a reason to delete the output logs: the file and the logs are moved aside, not removed
                changed = true; broken = true;
            }
        }
        indexUnreadable = unreadable;
        lock (Entries)
        {
            var sorted = Entries.OrderByDescending(e => e.Start).ToList();
            Entries.Clear();
            Entries.AddRange(sorted);
        }
        if (unreadable) { LastError = "history.json could not be read"; return; }
        if (broken)
        {
            RecoveredTo = KeepBroken();
            if (RecoveredTo == null) { indexUnreadable = true; LastError = "history.json is broken and could not be moved aside"; return; }
        }
        if (Limit().Count > 0) changed = true;
        if (!broken) RemoveOrphanOutputs();
        if (changed && Directory.Exists(Dir)) Save();
    }

    /// <summary>Moves history.json and every output log into a "corrupt-yyyyMMdd-HHmmss" folder next to them (the files stay
    /// readable and can be copied back by hand). null when that failed.</summary>
    string? KeepBroken()
    {
        try
        {
            string q = Path.Combine(Dir, "corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(q);
            File.Move(IndexPath, Path.Combine(q, "history.json"), true);
            foreach (var f in Directory.EnumerateFiles(Dir, "*.log")) File.Move(f, Path.Combine(q, Path.GetFileName(f)), true);
            return q;
        }
        catch { return null; }
    }

    /// <summary>Adds the finished runs another Shelly process wrote to the file meanwhile (1.2.0: a scheduled run while this
    /// one was open, or this one is the scheduled run): new ids only, never runs removed here. Returns how many.</summary>
    public int MergeFromDisk()
    {
        if (indexUnreadable || !File.Exists(IndexPath)) return 0;
        int added = 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(IndexPath).TrimStart('\uFEFF'), new JsonDocumentOptions { AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
            lock (Entries)
            {
                var known = new HashSet<string>(Entries.Select(e => e.Id));
                foreach (var x in doc.RootElement.EnumerateArray())
                {
                    var e = FromJson(x);
                    if (e == null || e.Kind == "running" || known.Contains(e.Id) || removed.Contains(e.Id)) continue;
                    if (e.HasOutput && !File.Exists(OutputPath(e.Id))) e.HasOutput = false;
                    Entries.Add(e);
                    known.Add(e.Id);
                    added++;
                }
                if (added > 0)
                {
                    var sorted = Entries.OrderByDescending(e => e.Start).ToList();
                    Entries.Clear();
                    Entries.AddRange(sorted);
                }
            }
            if (added > 0) Limit();
        }
        catch { return 0; }   // being written or broken: the next save or merge tries again
        return added;
    }

    public bool Save()
    {
        if (indexUnreadable) return false;
        try
        {
            Directory.CreateDirectory(Dir);
            MergeFromDisk();
            List<string> lines;
            lock (Entries) lines = Entries.Select(e => "  " + ToJsonLine(e)).ToList();
            string json = lines.Count == 0 ? "[]\r\n" : "[\r\n" + string.Join(",\r\n", lines) + "\r\n]\r\n";
            AtomicFile.WriteAllText(IndexPath, json, new UTF8Encoding(true));
            LastError = null;
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    /// <summary>Keeps the newest Max finished entries; returns the removed ones.</summary>
    public List<RunEntry> Limit()
    {
        var drop = new List<RunEntry>();
        lock (Entries)
        {
            int keep = 0;
            foreach (var e in Entries)
            {
                if (e.Kind == "running") continue;
                if (++keep > Max) drop.Add(e);
            }
            foreach (var e in drop) { Entries.Remove(e); removed.Add(e.Id); }
        }
        foreach (var e in drop) RemoveFiles(e);
        return drop;
    }

    public void RemoveOrphanOutputs()
    {
        if (!Directory.Exists(Dir)) return;
        HashSet<string> ids;
        lock (Entries) ids = new HashSet<string>(Entries.Select(e => e.Id));
        foreach (var f in Directory.EnumerateFiles(Dir, "*.log"))
            if (!ids.Contains(Path.GetFileNameWithoutExtension(f))) { try { File.Delete(f); } catch { } }
    }

    public void RemoveFiles(RunEntry e) { try { File.Delete(OutputPath(e.Id)); } catch { } }

    public List<RunEntry> Add(RunEntry e)
    {
        lock (Entries) Entries.Insert(0, e);
        var dropped = Limit();
        Save();
        return dropped;
    }

    public bool Remove(RunEntry e)
    {
        if (e.Kind == "running") return false;
        RemoveFiles(e);
        lock (Entries)
        {
            Entries.Remove(e);
            removed.Add(e.Id);
        }
        Save();
        return true;
    }

    /// <summary>Deletes all finished runs; returns how many.</summary>
    public int Clear()
    {
        List<RunEntry> done;
        lock (Entries)
        {
            done = Entries.Where(e => e.Kind != "running").ToList();
            foreach (var e in done) { Entries.Remove(e); removed.Add(e.Id); }
        }
        foreach (var e in done) RemoveFiles(e);
        RemoveOrphanOutputs();
        Save();
        return done.Count;
    }

    public void SaveOutput(RunEntry e, string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            AtomicFile.WriteAllText(OutputPath(e.Id), text, new UTF8Encoding(true));
            e.HasOutput = true;
        }
        catch (Exception ex) { e.HasOutput = false; LastError = ex.Message; }
    }

    public string ReadOutput(RunEntry e)
    {
        if (!e.HasOutput) return "";
        try { return File.ReadAllText(OutputPath(e.Id)); } catch { return ""; }
    }

    public RunEntry? Get(string id) { lock (Entries) return Entries.FirstOrDefault(e => e.Id == id); }
    public RunEntry? LastOf(string rel) { lock (Entries) return Entries.FirstOrDefault(e => string.Equals(e.Rel, rel, StringComparison.OrdinalIgnoreCase)); }

    /// <summary>A script (or a folder: prefix ending with \) was renamed: its entries follow.</summary>
    public int MoveRel(string oldRel, string newRel)
    {
        int n = 0;
        lock (Entries)
        {
            foreach (var e in Entries)
            {
                if (string.Equals(e.Rel, oldRel, StringComparison.OrdinalIgnoreCase)) { e.Rel = newRel; e.Name = Path.GetFileNameWithoutExtension(newRel.Replace('\\', '/')); n++; }
                else if (oldRel.EndsWith("\\") && e.Rel.StartsWith(oldRel, StringComparison.OrdinalIgnoreCase)) { e.Rel = newRel + e.Rel.Substring(oldRel.Length); n++; }
            }
        }
        if (n > 0) Save();
        return n;
    }

    public long DiskBytes()
    {
        try { return Directory.Exists(Dir) ? Directory.EnumerateFiles(Dir).Sum(f => new FileInfo(f).Length) : 0; } catch { return 0; }
    }

    public static bool Matches(RunEntry e, string filter, string? rel, string? q)
    {
        bool ok = filter switch
        {
            "ok" => e.Kind == "ok",
            "fail" => e.Kind == "fail" || e.Kind == "interrupted",
            "stopped" => e.Kind == "stopped",
            "window" => e.Mode != "inline",
            _ => true
        };
        if (!ok) return false;
        if (!string.IsNullOrEmpty(rel) && !string.Equals(e.Rel, rel, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(q))
        {
            string hay = e.Name + " " + e.Rel + " " + e.Args + " " + e.ResultText;
            foreach (var w in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (hay.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        }
        return true;
    }
}

/// <summary>The last result per script for the row mark: settings lastRuns, "relpath|kind|exitcode|time|ms|mode".</summary>
public sealed class LastRun
{
    /// <summary>How many scripts keep their last run in the settings (the newest).</summary>
    public const int MaxKept = 200;

    public string Rel = "";
    public string Kind = "";
    public int Code;
    public DateTime Time;
    public long Ms;
    public string Mode = "";
    public static readonly string[] Kinds = { "ok", "fail", "stopped", "closed", "started" };

    public string ToLine() => string.Join("|", Rel, Kind, Code.ToString(CultureInfo.InvariantCulture), Time.ToString("s", CultureInfo.InvariantCulture), Ms.ToString(CultureInfo.InvariantCulture), Mode);

    public static LastRun? Parse(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var p = line.Split('|');
        if (p.Length < 6 || p[0].Length == 0 || !Kinds.Contains(p[1])) return null;
        if (!DateTime.TryParseExact(p[3], "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        int.TryParse(p[2], out int code);
        long.TryParse(p[4], out long ms);
        return new LastRun { Rel = p[0], Kind = p[1], Code = code, Time = t, Ms = ms, Mode = p[5] };
    }

    public static Dictionary<string, LastRun> Import(Settings s)
    {
        var d = new Dictionary<string, LastRun>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in s.LastRuns) { var r = Parse(l); if (r != null) d[r.Rel] = r; }
        return d;
    }

    /// <summary>Stores the result (the <see cref="MaxKept"/> newest are kept).</summary>
    public static void Set(Settings s, Dictionary<string, LastRun> map, LastRun r)
    {
        map[r.Rel] = r;
        s.LastRuns = Lines(map.Values);
    }

    /// <summary>The settings lines of the <see cref="MaxKept"/> newest runs.</summary>
    public static List<string> Lines(IEnumerable<LastRun> runs) =>
        runs.OrderByDescending(x => x.Time).Take(MaxKept).Select(x => x.ToLine()).ToList();
}
