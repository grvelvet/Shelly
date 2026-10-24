using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>A script (.ps1 or .py) in the Scripts folder or in an external folder (<see cref="ExternalScripts"/>).</summary>
public sealed class ScriptFile
{
    public string Path = "";
    /// <summary>The identity of the script: the Scripts-relative path, or "ext:id\path" for an external folder.</summary>
    public string Rel = "";
    public string Name = "";
    /// <summary>The folder part of <see cref="Rel"/> ("" for the root of Scripts).</summary>
    public string Folder = "";
    /// <summary>The external folder the script comes from ("" for a script in Scripts).</summary>
    public string ExternalRoot = "";
    public bool IsExternal => ExternalRoot.Length > 0;
    /// <summary>A Python script (.py): started with the Python interpreter, not analyzed by PowerShell, not built into an .exe.</summary>
    public bool IsPython => ScriptKinds.IsPython(Path.Length > 0 ? Path : Rel);
    /// <summary>The language (<see cref="ScriptLangs"/>): ps, py, bat or custom.</summary>
    public string Lang => ScriptKinds.Lang(Path.Length > 0 ? Path : Rel);
    public bool IsPowerShell => Lang == ScriptLangs.PowerShell;
    /// <summary>A batch file (.bat / .cmd, 1.3.0): run by cmd.exe.</summary>
    public bool IsBatch => Lang == ScriptLangs.Batch;
    /// <summary>A script of a custom interpreter (1.3.0).</summary>
    public bool IsCustom => Lang == ScriptLangs.Custom;
    /// <summary>The script takes a free arguments line (everything but PowerShell): no parameter form, no PowerShell analysis,
    /// no .exe build, no administrator run of its own.</summary>
    public bool UsesArgs => !IsPowerShell;
    public string Ticks = "";
    /// <summary>SHA-256 of the file's content when it was scanned (hex). The analysis cache, "don't warn again" and the check
    /// before a run all go by this, not by the file time.</summary>
    public string Hash = "";
    public DateTime Modified;
    public long Size;
    /// <summary>The imported folder (Import folder…, with <see cref="FolderImport.ManifestName"/>) the script belongs to, else "".
    /// Such a script comes with its own files; deleting it offers to delete the whole folder.</summary>
    public string ProjectRoot = "";
    public bool InProject => ProjectRoot.Length > 0;

    /// <summary>The same script with another content hash (the file as it is on disk now).</summary>
    public ScriptFile WithHash(string hash) { var c = (ScriptFile)MemberwiseClone(); c.Hash = hash; return c; }
}

public static class ScriptCatalog
{
    public const int MaxDepth = 8;

    /// <summary>Where a scan reads from: Scripts, or an external folder with its limits.</summary>
    sealed class Source
    {
        public string Root = "";
        public string RelPrefix = "";
        public bool External;
        public int Files, Directories;
    }

    /// <summary>All .ps1 and .py files under Scripts and under the external folders (hidden names starting with a dot are skipped,
    /// and so are private Python modules starting with "_"), sorted by folder then name. In an external folder links (reparse
    /// points) are not followed and the number of files and folders read is limited; a folder that cannot be read is skipped.
    /// Python environments (a folder with pyvenv.cfg), __pycache__, site-packages and node_modules are not read.</summary>
    public static List<ScriptFile> Scan(AppPaths paths, IEnumerable<string>? external = null)
    {
        var list = new List<ScriptFile>();
        if (Directory.Exists(paths.Scripts)) Walk(paths, new Source { Root = paths.Scripts }, paths.Scripts, 0, list, null);
        foreach (var root in ExternalScripts.Usable(paths.Scripts, external))
        {
            var src = new Source { Root = root, RelPrefix = ExternalScripts.RootKey(root) + "\\", External = true };
            Walk(paths, src, root, 0, list, null);
        }
        return list.OrderBy(s => s.Folder, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>An imported folder being walked: its root and the scripts unticked at import.</summary>
    sealed record Project(string Root, FolderImport.Manifest Manifest);

    static void Walk(AppPaths paths, Source src, string dir, int depth, List<ScriptFile> list, Project? proj)
    {
        if (src.External && ++src.Directories > ExternalScripts.MaxDirectoriesPerFolder) return;
        try
        {
            // An imported folder in Scripts (not in external folders, and not nested in another one)
            if (!src.External && proj == null && depth > 0 && File.Exists(System.IO.Path.Combine(dir, FolderImport.ManifestName)))
                proj = new Project(dir, FolderImport.Manifest.Read(dir) ?? new FolderImport.Manifest());
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                string name = System.IO.Path.GetFileName(f);
                if (name.StartsWith(".") || !ScriptKinds.IsScript(name) || ScriptKinds.IsHiddenPython(name)) continue;
                var fi = new FileInfo(f);
                if ((fi.Attributes & FileAttributes.Hidden) != 0) continue;
                if (src.External && ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || ++src.Files > ExternalScripts.MaxFilesPerFolder)) continue;
                if (proj != null && proj.Manifest.IsHidden(System.IO.Path.GetRelativePath(proj.Root, fi.FullName))) continue;
                string rel = src.RelPrefix + paths.Rel(f, src.Root);
                list.Add(new ScriptFile
                {
                    Path = fi.FullName, Rel = rel, ExternalRoot = src.External ? src.Root : "", Name = System.IO.Path.GetFileNameWithoutExtension(name),
                    Folder = FolderOf(rel), Ticks = fi.LastWriteTimeUtc.Ticks.ToString(), Hash = Safety.FileSha256(fi.FullName), Modified = fi.LastWriteTime, Size = fi.Length,
                    ProjectRoot = proj?.Root ?? ""
                });
            }
            if (depth >= MaxDepth) return;
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var di = new DirectoryInfo(d);
                if (di.Name.StartsWith(".") || (di.Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0 || SkipFolder(d)) continue;
                Walk(paths, src, d, depth + 1, list, proj);
            }
        }
        catch { }
    }

    static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "__pycache__", "site-packages", "node_modules" };

    /// <summary>A folder whose files are not scripts to run: a Python virtual environment (it has pyvenv.cfg), __pycache__,
    /// site-packages, node_modules.</summary>
    public static bool SkipFolder(string dir)
    {
        if (SkippedFolders.Contains(System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(dir)))) return true;
        try { return File.Exists(System.IO.Path.Combine(dir, "pyvenv.cfg")); } catch { return false; }
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
    /// <summary>File name without .ps1 or .py: characters Windows does not allow removed, trailing dots and spaces trimmed.</summary>
    public static string? SafeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = ScriptKinds.StripExt(name, out _);
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

    /// <summary>Ext: the script extension the typed name asks for (".py", ".bat", ".cmd" or a custom interpreter's when the
    /// name ends with it, else ".ps1").</summary>
    public sealed record NameResult(string? Dir, string Name, string Error, string Ext = ScriptKinds.Ps1);

    /// <summary>'Folder\Sub\Name' -> where to create the script ("Name.py" makes a Python script). Error: "" | escape | reserved | bad.
    /// Null for an empty answer.</summary>
    public static NameResult? ResolveNewScript(string scriptsDir, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        string a = answer.Trim();
        if (Regex.IsMatch(a, "^[A-Za-z]:") || a.StartsWith("\\") || a.StartsWith("/") || a.StartsWith("~")) return new(null, a, "escape");
        var parts = Regex.Split(a, @"[\\/]").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return new(null, a, "bad");
        if (parts.Any(p => Regex.IsMatch(p, @"^\.+$"))) return new(null, a, "escape");
        string? name = SafeName(parts[^1])?.TrimStart('.').Trim();
        string ext = ScriptKinds.Ext(parts[^1]) ?? ScriptKinds.Ps1;
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
        return new(dir, name, "", ext);
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

    /// <summary>What to do with files dropped on the Scripts page: Copy = source and target (a new file in the Scripts folder),
    /// Ignored = not .ps1 or .py (or not usable as a name), Already = already inside the Scripts folder or the same file is there.</summary>
    public sealed class DropPlan
    {
        public List<(string Source, string Target)> Copy { get; } = new();
        public int Ignored;
        public int Already;
    }

    /// <summary>Only script files (.ps1, .py, .bat, .cmd and the custom interpreters' extensions) are taken (each keeps its extension); an existing file is never overwritten (an identical one counts as already there,
    /// another one with the same name gets " (2)", " (3)" ...). Nothing is copied or run here.</summary>
    public static DropPlan PlanDrop(string scriptsDir, IEnumerable<string> dropped)
    {
        var plan = new DropPlan();
        string bas = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(scriptsDir));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in dropped)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string full;
            try { full = System.IO.Path.GetFullPath(raw); } catch { plan.Ignored++; continue; }
            if (!seen.Add(full)) continue;
            string? ext = ScriptKinds.Ext(System.IO.Path.GetExtension(full));
            if (!File.Exists(full) || ext == null || !string.Equals(System.IO.Path.GetExtension(full), ext, StringComparison.OrdinalIgnoreCase) ||
                ScriptKinds.IsHiddenPython(System.IO.Path.GetFileName(full))) { plan.Ignored++; continue; }
            if (full.StartsWith(bas + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { plan.Already++; continue; }
            string name = System.IO.Path.GetFileNameWithoutExtension(full).TrimStart('.').Trim();
            if (name.Length == 0 || IsReserved(name)) { plan.Ignored++; continue; }
            string target = System.IO.Path.Combine(scriptsDir, name + ext);
            bool same = false;
            for (int n = 2; (File.Exists(target) || taken.Contains(target)) && !same; n++)
            {
                if (File.Exists(target) && SameContent(full, target)) same = true;
                else target = System.IO.Path.Combine(scriptsDir, name + " (" + n + ")" + ext);
            }
            if (same) { plan.Already++; continue; }
            taken.Add(target);
            plan.Copy.Add((full, target));
        }
        return plan;
    }

    static bool SameContent(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch { return false; }
    }

    /// <summary>The text of a new script; <paramref name="ext"/> ".py" gives a Python script, ".bat" / ".cmd" a batch file,
    /// a custom interpreter's extension an empty file.</summary>
    public static string Template(string name, string ext = ScriptKinds.Ps1) => ext == ScriptKinds.Py ? PythonRun.Template(name) :
        ext is ScriptKinds.Bat or ScriptKinds.Cmd ? BatchRun.Template(name) : ext != ScriptKinds.Ps1 ? "" :
        Loc.T("templateDesc") + "\r\n\r\nWrite-Host " + Quoting.PsString(Loc.F("templateHello", name)) + "\r\n";
}
