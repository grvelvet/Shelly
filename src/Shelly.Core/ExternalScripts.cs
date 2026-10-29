using System.Security.Cryptography;
using System.Text;

namespace Shelly.Core;

/// <summary>Script folders outside Scripts that the user added in Settings. A script from such a folder has the Rel
/// "ext:&lt;id&gt;\path\inside\the\folder.ps1": the id comes from the full path of the folder, and a colon cannot be part of a
/// Windows file name, so this Rel never equals the Rel of a script in Scripts. Favorites, history, trust, presets, icons and
/// the engine choice all go by Rel and therefore never clash.</summary>
public static class ExternalScripts
{
    public const string Prefix = "ext:";
    public const int MaxFolders = 20;
    public const int MaxFilesPerFolder = 1000;
    public const int MaxDirectoriesPerFolder = 2000;

    /// <summary>One group of the script list: the key (a folder Rel), the folder on disk and the title of the group.</summary>
    public sealed record Group(string Key, string Root, string Title);

    public static bool IsExternalRel(string rel) => rel.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>12 hex digits of the SHA-256 of the lower-cased full path.</summary>
    public static string Id(string fullPath) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant())))[..12].ToLowerInvariant();

    /// <summary>The Rel of the folder itself (the top group of its scripts).</summary>
    public static string RootKey(string fullPath) => Prefix + Id(fullPath);

    /// <summary>true when <paramref name="path"/> is <paramref name="folder"/> or lies inside it.</summary>
    public static bool Within(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Why a folder cannot be added, as a string key (extInvalid, extNotAbsolute, extMissing, extDuplicate, extInsideScripts,
    /// extContainsScripts, extOverlap, extTooMany), or "" when it can. <paramref name="full"/> is the normalized path.</summary>
    public static string Check(string? input, string scriptsDir, IReadOnlyCollection<string> existing, out string full)
    {
        full = "";
        string text = input?.Trim() ?? "";
        if (text.Length == 0) return "extInvalid";
        string path;
        try
        {
            if (!Path.IsPathFullyQualified(text)) return "extNotAbsolute";
            path = Normalize(text);
        }
        catch { return "extInvalid"; }
        full = path;
        if (!Directory.Exists(path)) return "extMissing";
        string scripts = Normalize(scriptsDir);
        if (Within(path, scripts)) return "extInsideScripts";
        if (Within(scripts, path)) return "extContainsScripts";
        if (existing.Any(e => string.Equals(e, path, StringComparison.OrdinalIgnoreCase))) return "extDuplicate";
        if (existing.Any(e => Within(path, e) || Within(e, path))) return "extOverlap";
        if (existing.Count >= MaxFolders) return "extTooMany";
        return "";
    }

    /// <summary>The saved list as it is kept in the settings file: absolute paths only, normalized, no duplicates, at most
    /// <see cref="MaxFolders"/>. A folder that does not exist right now stays (a drive may just be unplugged).</summary>
    public static List<string> Clean(IEnumerable<string> paths)
    {
        var res = new List<string>();
        foreach (var p in paths)
        {
            string full;
            try
            {
                if (!Path.IsPathFullyQualified(p.Trim())) continue;
                full = Normalize(p.Trim());
            }
            catch { continue; }
            if (res.Count < MaxFolders && !res.Any(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase))) res.Add(full);
        }
        return res;
    }

    /// <summary>The folders a scan reads: existing ones that are not inside Scripts, do not contain it and do not overlap
    /// an earlier one (a script would show up twice).</summary>
    public static List<string> Usable(string scriptsDir, IEnumerable<string>? roots)
    {
        var res = new List<string>();
        if (roots == null) return res;
        string scripts = Normalize(scriptsDir);
        foreach (var r in Clean(roots))
        {
            if (Within(r, scripts) || Within(scripts, r) || res.Any(x => Within(r, x) || Within(x, r))) continue;
            if (Directory.Exists(r)) res.Add(r);
        }
        return res;
    }

    /// <summary>Full path of an external Rel (a folder or a script) or null when it does not belong to one of the folders.</summary>
    public static string? Resolve(IEnumerable<string> roots, string rel)
    {
        if (!IsExternalRel(rel)) return null;
        int i = rel.IndexOf('\\');
        string id = i < 0 ? rel.Substring(Prefix.Length) : rel.Substring(Prefix.Length, i - Prefix.Length);
        string? root = roots.FirstOrDefault(r => string.Equals(Id(r), id, StringComparison.OrdinalIgnoreCase));
        if (root == null) return null;
        if (i < 0) return root;
        var parts = rel.Substring(i + 1).Split('\\');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return null;
        return Path.Combine(root, Path.Combine(parts));
    }

    /// <summary>The top groups of the folders: the title is the folder name with a mark that it is an added folder (the full
    /// path when two folders have the same name).</summary>
    public static List<Group> Groups(IReadOnlyList<string> roots)
    {
        string NameOf(string r) { string n = Path.GetFileName(r); return n.Length > 0 ? n : r; }
        return roots.Select(r =>
        {
            string name = roots.Count(o => string.Equals(NameOf(o), NameOf(r), StringComparison.OrdinalIgnoreCase)) > 1 ? r : NameOf(r);
            return new Group(RootKey(r), r, name);
        }).ToList();
    }

    /// <summary>Group keys of the folders for the script list: each folder, then the subfolders that hold scripts (and their
    /// parents). Empty subfolders are not listed.</summary>
    public static List<string> FolderKeys(IReadOnlyList<string> roots, IEnumerable<ScriptFile> scripts)
    {
        var res = new List<string>();
        var list = scripts.Where(s => s.ExternalRoot.Length > 0).ToList();
        foreach (var root in roots)
        {
            string key = RootKey(root);
            res.Add(key);
            var sub = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in list.Where(s => string.Equals(s.ExternalRoot, root, StringComparison.OrdinalIgnoreCase)))
                for (string f = s.Folder; f.Length > key.Length && sub.Add(f); f = ScriptCatalog.FolderOf(f)) { }
            res.AddRange(sub.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
        }
        return res;
    }
}
