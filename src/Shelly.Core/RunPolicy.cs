using System.Text;
using System.Text.Json;

namespace Shelly.Core;

/// <summary>Which mode a run uses (1.2.0).</summary>
public static class RunModes
{
    public const string Inline = "inline", Window = "window", Admin = "admin";
    /// <summary>The choices of Settings > Running > Default run mode ("window" is the default since 1.2.0).</summary>
    public static readonly string[] Defaults = { "window", "inline" };
    public const string DefaultMode = "window";

    /// <summary>requested: the mode the user picked for this run (menu item, Run settings, re-run) — always wins.
    /// For runs from the list, the favorites and the jump list the script's own choice comes next: the remembered mode
    /// (Run settings > Remember) before the <c># Shelly-Mode:</c> header. Everything else gets the global default
    /// (before 1.2.0 that fallback was always inline). Save and Run in the editor stays inline: it is the edit-and-try
    /// loop, and the editor brings the output in Shelly's window to the front.</summary>
    public static string Resolve(string? requested, string origin, string? remembered, string? header, string defaultMode)
    {
        if (requested is "inline" or "window" or "admin") return requested;
        if (origin == "editor") return "inline";
        if (origin is "list" or "jumplist")
        {
            if (remembered is "inline" or "window" or "admin") return remembered;
            if (header is "inline" or "window" or "admin") return header;
        }
        return defaultMode is "inline" or "window" ? defaultMode : DefaultMode;
    }
}

/// <summary>When a finished run shows a notification (1.2.0).</summary>
public static class Notifications
{
    /// <summary>Settings > Running > Notifications.</summary>
    public static readonly string[] Levels = { "off", "errors", "always" };
    /// <summary>Exit code of a console window closed with its X button (STATUS_CONTROL_C_EXIT).</summary>
    public const int WindowClosedCode = unchecked((int)0xC000013A);

    /// <summary>A failed run: an error exit code, an interrupted run, or a window that ended with an error code (not the X).</summary>
    public static bool IsFailure(string kind, int code) =>
        kind is "fail" or "interrupted" || (kind == "closed" && code != 0 && code != WindowClosedCode);

    /// <summary>level: off | errors | always. afterSec: "only if longer than" (0 = any length). Stopped runs (the user
    /// stopped them) and windows closed by the user never notify. Scheduled runs notify by the level alone: nobody watched
    /// them start. Otherwise the run must have taken at least afterSec, and an inline run is not announced while the main
    /// window is active (its output is on screen).</summary>
    public static bool Should(string level, int afterSec, string kind, int code, double ms, bool scheduled, bool inline, bool mainWindowActive)
    {
        if (level is not ("errors" or "always")) return false;
        if (kind is "stopped" or "started" or "running") return false;
        if (kind == "closed" && !IsFailure(kind, code)) return false;
        bool fail = IsFailure(kind, code);
        if (level == "errors" && !fail) return false;
        if (scheduled) return true;
        if (ms < Math.Max(0, afterSec) * 1000.0) return false;
        if (inline && mainWindowActive) return false;
        return true;
    }
}

/// <summary>Whether a scheduled (unattended) run may start. Nobody can answer a dialog, so everything that asks before an
/// interactive run blocks it instead, and the user gets a notification and a history entry.</summary>
public static class ScheduleGate
{
    public sealed record Input
    {
        public bool ScriptFound { get; init; } = true;
        public bool DisclaimerAccepted { get; init; } = true;
        /// <summary>The mode the script asks for (remembered mode or header); "admin" blocks.</summary>
        public string? ExplicitMode { get; init; }
        /// <summary>#Requires -RunAsAdministrator.</summary>
        public bool RequiresElevation { get; init; }
        /// <summary>Mark of the Web on the script (or a script it loads) that blocks it.</summary>
        public bool Motw { get; init; }
        /// <summary>ScriptMeta.Scan: ok | checking | unverified | none (Python).</summary>
        public string Scan { get; init; } = "ok";
        public bool SafetyWarnings { get; init; } = true;
        /// <summary>Safety.Needed for this content hash: findings that ask and are not trusted for this version.</summary>
        public bool NeedsConfirmation { get; init; }
        public bool PresetMissing { get; init; }
        public IReadOnlyCollection<string> MissingMandatory { get; init; } = Array.Empty<string>();
    }

    /// <summary>null = may run; otherwise the Loc key of the reason (its {0} is the script name).</summary>
    public static string? Block(Input i)
    {
        if (!i.ScriptFound) return "schedBlockScript";
        if (!i.DisclaimerAccepted) return "schedBlockDisclaimer";
        if (i.ExplicitMode == "admin" || i.RequiresElevation) return "schedBlockAdmin";
        if (i.Motw) return "schedBlockMotw";
        if (i.Scan is "checking" or "unverified" && i.SafetyWarnings) return "schedBlockUnverified";
        if (i.NeedsConfirmation) return "schedBlockConfirm";
        if (i.PresetMissing) return "schedBlockPreset";
        if (i.MissingMandatory.Count > 0) return "schedBlockParams";
        return null;
    }

    /// <summary>Administrator scripts can't be scheduled (1.2.0): a task with "highest privileges" would need an elevated
    /// Shelly to create it and would then run elevated with no UAC prompt at all. The safer and simpler rule is no.</summary>
    public static bool CanSchedule(string? explicitMode, bool requiresElevation) => explicitMode != "admin" && !requiresElevation;
}

/// <summary>Named sets of parameter values per script (1.2.0), launcher-settings.json "paramPresets": one compact JSON
/// line each {"rel":…,"name":…,"values":{…}}. PowerShell scripts keep their param() values by name; Python scripts keep
/// the free-text arguments under <see cref="PythonRun.ArgsKey"/>. Plain JSON, not encrypted: not for passwords.</summary>
public sealed class ParamPreset
{
    public const int MaxName = 60;
    public string Rel = "";
    public string Name = "";
    public Dictionary<string, object> Values = new(StringComparer.OrdinalIgnoreCase);

    public static ParamPreset? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("rel", out var rel) || rel.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(rel.GetString())) return null;
            if (!r.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || Presets.CleanName(name.GetString()) is not { } n) return null;
            var p = new ParamPreset { Rel = rel.GetString()!, Name = n };
            if (r.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
                foreach (var v in vals.EnumerateObject())
                    p.Values[v.Name] = v.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String => v.Value.GetString() ?? "",
                        _ => v.Value.GetRawText()
                    };
            return p;
        }
        catch { return null; }
    }

    public string ToLine()
    {
        // The values part is written like the remembered run settings
        string run = RunPreset.ToLine(Rel, "inline", Values);
        int i = run.IndexOf(",\"values\":", StringComparison.Ordinal);
        return "{\"rel\":" + Settings.JsonString(Rel) + ",\"name\":" + Settings.JsonString(Name) + run.Substring(i);
    }
}

public static class Presets
{
    /// <summary>The name as it is stored: trimmed, control characters and runs of spaces as one space, at most <see cref="ParamPreset.MaxName"/>
    /// characters. Null when nothing is left.</summary>
    public static string? CleanName(string? name)
    {
        if (name == null) return null;
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(char.IsControl(c) ? ' ' : c);
        string n = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        if (n.Length > ParamPreset.MaxName) n = n.Substring(0, ParamPreset.MaxName).TrimEnd();
        return n.Length == 0 ? null : n;
    }

    static List<ParamPreset> All(Settings s) => s.ParamPresets.Select(ParamPreset.Parse).Where(p => p != null).Select(p => p!).ToList();
    static void Store(Settings s, IEnumerable<ParamPreset> list) => s.ParamPresets = list.Select(p => p.ToLine()).ToList();
    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The presets of a script, by name (culture order, so Cyrillic and Latin names sort as people expect).</summary>
    public static List<ParamPreset> For(Settings s, string rel) =>
        All(s).Where(p => Same(p.Rel, rel)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static List<string> Names(Settings s, string rel) => For(s, rel).Select(p => p.Name).ToList();

    public static ParamPreset? Get(Settings s, string rel, string name) => All(s).FirstOrDefault(p => Same(p.Rel, rel) && Same(p.Name, name));

    public static bool Exists(Settings s, string rel, string name) => Get(s, rel, name) != null;

    /// <summary>Saves (or replaces, by name without case) a preset; false when the name is empty.</summary>
    public static bool Save(Settings s, string rel, string name, IDictionary<string, object> values)
    {
        var n = CleanName(name);
        if (n == null) return false;
        var list = All(s);
        var p = new ParamPreset { Rel = rel, Name = n };
        foreach (var (k, v) in values) if (v != null) p.Values[k] = v;
        int i = list.FindIndex(x => Same(x.Rel, rel) && Same(x.Name, n));
        if (i >= 0) list[i] = p; else list.Add(p);
        Store(s, list);
        return true;
    }

    public static bool Delete(Settings s, string rel, string name)
    {
        var list = All(s);
        int n = list.RemoveAll(x => Same(x.Rel, rel) && Same(x.Name, name));
        Store(s, list);
        return n > 0;
    }

    /// <summary>A script was renamed (newRel) or deleted (null): its presets follow or go.</summary>
    public static void MoveScript(Settings s, string oldRel, string? newRel)
    {
        var list = All(s);
        if (newRel == null) list.RemoveAll(p => Same(p.Rel, oldRel));
        else foreach (var p in list) if (Same(p.Rel, oldRel)) p.Rel = newRel;
        Store(s, list);
    }

    public static void MoveFolder(Settings s, Func<string, string> fix)
    {
        var list = All(s);
        foreach (var p in list) p.Rel = fix(p.Rel);
        Store(s, list);
    }

    public static void DropWhere(Settings s, Func<string, bool> under)
    {
        var list = All(s);
        list.RemoveAll(p => under(p.Rel));
        Store(s, list);
    }

    /// <summary>The values a preset gives the script as it is now: values of parameters that no longer exist are left out
    /// and listed in Ignored (the script was edited since the preset was saved); parameters the preset does not mention
    /// keep the script's defaults. A Python script takes only the arguments text.</summary>
    public static (Dictionary<string, object> Values, List<string> Ignored) Apply(IEnumerable<ParamInfo> ps, bool python, IDictionary<string, object> preset)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var ignored = new List<string>();
        if (python)
        {
            foreach (var (k, v) in preset)
                if (k == PythonRun.ArgsKey) values[k] = v; else ignored.Add(k);
            return (values, ignored);
        }
        var byName = ps.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in preset)
        {
            if (byName.TryGetValue(k, out var p)) values[p.Name] = v;
            else ignored.Add(k);
        }
        ignored.Sort(StringComparer.OrdinalIgnoreCase);
        return (values, ignored);
    }

    /// <summary>The form after a preset was picked in Run settings: the preset's values over the script's defaults (what
    /// is in the form now does not leak into it), so what runs is what was saved.</summary>
    public static Dictionary<string, object> Merge(IDictionary<string, object>? defaults, IDictionary<string, object> applied)
    {
        var res = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (defaults != null) foreach (var (k, v) in defaults) res[k] = v;
        foreach (var (k, v) in applied) res[k] = v;
        return res;
    }
}

/// <summary>Shelly's command line (1.2.0 moved it to Core so it is tested):
/// --run &lt;script&gt; [--preset &lt;name&gt;] [--headless] · --scheduled &lt;id&gt; · --history &lt;run id&gt; · --minimized · --show.
/// &lt;script&gt; is the path relative to the Scripts folder (as in the jump list). --run alone runs the script like a
/// click in the list; with --headless it runs unattended (hidden, output to the history, no dialogs), which is what a
/// Task Scheduler task does with --scheduled.</summary>
public sealed class Cli
{
    public string? Run;
    public string? Preset;
    public bool Headless;
    public string? Scheduled;
    public string? History;
    public bool Minimized;
    public bool Restarted;
    public bool Perf;

    /// <summary>An unattended run: no main window.</summary>
    public bool IsUnattended => Scheduled != null || (Run != null && Headless);

    static string? Value(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;

    public static Cli Parse(string[] args)
    {
        var c = new Cli();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if (a.StartsWith("/") || (a.StartsWith("-") && !a.StartsWith("--"))) a = "--" + a.TrimStart('-', '/');
            switch (a)
            {
                case "--run": c.Run = Value(args, ref i); break;
                case "--preset": c.Preset = Presets.CleanName(Value(args, ref i)); break;
                case "--headless": c.Headless = true; break;
                case "--scheduled":
                    var id = Value(args, ref i)?.Trim().ToLowerInvariant();
                    c.Scheduled = id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{8}$") ? id : null;
                    break;
                case "--history":
                    var h = Value(args, ref i)?.Trim();
                    c.History = h != null && System.Text.RegularExpressions.Regex.IsMatch(h, @"^[\w-]{1,64}$") ? h : null;
                    break;
                case "--restarted": c.Restarted = true; break;
                case "--perf": c.Perf = true; break;
                case "--minimized": case "--tray": c.Minimized = true; break;
            }
        }
        if (c.Run != null)
        {
            c.Run = c.Run.Trim().Trim('"').Replace('/', '\\');
            if (c.Run.Length == 0 || c.Run.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0) c.Run = null;
        }
        return c;
    }

    /// <summary>What a second start sends to the running Shelly (named pipe, tab-separated).</summary>
    public string ToMessage()
    {
        if (Scheduled != null) return "sched\t" + Scheduled;
        if (Run != null) return (Headless ? "brun\t" : "run\t") + Run + (Preset != null ? "\t" + Preset : "");
        if (History != null) return "history\t" + History;
        return "show";
    }

    public static Cli FromMessage(string m)
    {
        var p = (m ?? "").Split('\t');
        var c = new Cli();
        switch (p[0])
        {
            case "sched" when p.Length > 1: return Parse(new[] { "--scheduled", p[1] });
            case "run" when p.Length > 1:
            case "brun" when p.Length > 1:
                c = Parse(p.Length > 2 ? new[] { "--run", p[1], "--preset", p[2] } : new[] { "--run", p[1] });
                c.Headless = p[0] == "brun";
                return c;
            case "history" when p.Length > 1: return Parse(new[] { "--history", p[1] });
            default: return c;
        }
    }
}

/// <summary>The texts of a run notification (balloon tip: the title is cut at 63 characters, the text at 255).</summary>
public static class NotifyText
{
    public static (string Title, string Text, bool Ok) ForRun(RunEntry e, bool scheduled)
    {
        bool fail = Notifications.IsFailure(e.Kind, e.Code);
        string name = e.Name + (e.Preset.Length > 0 ? " · " + e.Preset : "");
        string text = Loc.F("notifText", e.Code, RunResult.FormatDuration(e.Ms));
        if (scheduled) text = Loc.T("notifScheduled") + " · " + text;
        return (Clip(Loc.F(fail ? "notifTitleFail" : "notifTitleOk", name), 63), Clip(text, 255), !fail);
    }

    /// <summary>A scheduled run that was not started (reason: the text of a ScheduleGate key).</summary>
    public static (string Title, string Text, bool Ok) ForBlocked(string name, string reason) =>
        (Clip(Loc.F("notifBlockedTitle", name), 63), Clip(reason, 255), false);

    public static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";
}
