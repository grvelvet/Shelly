using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>launcher-settings.json: one key per line. Unknown keys are kept as they are.</summary>
public sealed class Settings
{
    public string Language = "en";
    /// <summary>The file has a "language" key: the user's (or an earlier start's) choice, which beats the system language.</summary>
    public bool LanguageSaved;
    public List<string> Favorites = new();
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
    /// <summary>Script folders outside Scripts (full paths, see <see cref="ExternalScripts"/>).</summary>
    public List<string> ExternalFolders = new();
    // New in 1.1.0
    /// <summary>python.exe for .py scripts; empty = find it (py launcher, PATH, usual install folders).</summary>
    public string PythonPath = "";
    // New in 1.3.0
    /// <summary>Custom interpreters (Settings > Environment): one JSON line each, see <see cref="CustomInterpreter"/>.</summary>
    public List<string> CustomInterpreters = new();
    /// <summary>Run .py scripts with the .venv / venv / env found next to them (1.1.1; on by default, missing key = on).</summary>
    public bool UseVenv = true;
    /// <summary>Order of the Favorites page (1.1.2): manual (the order they were added, or moved by hand; the default,
    /// as before) | name | lastrun | runcount | type.</summary>
    public string FavoritesSort = "manual";
    /// <summary>Favorites tile size (1.1.2): small | medium | large (Layout.FavoriteTiles); medium is the size before 1.1.2.</summary>
    public string FavoritesSize = "medium";
    // New in 1.2.0
    /// <summary>Mode of runs that don't ask for one (no remembered mode, no <c># Shelly-Mode:</c> header): window | inline.
    /// Missing key = window, the 1.2.0 default (it was always inline before).</summary>
    public string DefaultMode = RunModes.DefaultMode;
    /// <summary>Notifications when a run finishes: off | errors | always. Missing key: from toastOnFinish (off when that was off).</summary>
    public string Notify = "always";
    /// <summary>Named parameter presets (<see cref="ParamPreset"/>), one JSON line each. Plain text: not for secrets.</summary>
    public List<string> ParamPresets = new();
    /// <summary>Schedules (<see cref="Schedule"/>), one JSON line each; the source of truth for the Task Scheduler tasks.</summary>
    public List<string> Schedules = new();
    // New in 1.4.0
    /// <summary>Default editor for Edit (Ctrl+E / context menu): builtin | external.</summary>
    public string DefaultEditor = "builtin";
    /// <summary>External editor choice: auto | vscode | notepadpp | ise | notepad | custom.</summary>
    public string ExternalEditor = "auto";
    /// <summary>Executable path when ExternalEditor == "custom".</summary>
    public string ExternalEditorPath = "";
    /// <summary>Arguments template when ExternalEditor == "custom" ({file}, {line}).</summary>
    public string ExternalEditorArgs = "";

    /// <summary>Keys this version does not know (kept on save).</summary>
    public Dictionary<string, JsonElement> Unknown = new();
    /// <summary>The file exists but Load could not use it (broken or unreadable): the values are defaults and must not be saved
    /// over the file at startup.</summary>
    public bool LoadFailed;
    /// <summary>Where the copy of a broken file was kept (null when none was made).</summary>
    public string? CorruptCopy;

    public static readonly string[] Keys =
    {
        "language", "theme", "accent", "startMinimized", "closeToTray", "hotkey", "favorites", "trayHintShown",
        "navCompact", "outputCollapsed", "outputHeight", "outputWrap", "historyFilter",
        "historyMax", "historyOutputKB", "collapsedGroups", "compatDismissed",
        "safetyWarnings", "trusted", "lastRuns", "engineDefault", "engines", "pwshOfferDismissed", "lastVersion", "runPresets",
        "scriptIcons", "toastOnFinish", "toastAfterSec", "updateCheck", "updateLastCheck", "updateLatest", "jumpList",
        "listSort", "listSortDesc", "listView", "trustedSources", "disabledBuiltinSources", "disclaimerAccepted", "externalFolders",
        "pythonPath", "useVenv", "favoritesSort", "favoritesSize", "defaultMode", "notify", "paramPresets", "schedules",
        "customInterpreters",
        "defaultEditor", "externalEditor", "externalEditorPath", "externalEditorArgs"
    };

    public static readonly string[] SortChoices = { "name", "modified", "lastrun", "runcount" };
    public static readonly string[] FavoritesSortChoices = { "manual", "name", "lastrun", "runcount", "type" };
    /// <summary>"Only if longer than": 0 = any length (1.2.0).</summary>
    public static readonly int[] ToastAfterChoices = { 0, 10, 20, 30, 60 };

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
        string text;
        try { text = File.ReadAllText(path); }
        catch { s.LoadFailed = true; return s; }   // cannot be read right now (locked?): defaults must not be written over it
        try { s.LoadJson(text); }
        catch
        {
            // Broken file: defaults are used, a copy of the file is kept, and the startup does not overwrite it
            s.LoadFailed = true;
            try
            {
                s.CorruptCopy = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                File.Copy(path, s.CorruptCopy, true);
            }
            catch { s.CorruptCopy = null; }
        }
        return s;
    }

    public void LoadJson(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        bool notifySeen = false;
        bool legacyToast = true;   // toastOnFinish of a file from before 1.2.0 (read only, for the migration below)
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "language": Language = Str(v); LanguageSaved = true; break;
                case "favorites": Favorites = List(v); break;
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
                case "externalFolders": ExternalFolders = List(v); break;
                case "customInterpreters": CustomInterpreters = List(v); break;
                case "defaultEditor": DefaultEditor = Str(v); break;
                case "externalEditor": ExternalEditor = Str(v); break;
                case "externalEditorPath": ExternalEditorPath = Str(v); break;
                case "externalEditorArgs": ExternalEditorArgs = Str(v); break;
                case "pythonPath": PythonPath = Str(v); break;
                case "useVenv": UseVenv = Bool(v); break;
                case "favoritesSort": FavoritesSort = Str(v); break;
                case "favoritesSize": FavoritesSize = Str(v); break;
                case "defaultMode": DefaultMode = Str(v); break;
                case "notify": Notify = Str(v); notifySeen = true; break;
                case "paramPresets": ParamPresets = List(v); break;
                case "schedules": Schedules = List(v); break;
                case "trusted": Trusted = List(v); break;
                case "lastRuns": LastRuns = List(v); break;
                case "engineDefault": EngineDefault = Str(v); break;
                case "engines": Engines = List(v); break;
                case "pwshOfferDismissed": PwshOfferDismissed = Bool(v); break;
                case "lastVersion": LastVersion = Str(v); break;
                case "runPresets": RunPresets = List(v); break;
                case "scriptIcons": ScriptIcons = List(v); break;
                case "toastOnFinish": legacyToast = Bool(v); break;
                case "toastAfterSec": ToastAfterSec = Str(v); break;
                case "updateCheck": UpdateCheck = Bool(v); break;
                case "updateLastCheck": UpdateLastCheck = Str(v); break;
                case "updateLatest": UpdateLatest = Str(v); break;
                case "jumpList": JumpList = Bool(v); break;
                case "listSort": ListSort = Str(v); break;
                case "listSortDesc": ListSortDesc = Bool(v); break;
                case "listView": ListView = Str(v); break;
                default: Unknown[p.Name] = v.Clone(); break;
            }
        }
        // A file from before 1.2.0: notifications were one switch (toastOnFinish)
        if (!notifySeen) Notify = legacyToast ? "always" : "off";
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
        if (!FavoritesSortChoices.Contains(FavoritesSort)) FavoritesSort = "manual";
        if (!Layout.FavoriteTiles.Any(t => t.Key == FavoritesSize)) FavoritesSize = Layout.DefaultFavoriteTile;
        if (ListView != "cards" && ListView != "compact") ListView = "cards";
        if (!RunModes.Defaults.Contains(DefaultMode)) DefaultMode = RunModes.DefaultMode;
        if (!Notifications.Levels.Contains(Notify)) Notify = "always";
        ParamPresets = ParamPresets.Where(l => ParamPreset.Parse(l) != null).ToList();
        Schedules = Schedules.Where(l => Schedule.Parse(l) != null).ToList();
        Engines = Engines.Where(e => Regex.IsMatch(e, @"^[^|]+\|(7|5\.1)$")).ToList();
        ScriptIcons = ScriptIcons.Where(e => Regex.IsMatch(e, @"^[^|]+\|[^|]+$")).ToList();
        ExternalFolders = ExternalScripts.Clean(ExternalFolders);
        PythonPath = PythonPath.Trim().Trim('"').Trim();
        if (!ExternalEditors.DefaultChoices.Contains(DefaultEditor)) DefaultEditor = "builtin";
        if (!ExternalEditors.EditorChoices.Contains(ExternalEditor)) ExternalEditor = "auto";
        ExternalEditorPath = ExternalEditorPath.Trim().Trim('"').Trim();
        ExternalEditorArgs = ExternalEditorArgs.Trim();
        CustomInterpreters = CustomInterpreters.Select(CustomInterpreter.Parse).Where(c => c != null)
            .GroupBy(c => c!.Id).Select(g => g.First()!).Take(Core.CustomInterpreters.MaxEntries).Select(c => c.ToLine()).ToList();
    }

    static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => ""
    };

    // Real booleans, and for hand-edited files: numbers (0 is false) and strings ("false", "0" and "" are false)
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
        else { string s = Str(v); if (s.Length > 0) res.Add(s); }   // a single value instead of an array
        return res;
    }

    // ---------- Save (one key per line, UTF-8 with BOM, CRLF) ----------
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
        AtomicFile.WriteAllText(path, ToJson(), new UTF8Encoding(true));
    }

    string ValueJson(string key) => key switch
    {
        "language" => JsonString(Language), "theme" => JsonString(Theme), "accent" => JsonString(Accent),
        "startMinimized" => B(StartMinimized), "closeToTray" => B(CloseToTray), "hotkey" => JsonString(Hotkey),
        "favorites" => L(Favorites), "trayHintShown" => B(TrayHintShown),
        "navCompact" => B(NavCompact), "outputCollapsed" => B(OutputCollapsed), "outputHeight" => JsonString(OutputHeight),
        "outputWrap" => B(OutputWrap), "historyFilter" => JsonString(HistoryFilter),
        "historyMax" => JsonString(HistoryMax), "historyOutputKB" => JsonString(HistoryOutputKB), "collapsedGroups" => L(CollapsedGroups),
        "compatDismissed" => JsonString(CompatDismissed),
        "safetyWarnings" => B(SafetyWarnings), "trusted" => L(Trusted), "lastRuns" => L(LastRuns), "engineDefault" => JsonString(EngineDefault),
        "engines" => L(Engines), "pwshOfferDismissed" => B(PwshOfferDismissed),
        "lastVersion" => JsonString(LastVersion), "runPresets" => L(RunPresets),
        "scriptIcons" => L(ScriptIcons), "toastOnFinish" => B(Notify != "off") /* still written from Notify: 1.1.x reads only this key */, "toastAfterSec" => JsonString(ToastAfterSec),
        "updateCheck" => B(UpdateCheck), "updateLastCheck" => JsonString(UpdateLastCheck), "updateLatest" => JsonString(UpdateLatest),
        "jumpList" => B(JumpList), "listSort" => JsonString(ListSort), "listSortDesc" => B(ListSortDesc), "listView" => JsonString(ListView),
        "trustedSources" => L(TrustedSources), "disabledBuiltinSources" => L(DisabledBuiltinSources), "disclaimerAccepted" => JsonString(DisclaimerAccepted), "externalFolders" => L(ExternalFolders),
        "pythonPath" => JsonString(PythonPath), "useVenv" => B(UseVenv), "favoritesSort" => JsonString(FavoritesSort), "favoritesSize" => JsonString(FavoritesSize),
        "defaultMode" => JsonString(DefaultMode), "notify" => JsonString(Notify), "paramPresets" => L(ParamPresets), "schedules" => L(Schedules),
        "customInterpreters" => L(CustomInterpreters),
        "defaultEditor" => JsonString(DefaultEditor), "externalEditor" => JsonString(ExternalEditor),
        "externalEditorPath" => JsonString(ExternalEditorPath), "externalEditorArgs" => JsonString(ExternalEditorArgs),
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

    /// <summary>Moves a favorite one place earlier (delta -1) or later (+1) in the manual order. False at either end
    /// or when rel is not a favorite.</summary>
    public bool MoveFavorite(string rel, int delta)
    {
        int i = Favorites.FindIndex(f => string.Equals(f, rel, StringComparison.OrdinalIgnoreCase));
        int j = i + Math.Sign(delta);
        if (i < 0 || delta == 0 || j < 0 || j >= Favorites.Count) return false;
        (Favorites[i], Favorites[j]) = (Favorites[j], Favorites[i]);
        return true;
    }

    public void SetFavorite(string rel, bool on)
    {
        Favorites = Favorites.Where(f => !string.Equals(f, rel, StringComparison.OrdinalIgnoreCase)).ToList();
        if (on) Favorites.Add(rel);
    }

    /// <summary>A script was renamed or moved: favorites, engine, icon, presets, schedules and trust follow it (null = deleted;
    /// its schedules stay until the app removed their tasks).</summary>
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
        Presets.MoveScript(this, oldRel, newRel);
        ScheduleList.MoveScript(this, oldRel, newRel);
        string tpre = oldRel.ToLowerInvariant() + "|";
        Trusted = Trusted.Where(t => !t.ToLowerInvariant().StartsWith(tpre)).ToList();
    }

    /// <summary>A folder is no longer shown (an external folder was removed): every per-script entry under it is forgotten.</summary>
    public void DropFolder(string folderRel)
    {
        string p = folderRel.TrimEnd('\\') + "\\";
        bool Under(string rel) => rel.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        bool UnderLine(string line) { int i = line.IndexOf('|'); return Under(i < 0 ? line : line.Substring(0, i)); }
        Favorites = Favorites.Where(f => !Under(f)).ToList();
        Engines = Engines.Where(l => !UnderLine(l)).ToList();
        ScriptIcons = ScriptIcons.Where(l => !UnderLine(l)).ToList();
        LastRuns = LastRuns.Where(l => !UnderLine(l)).ToList();
        Trusted = Trusted.Where(l => !UnderLine(l)).ToList();
        RunPresets = RunPresets.Where(l => RunPreset.Parse(l) is not { } r || !Under(r.Rel)).ToList();
        Presets.DropWhere(this, Under);
        CollapsedGroups = CollapsedGroups.Where(g => !Under(g + "\\")).ToList();
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
        Presets.MoveFolder(this, Fix);
        ScheduleList.MoveFolder(this, Fix);
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
