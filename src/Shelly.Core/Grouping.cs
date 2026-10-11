namespace Shelly.Core;

/// <summary>One line of the script list: a group header (favorites, root scripts, a folder) or a script under it.</summary>
public readonly record struct ListEntry(bool IsGroup, string Key, string Title, int Count, int Depth, bool Collapsed, ScriptFile? File);

/// <summary>Builds the grouped script list (UI-free, so it can be measured and tested). One pass over the scripts:
/// they are bucketed by folder first, so the work is O(scripts + folders) instead of O(scripts × folders).</summary>
public static class ScriptGrouping
{
    public static List<ListEntry> Build(IReadOnlyList<ScriptFile> scripts, IReadOnlyList<string> folders, IEnumerable<ScriptFile> favorites,
        ICollection<string> collapsed, string favTitle, string rootTitle, Func<IEnumerable<ScriptFile>, IEnumerable<ScriptFile>>? order = null)
    {
        order ??= x => x;
        var res = new List<ListEntry>(scripts.Count + folders.Count + 8);
        var coll = collapsed as HashSet<string> ?? new HashSet<string>(collapsed, StringComparer.OrdinalIgnoreCase);
        var favs = favorites.ToList();
        bool groups = favs.Count > 0 || folders.Count > 0;

        // Scripts per folder (in list order) and the total under each folder, subfolders included
        var byFolder = new Dictionary<string, List<ScriptFile>>(StringComparer.OrdinalIgnoreCase);
        var total = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in scripts)
        {
            if (!byFolder.TryGetValue(s.Folder, out var l)) byFolder[s.Folder] = l = new List<ScriptFile>();
            l.Add(s);
            for (string f = s.Folder; f.Length > 0; f = ScriptCatalog.FolderOf(f))
                total[f] = total.TryGetValue(f, out var n) ? n + 1 : 1;
        }

        if (favs.Count > 0)
        {
            bool c = coll.Contains("*fav");
            res.Add(new ListEntry(true, "*fav", favTitle, favs.Count, 0, c, null));
            if (!c) foreach (var f in order(favs)) res.Add(new ListEntry(false, "*fav|" + f.Rel, f.Name, 0, 0, false, f));
        }
        var root = byFolder.TryGetValue("", out var r) ? r : new List<ScriptFile>();
        if (groups && root.Count > 0)
        {
            bool c = coll.Contains("*root");
            res.Add(new ListEntry(true, "", rootTitle, root.Count, 0, c, null));
            if (!c) foreach (var f in order(root)) res.Add(new ListEntry(false, f.Rel, f.Name, 0, 0, false, f));
        }
        else foreach (var f in order(root)) res.Add(new ListEntry(false, f.Rel, f.Name, 0, 0, false, f));

        foreach (var folder in folders)
        {
            bool hidden = false;   // a parent folder is collapsed
            for (int i = folder.IndexOf('\\'); i > 0; i = folder.IndexOf('\\', i + 1))
                if (coll.Contains(folder.Substring(0, i))) { hidden = true; break; }
            if (hidden) continue;
            int depth = 0;
            foreach (char ch in folder) if (ch == '\\') depth++;
            bool c = coll.Contains(folder);
            res.Add(new ListEntry(true, folder, folder.Substring(folder.LastIndexOf('\\') + 1), total.TryGetValue(folder, out var n) ? n : 0, depth, c, null));
            if (c || !byFolder.TryGetValue(folder, out var list)) continue;
            foreach (var f in order(list)) res.Add(new ListEntry(false, f.Rel, f.Name, 0, depth + 1, false, f));
        }
        return res;
    }

    /// <summary>The folders that hold at least one of <paramref name="scripts"/> (directly or below), with their parents,
    /// in the order of <paramref name="allFolders"/>: with a filter on, empty groups are left out.</summary>
    public static List<string> FoldersWith(IEnumerable<ScriptFile> scripts, IReadOnlyList<string> allFolders)
    {
        var need = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in scripts)
            for (string f = s.Folder; f.Length > 0 && need.Add(f); f = ScriptCatalog.FolderOf(f)) { }
        return allFolders.Where(need.Contains).ToList();
    }

    /// <summary>Keys of every group the list can show ("*fav", "*root" and the folders), as stored in collapsedGroups.</summary>
    public static List<string> GroupKeys(IReadOnlyList<ScriptFile> scripts, IReadOnlyList<string> folders, bool hasFavorites)
    {
        var keys = new List<string>();
        if (hasFavorites) keys.Add("*fav");
        if (scripts.Any(s => s.Folder.Length == 0) && (hasFavorites || folders.Count > 0)) keys.Add("*root");
        keys.AddRange(folders);
        return keys;
    }

    /// <summary>Search: every word must appear in the name, folder or description; name-prefix matches first.</summary>
    public static List<ScriptFile> Search(IEnumerable<ScriptFile> scripts, string query, Func<ScriptFile, string> desc)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return new();
        return scripts
            .Where(f => tokens.All(t => f.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || f.Folder.Contains(t, StringComparison.OrdinalIgnoreCase) || desc(f).Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Name.StartsWith(tokens[0], StringComparison.OrdinalIgnoreCase) ? 0 : f.Name.Contains(tokens[0], StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>What the sort and the filters need to know about a script (taken from settings, history and the analysis).</summary>
public readonly record struct ScriptFacts(DateTime? LastRun, int RunCount, bool Needs51, bool HasDanger, bool AdminMode, bool Favorite);

/// <summary>Sort order inside each group of the script list (favorites stay a group on top).</summary>
public static class ScriptOrder
{
    /// <summary>key: name | modified | lastrun | runcount. Ties go by name (A to Z); scripts never run stay at the end for
    /// "last run" in both directions.</summary>
    public static IEnumerable<ScriptFile> Sort(IEnumerable<ScriptFile> list, string key, bool desc, Func<ScriptFile, ScriptFacts> facts)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        switch (key)
        {
            case "modified":
                return (desc ? list.OrderByDescending(f => f.Modified) : list.OrderBy(f => f.Modified)).ThenBy(f => f.Name, byName);
            case "lastrun":
            {
                var l = list.Select(f => (f, t: facts(f).LastRun)).ToList();
                var ran = l.Where(x => x.t != null);
                var ordered = desc ? ran.OrderByDescending(x => x.t) : ran.OrderBy(x => x.t);
                return ordered.ThenBy(x => x.f.Name, byName).Select(x => x.f)
                    .Concat(l.Where(x => x.t == null).Select(x => x.f).OrderBy(f => f.Name, byName));
            }
            case "runcount":
                return (desc ? list.OrderByDescending(f => facts(f).RunCount) : list.OrderBy(f => facts(f).RunCount)).ThenBy(f => f.Name, byName);
            default:
                return desc ? list.OrderByDescending(f => f.Name, byName) : list.OrderBy(f => f.Name, byName);
        }
    }
}

/// <summary>Filters of the script list. "Ran before" and "Never run" widen each other (either one); every other
/// filter narrows the list (all of them must match).</summary>
public static class ScriptFilters
{
    public static readonly string[] All = { "ran", "never", "ps51", "danger", "admin", "fav" };

    public static bool Matches(ScriptFacts f, IReadOnlyCollection<string> on)
    {
        if (on.Count == 0) return true;
        bool ran = on.Contains("ran"), never = on.Contains("never");
        if ((ran || never) && !((ran && f.LastRun != null) || (never && f.LastRun == null))) return false;
        if (on.Contains("ps51") && !f.Needs51) return false;
        if (on.Contains("danger") && !f.HasDanger) return false;
        if (on.Contains("admin") && !f.AdminMode) return false;
        if (on.Contains("fav") && !f.Favorite) return false;
        return true;
    }
}

/// <summary>Shelly directives in a script's leading comments, e.g. <c># Shelly-Icon: E9D2</c> (a Segoe Fluent Icons
/// code point used as the script's default icon). Only the first lines are read, and results are cached per file time.</summary>
public static class ScriptHeader
{
    static readonly Dictionary<string, string> cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object gate = new();
    public const string DefaultGlyph = "\uE756";

    /// <summary>The glyph of <c># Shelly-Icon: XXXX</c> as a string, or null.</summary>
    public static string? IconGlyph(string text)
    {
        int n = 0;
        bool inBlock = false;   // inside a <# ... #> help block, which may come before the directive
        foreach (var raw in text.Split('\n'))
        {
            if (n++ >= 40) break;
            string t = raw.Trim().TrimStart('\uFEFF');
            if (inBlock) { if (t.Contains("#>")) inBlock = false; continue; }
            if (t.Length == 0) continue;
            if (t.StartsWith("<#")) { inBlock = !t.Substring(2).Contains("#>"); continue; }
            if (!t.StartsWith("#")) break;
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^#\s*Shelly-Icon\s*:\s*(?:U\+|0x|&#x|\\u)?([0-9A-Fa-f]{4,5});?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int cp = Convert.ToInt32(m.Groups[1].Value, 16);
                // Private-use area only: that is where the icon fonts keep their symbols
                if (cp is >= 0xE000 and <= 0xF8FF) return char.ConvertFromUtf32(cp);
                return null;
            }
        }
        return null;
    }

    /// <summary>The run mode of <c># Shelly-Mode: inline | window | admin</c> (the default for runs from the list and the
    /// jump list when no mode was remembered), or null. Administrator runs still ask and show the UAC prompt.</summary>
    public static string? ModeIn(string text)
    {
        int n = 0;
        bool inBlock = false;
        foreach (var raw in text.Split('\n'))
        {
            if (n++ >= 40) break;
            string t = raw.Trim().TrimStart('\uFEFF');
            if (inBlock) { if (t.Contains("#>")) inBlock = false; continue; }
            if (t.Length == 0) continue;
            if (t.StartsWith("<#")) { inBlock = !t.Substring(2).Contains("#>"); continue; }
            if (!t.StartsWith("#")) break;
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^#\s*Shelly-Mode\s*:\s*(inline|window|admin)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    public static string? ModeOf(ScriptFile f)
    {
        try
        {
            using var r = new StreamReader(f.Path);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 40 && r.ReadLine() is { } l; i++) sb.Append(l).Append('\n');
            return ModeIn(sb.ToString());
        }
        catch { return null; }
    }

    /// <summary>True for a leading comment line that is a Shelly directive (not a description).</summary>
    public static bool IsDirective(string commentLine) =>
        System.Text.RegularExpressions.Regex.IsMatch(commentLine, @"^#\s*Shelly-\w+\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The script's icon glyph from its header (the default script glyph when none), cached by path + file time.</summary>
    public static string GlyphOf(ScriptFile f)
    {
        string key = f.Path + "|" + f.Ticks;
        lock (gate) if (cache.TryGetValue(key, out var g)) return g;
        string glyph = DefaultGlyph;
        try
        {
            using var sr = new StreamReader(f.Path, detectEncodingFromByteOrderMarks: true);
            var buf = new char[4096];
            int len = sr.ReadBlock(buf, 0, buf.Length);
            glyph = IconGlyph(new string(buf, 0, len)) ?? DefaultGlyph;
        }
        catch { }
        lock (gate) cache[key] = glyph;
        return glyph;
    }
}
