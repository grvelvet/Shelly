using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>A .ps1 file in the Scripts folder.</summary>
public sealed class ScriptFile
{
    public string Path = "";
    public string Rel = "";
    public string Name = "";
    /// <summary>Scripts-relative folder ("" for the root).</summary>
    public string Folder = "";
    public string Ticks = "";
    public DateTime Modified;
    public long Size;
}

public static class ScriptCatalog
{
    public const int MaxDepth = 8;

    /// <summary>All .ps1 files under Scripts (hidden names starting with a dot are skipped), sorted by folder then name.</summary>
    public static List<ScriptFile> Scan(AppPaths paths)
    {
        var list = new List<ScriptFile>();
        if (!Directory.Exists(paths.Scripts)) return list;
        Walk(paths, paths.Scripts, 0, list);
        return list.OrderBy(s => s.Folder, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static void Walk(AppPaths paths, string dir, int depth, List<ScriptFile> list)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.ps1"))
            {
                string name = System.IO.Path.GetFileName(f);
                if (name.StartsWith(".") || !name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) continue;
                var fi = new FileInfo(f);
                if ((fi.Attributes & FileAttributes.Hidden) != 0) continue;
                string rel = paths.Rel(f);
                list.Add(new ScriptFile
                {
                    Path = fi.FullName, Rel = rel, Name = System.IO.Path.GetFileNameWithoutExtension(name),
                    Folder = FolderOf(rel), Ticks = fi.LastWriteTimeUtc.Ticks.ToString(), Modified = fi.LastWriteTime, Size = fi.Length
                });
            }
            if (depth >= MaxDepth) return;
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var di = new DirectoryInfo(d);
                if (di.Name.StartsWith(".") || (di.Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
                Walk(paths, d, depth + 1, list);
            }
        }
        catch { }
    }

    /// <summary>All subfolders of Scripts (Scripts-relative), empty ones too.</summary>
    public static List<string> Folders(AppPaths paths)
    {
        var res = new List<string>();
        void Rec(string dir, int depth)
        {
            if (depth > MaxDepth) return;
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var di = new DirectoryInfo(d);
                    if (di.Name.StartsWith(".") || (di.Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
                    res.Add(paths.Rel(d));
                    Rec(d, depth + 1);
                }
            }
            catch { }
        }
        if (Directory.Exists(paths.Scripts)) Rec(paths.Scripts, 1);
        return res.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string FolderOf(string rel)
    {
        int i = rel.LastIndexOf('\\');
        return i < 0 ? "" : rel.Substring(0, i);
    }

    // ---------- Names ----------
    /// <summary>File name without .ps1: characters Windows does not allow removed, trailing dots and spaces trimmed.</summary>
    public static string? SafeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
        name = Regex.Replace(name, "[<>:\"/\\\\|?*\\x00-\\x1F]", "");
        name = name.Trim().TrimEnd('.').Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>CON, PRN, AUX, NUL, COM1-9, LPT1-9 (also with an extension) cannot be file names.</summary>
    public static bool IsReserved(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string b = name.Split('.')[0].Trim();
        return Regex.IsMatch(b, "^(con|prn|aux|nul|com[1-9\u00B9\u00B2\u00B3]|lpt[1-9\u00B9\u00B2\u00B3])$", RegexOptions.IgnoreCase);
    }

    public sealed record NameResult(string? Dir, string Name, string Error);

    /// <summary>'Folder\Sub\Name' -> where to create the script. Error: "" | escape | reserved | bad. Null for an empty answer.</summary>
    public static NameResult? ResolveNewScript(string scriptsDir, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        string a = answer.Trim();
        if (Regex.IsMatch(a, "^[A-Za-z]:") || a.StartsWith("\\") || a.StartsWith("/") || a.StartsWith("~")) return new(null, a, "escape");
        var parts = Regex.Split(a, @"[\\/]").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return new(null, a, "bad");
        if (parts.Any(p => Regex.IsMatch(p, @"^\.+$"))) return new(null, a, "escape");
        string? name = SafeName(parts[^1])?.TrimStart('.').Trim();
        if (string.IsNullOrEmpty(name)) return new(null, parts[^1], "bad");
        if (IsReserved(name)) return new(null, name, "reserved");
        var sub = new List<string>();
        foreach (var p in parts.Take(parts.Count - 1))
        {
            string? sp = SafeName(p)?.TrimStart('.').Trim();
            if (string.IsNullOrEmpty(sp)) return new(null, p, "bad");
            if (IsReserved(sp)) return new(null, sp, "reserved");
            sub.Add(sp);
        }
        string dir = sub.Count > 0 ? System.IO.Path.Combine(new[] { scriptsDir }.Concat(sub).ToArray()) : scriptsDir;
        string full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(dir));
        string bas = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(scriptsDir));
        if (!string.Equals(full, bas, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(bas + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return new(null, a, "escape");
        return new(dir, name, "");
    }

    /// <summary>A new name typed for Rename. Error: "" | escape | nofolder | reserved | bad. Null for an empty answer.</summary>
    public static NameResult? CheckRename(string? answer)
    {
        if (answer == null) return null;
        string a = answer.Trim();
        if (a.Length == 0) return null;
        if (Regex.IsMatch(a, "^[A-Za-z]:") || Regex.IsMatch(a, @"^\.+$") || Regex.IsMatch(a, @"(^|[\\/])\.\.([\\/]|$)")) return new(null, a, "escape");
        if (Regex.IsMatch(a, @"[\\/]")) return new(null, a, "nofolder");
        string? name = SafeName(a)?.TrimStart('.').Trim();
        if (string.IsNullOrEmpty(name)) return new(null, a, "bad");
        if (IsReserved(name)) return new(null, name, "reserved");
        return new(null, name, "");
    }

    /// <summary>Folder name check: returns the string key of the problem (fnEmpty, fnChars, fnDotOnly, fnHidden, fnTrail,
    /// fnReserved, fnLong) or "" when the name is fine.</summary>
    public static string CheckFolderName(string? name, out string clean)
    {
        clean = (name ?? "").Trim();
        if (clean.Length == 0) return "fnEmpty";
        if (Regex.IsMatch(clean, "[<>:\"/\\\\|?*\\x00-\\x1F]")) return "fnChars";
        if (Regex.IsMatch(clean, @"^\.+$")) return "fnDotOnly";
        if (clean.StartsWith(".")) return "fnHidden";
        if (clean.EndsWith(".") || clean.EndsWith(" ")) return "fnTrail";
        if (IsReserved(clean)) return "fnReserved";
        if (clean.Length > 100) return "fnLong";
        return "";
    }

    public static string Template(string name) =>
        Loc.T("templateDesc") + "\r\n\r\nWrite-Host " + Quoting.PsString(Loc.F("templateHello", name)) + "\r\n";
}
