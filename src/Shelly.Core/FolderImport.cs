using System.Text.Json;

namespace Shelly.Core;

/// <summary>"Import folder…" (1.1.1): a script together with its own files is copied as a whole folder into Scripts. The copy
/// goes to a hidden temporary folder next to the target and is renamed into place at the end, so a failure or a cancel never
/// leaves half a folder. Python environments, __pycache__, .git and node_modules are left out; links and junctions are not
/// followed. The folder gets a small manifest (<see cref="ManifestName"/>) that marks it as imported and lists the scripts the
/// user unticked (they stay on disk but are not shown).</summary>
public static class FolderImport
{
    public const string ManifestName = ".shelly.json";
    /// <summary>Temporary copies are hidden dot folders inside Scripts (same volume: the final rename is atomic).</summary>
    public const string TempPrefix = ".import-";
    /// <summary>Above either one the dialog suggests adding the folder as an external folder instead.</summary>
    public const long LargeBytes = 100L * 1024 * 1024;
    public const int LargeFiles = 2000;
    /// <summary>Planning stops reading after this many files (the folder is then "large" anyway).</summary>
    public const int ScanLimit = 50_000;

    static readonly HashSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase) { ".git", "__pycache__", "node_modules" };

    public sealed class Plan
    {
        public string Source = "";
        /// <summary>The files to copy, relative to <see cref="Source"/> (with the OS separator).</summary>
        public List<string> Files { get; } = new();
        /// <summary>The scripts among them (.ps1 / .py that the Scripts list would show), relative, sorted.</summary>
        public List<string> Scripts { get; } = new();
        public long Bytes;
        /// <summary>Folders left out (environments, caches, .git, node_modules, too deep) and links not followed.</summary>
        public int SkippedFolders, SkippedLinks;
        /// <summary>Reading stopped at <see cref="ScanLimit"/>.</summary>
        public bool Truncated;
        public bool IsLarge => Truncated || Bytes > LargeBytes || Files.Count > LargeFiles;
    }

    /// <summary>A folder that is not copied: a Python environment (pyvenv.cfg), __pycache__, .git, node_modules.</summary>
    public static bool IsSkippedFolder(string dir)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
        if (SkippedNames.Contains(name)) return true;
        try { return File.Exists(Path.Combine(dir, "pyvenv.cfg")); } catch { return false; }
    }

    static bool IsLink(FileSystemInfo fi)
    {
        try { return (fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.LinkTarget != null; }
        catch { return true; }
    }

    /// <summary>Reads the folder (off the UI thread): what would be copied, how much, and which scripts are in it.</summary>
    public static Plan Scan(string source, CancellationToken ct = default)
    {
        var plan = new Plan { Source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) };
        void Walk(string dir, int depth)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> files, dirs;
            try { files = Directory.EnumerateFiles(dir).ToList(); dirs = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { plan.SkippedFolders++; return; }
            foreach (var f in files)
            {
                if (plan.Files.Count >= ScanLimit) { plan.Truncated = true; return; }
                var fi = new FileInfo(f);
                if (IsLink(fi)) { plan.SkippedLinks++; continue; }
                string rel = Path.GetRelativePath(plan.Source, f);
                if (string.Equals(fi.Name, ManifestName, StringComparison.OrdinalIgnoreCase) && depth == 0) continue;   // written anew
                plan.Files.Add(rel);
                plan.Bytes += fi.Length;
                if (IsListedScript(fi.Name)) plan.Scripts.Add(rel);
            }
            foreach (var d in dirs)
            {
                var di = new DirectoryInfo(d);
                if (IsLink(di)) { plan.SkippedLinks++; continue; }
                if (IsSkippedFolder(d) || depth + 1 > ScriptCatalog.MaxDepth) { plan.SkippedFolders++; continue; }
                Walk(d, depth + 1);
                if (plan.Truncated) return;
            }
        }
        Walk(plan.Source, 0);
        plan.Scripts.Sort(StringComparer.OrdinalIgnoreCase);
        return plan;
    }

    /// <summary>A file the Scripts list would show: .ps1 / .py, not hidden (dot) and not a private Python module.</summary>
    static bool IsListedScript(string name) => !name.StartsWith(".") && ScriptKinds.IsScript(name) && !ScriptKinds.IsHiddenPython(name);

    /// <summary>The source folder check: "" when it can be imported, else fiMissing, fiInsideScripts (it is already in
    /// Scripts) or fiContainsScripts (Scripts is inside it: the copy would copy itself).</summary>
    public static string CheckSource(string scriptsDir, string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "fiMissing";
        string src, scripts;
        try { src = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)); scripts = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scriptsDir)); }
        catch { return "fiMissing"; }
        if (!Directory.Exists(src)) return "fiMissing";
        if (string.Equals(src, scripts, StringComparison.OrdinalIgnoreCase) || IsInside(src, scripts)) return "fiInsideScripts";
        if (IsInside(scripts, src)) return "fiContainsScripts";
        return "";
    }

    /// <summary>The folder name check: "" when fine, else a string key (the ones of <see cref="ScriptCatalog.CheckFolderName"/>,
    /// or "fiExists" when Scripts already has a folder or file with that name).</summary>
    public static string CheckName(string scriptsDir, string? name, out string clean)
    {
        string err = ScriptCatalog.CheckFolderName(name, out clean);
        if (err.Length > 0) return err;
        string target = Path.Combine(scriptsDir, clean);
        if (Directory.Exists(target) || File.Exists(target)) return "fiExists";
        return "";
    }

    /// <summary>The default name: the source folder's name, cleaned, with " (2)", " (3)"… when it is taken.</summary>
    public static string SuggestName(string scriptsDir, string source)
    {
        string bas = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        if (bas is "" or "." or "..") bas = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)));
        bas = System.Text.RegularExpressions.Regex.Replace(bas, "[<>:\"/\\\\|?*\\x00-\\x1F]", "").Trim().TrimStart('.').TrimEnd('.', ' ');
        if (bas.Length == 0 || ScriptCatalog.IsReserved(bas)) bas = "Imported";
        if (bas.Length > 90) bas = bas[..90].TrimEnd('.', ' ');
        string name = bas;
        for (int n = 2; CheckName(scriptsDir, name, out _) == "fiExists" && n < 1000; n++) name = bas + " (" + n + ")";
        return name;
    }

    /// <summary>Copies the planned files to Scripts\<paramref name="name"/>: first into a hidden temporary folder, then one
    /// rename. On an error or a cancel the temporary folder is removed and nothing appears in Scripts. <paramref name="hidden"/>:
    /// the scripts (relative, as in <see cref="Plan.Scripts"/>) that were unticked. Returns the new folder.</summary>
    public static string Copy(Plan plan, string scriptsDir, string name, IEnumerable<string> hidden, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        string err = CheckName(scriptsDir, name, out string clean);
        if (err.Length > 0) throw new InvalidOperationException(err);
        Directory.CreateDirectory(scriptsDir);
        string target = Path.Combine(scriptsDir, clean);
        string temp = Path.Combine(scriptsDir, TempPrefix + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            var tdi = Directory.CreateDirectory(temp);
            try { tdi.Attributes |= FileAttributes.Hidden; } catch { }
            int i = 0;
            foreach (var rel in plan.Files)
            {
                ct.ThrowIfCancellationRequested();
                string src = Path.Combine(plan.Source, rel), dst = Path.Combine(temp, rel);
                if (!IsInside(dst, temp)) throw new InvalidOperationException("path outside the folder: " + rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, false);   // on Windows File.Copy keeps the Mark of the Web stream
                progress?.Report(++i);
            }
            new Manifest { Source = plan.Source, Imported = DateTime.UtcNow.ToString("o"), Hidden = NormalizeHidden(hidden) }.Write(temp);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException("fiExists");
            Directory.Move(temp, target);
            try { var di = new DirectoryInfo(target); di.Attributes &= ~FileAttributes.Hidden; } catch { }
            return target;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Temporary folders left by an import that was killed (power cut, crash); older than <paramref name="age"/>.</summary>
    public static int CleanupStale(string scriptsDir, TimeSpan age)
    {
        int n = 0;
        try
        {
            if (!Directory.Exists(scriptsDir)) return 0;
            foreach (var d in Directory.EnumerateDirectories(scriptsDir, TempPrefix + "*"))
            {
                var di = new DirectoryInfo(d);
                if (IsLink(di) || DateTime.UtcNow - di.CreationTimeUtc < age) continue;
                if (TryDelete(d)) n++;
            }
        }
        catch { }
        return n;
    }

    static bool TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return true; } catch { return false; }
    }

    static List<string> NormalizeHidden(IEnumerable<string> hidden) =>
        hidden.Select(h => h.Replace('/', '\\').Trim('\\')).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsInside(string path, string folder)
    {
        string p = Path.GetFullPath(path), f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Imported folders in Scripts ----------

    /// <summary>The manifest of an imported folder.</summary>
    public sealed class Manifest
    {
        public string Source = "";
        public string Imported = "";
        /// <summary>Scripts not shown in the list, relative to the folder, with backslashes.</summary>
        public List<string> Hidden = new();

        public bool IsHidden(string relInFolder) => Hidden.Contains(relInFolder.Replace('/', '\\'), StringComparer.OrdinalIgnoreCase);

        public static Manifest? Read(string folder)
        {
            string p = Path.Combine(folder, ManifestName);
            try
            {
                if (!File.Exists(p)) return null;
                var m = new Manifest();
                using var doc = JsonDocument.Parse(File.ReadAllText(p));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return m;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("source") && prop.Value.ValueKind == JsonValueKind.String) m.Source = prop.Value.GetString() ?? "";
                    else if (prop.NameEquals("imported") && prop.Value.ValueKind == JsonValueKind.String) m.Imported = prop.Value.GetString() ?? "";
                    else if (prop.NameEquals("hidden") && prop.Value.ValueKind == JsonValueKind.Array)
                        m.Hidden = NormalizeHidden(prop.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? ""));
                }
                return m;
            }
            catch { return File.Exists(p) ? new Manifest() : null; }   // a damaged manifest still marks the folder; nothing is hidden
        }

        public void Write(string folder)
        {
            var o = new Dictionary<string, object> { ["shellyFolder"] = 1, ["source"] = Source, ["imported"] = Imported, ["hidden"] = Hidden };
            string p = Path.Combine(folder, ManifestName);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, p, true);
        }
    }

    /// <summary>"Remove only this script from the list": adds it to the hidden list of its folder.</summary>
    public static void Hide(string folder, string scriptPath)
    {
        var m = Manifest.Read(folder) ?? new Manifest();
        string rel = Path.GetRelativePath(folder, scriptPath).Replace('/', '\\');
        if (!m.IsHidden(rel)) m.Hidden = NormalizeHidden(m.Hidden.Append(rel));
        m.Write(folder);
    }

    /// <summary>Files and bytes of a folder (links not followed), for the delete confirmation.</summary>
    public static (int Files, long Bytes) Measure(string folder)
    {
        int files = 0; long bytes = 0;
        void Walk(string dir, int depth)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir)) { var fi = new FileInfo(f); if (IsLink(fi)) continue; files++; bytes += fi.Length; }
                if (depth > 32) return;
                foreach (var d in Directory.EnumerateDirectories(dir)) { if (!IsLink(new DirectoryInfo(d))) Walk(d, depth + 1); }
            }
            catch { }
        }
        Walk(folder, 0);
        return (files, bytes);
    }

    /// <summary>Whether an imported folder may be deleted as a whole: strictly inside Scripts (never Scripts itself or something
    /// outside it), a real folder (not a link) with the manifest.</summary>
    public static bool CanDelete(string scriptsDir, string folder)
    {
        try
        {
            string s = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scriptsDir));
            string f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (!f.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            if (f.Substring(s.Length + 1).Split(Path.DirectorySeparatorChar).Any(p => p is "" or "." or "..")) return false;
            var di = new DirectoryInfo(f);
            return di.Exists && !IsLink(di) && File.Exists(Path.Combine(f, ManifestName));
        }
        catch { return false; }
    }

    /// <summary>"12.3 MB" / "12,3 МБ"; "840 KB".</summary>
    public static string SizeText(long bytes)
    {
        double kb = bytes / 1024.0, mb = kb / 1024.0, gb = mb / 1024.0;
        return gb >= 1 ? Loc.F("sizeGB", gb.ToString("0.#")) : mb >= 1 ? Loc.F("sizeMB", mb.ToString("0.#")) : Loc.F("sizeKB", Math.Max(1, Math.Ceiling(kb)).ToString("0"));
    }
}
