using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>launcher-settings.json: the same file and keys as the PowerShell edition (0.9.x), so both editions can
/// share one Shelly folder. Keys added in 1.0 are appended; unknown keys are kept as they are.</summary>
public sealed class Settings
{
    public string Language = "en";
    public List<string> Favorites = new();
    public string FavoritesBase = "Scripts";
    public bool TrayHintShown;
    public bool NavCompact;
    public bool OutputCollapsed;
    public string OutputHeight = "240";
    public bool OutputWrap = true;
    public string HistoryFilter = "all";
    public string HistoryMax = "100";
    public string HistoryOutputKB = "1024";
    public List<string> CollapsedGroups = new();
    public bool StartMinimized;
    public bool CloseToTray = true;
    public string Hotkey = "Ctrl+Alt+L";
    public string Theme = "system";
    public string Accent = "system";
    public string CompatDismissed = "";
    public bool SafetyWarnings = true;
    public List<string> Trusted = new();
    public List<string> LastRuns = new();
    public string EngineDefault = "auto";
    public List<string> Engines = new();
    public bool PwshOfferDismissed;
    public string LastVersion = "";
    public List<string> RunPresets = new();
    // New in 1.0
    public List<string> ScriptIcons = new();      // "Folder\Name.ps1|assets\script-icons\xxx.png" (icon path relative to the Shelly folder)
    public bool ToastOnFinish = true;
    public string ToastAfterSec = "20";
    public bool UpdateCheck = true;
    public string UpdateLastCheck = "";
    public string UpdateLatest = "";
    public bool JumpList = true;
    /// <summary>Script list order inside each group: name | modified | lastrun | runcount.</summary>
    public string ListSort = "name";
    public bool ListSortDesc;
    /// <summary>Script list view: cards | compact.</summary>
    public string ListView = "cards";
    // New in 1.0.11
    /// <summary>Remote sources the user trusts for download-and-run one-liners: "host" or "host/path-prefix" (HTTPS only).</summary>
    public List<string> TrustedSources = new();
    /// <summary>Built-in trusted sources the user turned off.</summary>
    public List<string> DisabledBuiltinSources = new();
    /// <summary>The Shelly version whose first-run notice ("not a sandbox") the user accepted; empty = not yet.</summary>
    public string DisclaimerAccepted = "";

    /// <summary>Keys this version does not know (kept on save).</summary>
    public Dictionary<string, JsonElement> Unknown = new();
    /// <summary>True when Load changed something that should be written back (old formats).</summary>
    public bool Dirty;
    /// <summary>The file existed and came from the PowerShell edition (lastVersion 0.x or missing).</summary>
    public bool MigratedFromPs;

    public static readonly string[] Keys =
    {
        "language", "theme", "accent", "startMinimized", "closeToTray", "hotkey", "favorites", "favoritesBase", "trayHintShown",
        "navCompact", "outputCollapsed", "outputHeight", "outputWrap", "historyFilter",
        "historyMax", "historyOutputKB", "collapsedGroups", "compatDismissed",
        "safetyWarnings", "trusted", "lastRuns", "engineDefault", "engines", "pwshOfferDismissed", "lastVersion", "runPresets",
        "scriptIcons", "toastOnFinish", "toastAfterSec", "updateCheck", "updateLastCheck", "updateLatest", "jumpList",
        "listSort", "listSortDesc", "listView", "trustedSources", "disabledBuiltinSources", "disclaimerAccepted"
    };

    /// <summary>Keys of the PowerShell edition that Shelly 1.0 has no use for: read (so they don't land in Unknown) and
    /// dropped on the next save.</summary>
    public static readonly string[] ObsoleteKeys = { "favTilesCollapsed", "detailsCollapsed", "detailsOpen", "historyThisScript", "migrationDeclined", "launcherHost" };
    public static readonly string[] SortChoices = { "name", "modified", "lastrun", "runcount" };
    public static readonly int[] ToastAfterChoices = { 10, 20, 30, 60 };

    public static readonly int[] HistoryMaxChoices = { 50, 100, 200 };
    public static readonly int[] HistoryKBChoices = { 256, 1024, 5120, 20480 };
    public static readonly string[] AccentChoices = { "#0067C0", "#2D7D9A", "#107C10", "#8764B8", "#C239B3", "#E3008C", "#CA5010", "#C42B1C", "#68768A" };

    public int HistoryMaxValue => int.TryParse(HistoryMax, out var n) ? Math.Max(1, n) : 100;
    public int HistoryOutputChars => (int.TryParse(HistoryOutputKB, out var n) ? Math.Max(16, n) : 1024) * 1024;
    public int ToastAfterSeconds => int.TryParse(ToastAfterSec, out var n) ? Math.Clamp(n, 0, 3600) : 20;

    // ---------- Load ----------
    public static Settings Load(string path)
    {
        var s = new Settings();
        if (!File.Exists(path)) return s;
        try
        {
            string text = File.ReadAllText(path);
            s.LoadJson(text);
        }
        catch { }
        return s;
    }

    public void LoadJson(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        FavoritesBase = "";   // missing in the oldest pre-release files
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "language": Language = Str(v); break;
                case "favorites": Favorites = List(v); break;
                case "favoritesBase": FavoritesBase = Str(v); break;
                case "trayHintShown": TrayHintShown = Bool(v); break;
                case "navCompact": NavCompact = Bool(v); break;
                case "outputCollapsed": OutputCollapsed = Bool(v); break;
                case "outputHeight": OutputHeight = Str(v); break;
                case "outputWrap": OutputWrap = Bool(v); break;
                case "historyFilter": HistoryFilter = Str(v); break;
                case "historyMax": HistoryMax = Str(v); break;
                case "historyOutputKB": HistoryOutputKB = Str(v); break;
                case "collapsedGroups": CollapsedGroups = List(v); break;
                case "startMinimized": StartMinimized = Bool(v); break;
                case "closeToTray": CloseToTray = Bool(v); break;
                case "hotkey": Hotkey = v.ValueKind == JsonValueKind.Null ? "Ctrl+Alt+L" : Str(v); break;
                case "theme": Theme = Str(v); break;
                case "accent": Accent = Str(v); break;
                case "compatDismissed": CompatDismissed = Str(v); break;
                case "safetyWarnings": SafetyWarnings = Bool(v); break;
                case "trustedSources": TrustedSources = List(v); break;
                case "disabledBuiltinSources": DisabledBuiltinSources = List(v); break;
                case "disclaimerAccepted": DisclaimerAccepted = Str(v); break;
                case "trusted": Trusted = List(v); break;
                case "lastRuns": LastRuns = List(v); break;
                case "engineDefault": EngineDefault = Str(v); break;
                case "engines": Engines = List(v); break;
                case "pwshOfferDismissed": PwshOfferDismissed = Bool(v); break;
                case "lastVersion": LastVersion = Str(v); break;
                case "runPresets": RunPresets = List(v); break;
                case "scriptIcons": ScriptIcons = List(v); break;
                case "toastOnFinish": ToastOnFinish = Bool(v); break;
                case "toastAfterSec": ToastAfterSec = Str(v); break;
                case "updateCheck": UpdateCheck = Bool(v); break;
                case "updateLastCheck": UpdateLastCheck = Str(v); break;
                case "updateLatest": UpdateLatest = Str(v); break;
                case "jumpList": JumpList = Bool(v); break;
                case "listSort": ListSort = Str(v); break;
                case "listSortDesc": ListSortDesc = Bool(v); break;
                case "listView": ListView = Str(v); break;
                case var o when ObsoleteKeys.Contains(o): Dirty = true; break;
                default: Unknown[p.Name] = v.Clone(); break;
            }
        }
        Validate();
    }

    void Validate()
    {
        if (Language != "en" && Language != "ru") Language = "en";
        if (Theme != "system" && Theme != "light" && Theme != "dark") Theme = "system";
        if (Accent != "system" && !Regex.IsMatch(Accent, "^#[0-9A-Fa-f]{6}$")) Accent = "system";
        if (!new[] { "all", "ok", "fail", "stopped", "window" }.Contains(HistoryFilter)) HistoryFilter = "all";
        if (!int.TryParse(HistoryMax, out int n) || n < 1) HistoryMax = "100"; else if (n > 1000) HistoryMax = "1000";
        if (!int.TryParse(HistoryOutputKB, out n) || n < 16) HistoryOutputKB = "1024"; else if (n > 102400) HistoryOutputKB = "102400";
        if (!int.TryParse(OutputHeight, out n) || n < 80 || n > 2000) OutputHeight = "240";
        if (!int.TryParse(ToastAfterSec, out n) || n < 0) ToastAfterSec = "20";
        if (EngineDefault != "auto" && EngineDefault != "7" && EngineDefault != "5.1") EngineDefault = "auto";
        if (!SortChoices.Contains(ListSort)) ListSort = "name";
        if (ListView != "cards" && ListView != "compact") ListView = "cards";
        Engines = Engines.Where(e => Regex.IsMatch(e, @"^[^|]+\|(7|5\.1)$")).ToList();
        ScriptIcons = ScriptIcons.Where(e => Regex.IsMatch(e, @"^[^|]+\|[^|]+$")).ToList();
        // Early pre-releases stored favorites relative to the Shelly folder; they are relative to Scripts since then
        if (FavoritesBase != "Scripts")
        {
            Favorites = Favorites.Select(f => Regex.Replace(f, @"^(\.\\)?Scripts\\", "", RegexOptions.IgnoreCase)).Where(f => f.Length > 0).ToList();
            FavoritesBase = "Scripts";
            Dirty = true;
        }
        if (string.IsNullOrEmpty(LastVersion) || LastVersion.StartsWith("0.")) MigratedFromPs = true;
    }

    static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => ""
    };

    // PowerShell's [bool] cast: non-empty strings are true, "False" too; the PowerShell edition always wrote real booleans
    static bool Bool(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => v.TryGetDouble(out var d) && d != 0,
        JsonValueKind.String => !string.IsNullOrEmpty(v.GetString()) && !string.Equals(v.GetString(), "false", StringComparison.OrdinalIgnoreCase) && v.GetString() != "0",
        _ => false
    };

    static List<string> List(JsonElement v)
    {
        var res = new List<string>();
        if (v.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in v.EnumerateArray()) { string s = Str(x); if (s.Length > 0) res.Add(s); }
        }
        else { string s = Str(v); if (s.Length > 0) res.Add(s); }   // ConvertTo-Json of a one-item array in old files
        return res;
    }

    // ---------- Save (the same layout as the PowerShell edition: one key per line, UTF-8 with BOM, CRLF) ----------
    public string ToJson()
    {
        var sb = new StringBuilder("{\r\n");
        var lines = new List<string>();
        foreach (var k in Keys) lines.Add("  " + JsonString(k) + ": " + ValueJson(k));
        foreach (var (k, v) in Unknown) lines.Add("  " + JsonString(k) + ": " + v.GetRawText());
        sb.Append(string.Join(",\r\n", lines));
        sb.Append("\r\n}\r\n");
        return sb.ToString();
    }

    public void Save(string path)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(), new UTF8Encoding(true));
        File.Move(tmp, path, true);
        Dirty = false;
    }

    string ValueJson(string key) => key switch
    {
        "language" => JsonString(Language), "theme" => JsonString(Theme), "accent" => JsonString(Accent),
        "startMinimized" => B(StartMinimized), "closeToTray" => B(CloseToTray), "hotkey" => JsonString(Hotkey),
        "favorites" => L(Favorites), "favoritesBase" => JsonString(FavoritesBase), "trayHintShown" => B(TrayHintShown),
        "navCompact" => B(NavCompact), "outputCollapsed" => B(OutputCollapsed), "outputHeight" => JsonString(OutputHeight),
        "outputWrap" => B(OutputWrap), "historyFilter" => JsonString(HistoryFilter),
        "historyMax" => JsonString(HistoryMax), "historyOutputKB" => JsonString(HistoryOutputKB), "collapsedGroups" => L(CollapsedGroups),
        "compatDismissed" => JsonString(CompatDismissed),
        "safetyWarnings" => B(SafetyWarnings), "trusted" => L(Trusted), "lastRuns" => L(LastRuns), "engineDefault" => JsonString(EngineDefault),
        "engines" => L(Engines), "pwshOfferDismissed" => B(PwshOfferDismissed),
        "lastVersion" => JsonString(LastVersion), "runPresets" => L(RunPresets),
        "scriptIcons" => L(ScriptIcons), "toastOnFinish" => B(ToastOnFinish), "toastAfterSec" => JsonString(ToastAfterSec),
        "updateCheck" => B(UpdateCheck), "updateLastCheck" => JsonString(UpdateLastCheck), "updateLatest" => JsonString(UpdateLatest),
        "jumpList" => B(JumpList), "listSort" => JsonString(ListSort), "listSortDesc" => B(ListSortDesc), "listView" => JsonString(ListView),
        "trustedSources" => L(TrustedSources), "disabledBuiltinSources" => L(DisabledBuiltinSources), "disclaimerAccepted" => JsonString(DisclaimerAccepted),
        _ => "null"
    };

    static string B(bool b) => b ? "true" : "false";
    static string L(List<string> l) => l.Count == 0 ? "[]" : "[\r\n    " + string.Join(",\r\n    ", l.Select(JsonString)) + "\r\n  ]";
    public static string JsonString(string s) => JsonSerializer.Serialize(s ?? "", JsonOpts.Relaxed);

    // ---------- Per-script lists keyed by the Scripts-relative path (case-insensitive) ----------
    public string GetEngineChoice(string rel)
    {
        string pre = rel.ToLowerInvariant() + "|";
        foreach (var l in Engines) if (l.ToLowerInvariant().StartsWith(pre)) return l.Substring(pre.Length);
        return "auto";
    }

    public void SetEngineChoice(string rel, string engine)
    {
        string pre = rel.ToLowerInvariant() + "|";
        Engines = Engines.Where(l => !l.ToLowerInvariant().StartsWith(pre)).ToList();
        if (engine == "7" || engine == "5.1") Engines.Add(rel + "|" + engine);
    }

    public string? GetIcon(string rel)
    {
        string pre = rel.ToLowerInvariant() + "|";
        foreach (var l in ScriptIcons) if (l.ToLowerInvariant().StartsWith(pre)) return l.Substring(pre.Length);
        return null;
    }

    public void SetIcon(string rel, string? iconRel)
    {
        string pre = rel.ToLowerInvariant() + "|";
        ScriptIcons = ScriptIcons.Where(l => !l.ToLowerInvariant().StartsWith(pre)).ToList();
        if (!string.IsNullOrEmpty(iconRel)) ScriptIcons.Add(rel + "|" + iconRel);
    }

    public bool IsFavorite(string rel) => Favorites.Any(f => string.Equals(f, rel, StringComparison.OrdinalIgnoreCase));

    public void SetFavorite(string rel, bool on)
    {
        Favorites = Favorites.Where(f => !string.Equals(f, rel, StringComparison.OrdinalIgnoreCase)).ToList();
        if (on) Favorites.Add(rel);
    }

    /// <summary>A script was renamed or moved: favorites, engine, icon, presets and trust follow it (null = deleted).</summary>
    public void MoveScript(string oldRel, string? newRel)
    {
        bool fav = IsFavorite(oldRel);
        SetFavorite(oldRel, false);
        if (fav && newRel != null) SetFavorite(newRel, true);
        string eng = GetEngineChoice(oldRel);
        SetEngineChoice(oldRel, "auto");
        if (newRel != null) SetEngineChoice(newRel, eng);
        string? icon = GetIcon(oldRel);
        SetIcon(oldRel, null);
        if (newRel != null && icon != null) SetIcon(newRel, icon);
        var preset = RunPreset.Get(this, oldRel);
        RunPreset.Remove(this, oldRel);
        if (newRel != null && preset != null) RunPreset.Set(this, newRel, preset.Mode, preset.Values);
        string tpre = oldRel.ToLowerInvariant() + "|";
        Trusted = Trusted.Where(t => !t.ToLowerInvariant().StartsWith(tpre)).ToList();
    }

    /// <summary>A folder was renamed: every per-script entry under it follows.</summary>
    public void MoveFolder(string oldFolderRel, string newFolderRel)
    {
        string op = oldFolderRel.TrimEnd('\\') + "\\";
        string np = newFolderRel.TrimEnd('\\') + "\\";
        string Fix(string rel) => rel.StartsWith(op, StringComparison.OrdinalIgnoreCase) ? np + rel.Substring(op.Length) : rel;
        string FixLine(string line) { int i = line.IndexOf('|'); return i < 0 ? Fix(line) : Fix(line.Substring(0, i)) + line.Substring(i); }
        Favorites = Favorites.Select(Fix).ToList();
        Engines = Engines.Select(FixLine).ToList();
        ScriptIcons = ScriptIcons.Select(FixLine).ToList();
        LastRuns = LastRuns.Select(FixLine).ToList();
        Trusted = Trusted.Select(t => { int i = t.IndexOf('|'); return i < 0 ? t : Fix(t.Substring(0, i)).ToLowerInvariant() + t.Substring(i); }).ToList();
        RunPresets = RunPresets.Select(l => { var p = RunPreset.Parse(l); return p == null ? l : RunPreset.ToLine(Fix(p.Rel), p.Mode, p.Values); }).ToList();
        string og = oldFolderRel.TrimEnd('\\');
        CollapsedGroups = CollapsedGroups.Select(g => string.Equals(g, og, StringComparison.OrdinalIgnoreCase) ? newFolderRel.TrimEnd('\\') : (g.StartsWith(og + "\\", StringComparison.OrdinalIgnoreCase) ? newFolderRel.TrimEnd('\\') + g.Substring(og.Length) : g)).ToList();
    }
}

public static class JsonOpts
{
    public static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

/// <summary>Remembered run settings (Run settings > Remember): one compact JSON line per script
/// {"rel":"Folder\\a.ps1","mode":"inline","values":{"Name":"x","Force":true}}.</summary>
public sealed class RunPreset
{
    public string Rel = "";
    public string Mode = "inline";
    public Dictionary<string, object> Values = new(StringComparer.OrdinalIgnoreCase);

    public static RunPreset? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("rel", out var rel) || rel.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(rel.GetString())) return null;
            var p = new RunPreset { Rel = rel.GetString()! };
            if (r.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String) p.Mode = m.GetString() ?? "inline";
            if (p.Mode != "inline" && p.Mode != "window" && p.Mode != "admin") p.Mode = "inline";
            if (r.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
            {
                foreach (var v in vals.EnumerateObject())
                {
                    p.Values[v.Name] = v.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String => v.Value.GetString() ?? "",
                        _ => v.Value.GetRawText()
                    };
                }
            }
            return p;
        }
        catch { return null; }
    }

    public static string ToLine(string rel, string mode, IDictionary<string, object>? values)
    {
        var sb = new StringBuilder("{\"rel\":").Append(Settings.JsonString(rel)).Append(",\"mode\":").Append(Settings.JsonString(mode)).Append(",\"values\":{");
        bool first = true;
        if (values != null)
        {
            foreach (var (k, v) in values)
            {
                if (v == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Settings.JsonString(k)).Append(':');
                sb.Append(v is bool b ? (b ? "true" : "false") : Settings.JsonString(Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""));
            }
        }
        return sb.Append("}}").ToString();
    }

    public static RunPreset? Get(Settings s, string rel)
    {
        foreach (var l in s.RunPresets)
        {
            var p = Parse(l);
            if (p != null && string.Equals(p.Rel, rel, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return null;
    }

    public static void Set(Settings s, string rel, string mode, IDictionary<string, object>? values)
    {
        var keep = s.RunPresets.Where(l => { var p = Parse(l); return p != null && !string.Equals(p.Rel, rel, StringComparison.OrdinalIgnoreCase); }).ToList();
        if (values != null)
        {
            if (mode != "inline" && mode != "window" && mode != "admin") mode = "inline";
            keep.Add(ToLine(rel, mode, values));
        }
        s.RunPresets = keep;
    }

    public static void Remove(Settings s, string rel) => Set(s, rel, "", null);
}
