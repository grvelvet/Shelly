namespace Shelly.Core;

/// <summary>Folder layout: Shelly.exe in the Shelly folder (portable), Scripts\ for scripts, build\ for built exes,
/// launcher-settings.json and launcher-history\ next to it, like the PowerShell edition. Shelly.exe inside a build\
/// folder without its own Scripts uses the parent folder (the layout of 0.9.x, where build\Shelly.exe lived).</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string Scripts => Path.Combine(Root, "Scripts");
    public string Build => Path.Combine(Root, "build");
    public string Settings => Path.Combine(Root, "launcher-settings.json");
    public string HistoryDir => Path.Combine(Root, "launcher-history");
    public string IconsDir => Path.Combine(Root, "assets", "script-icons");
    public string AppIcon => Path.Combine(Root, "assets", "shelly.ico");
    public string CacheDir { get; }

    public AppPaths(string root, string? cacheDir = null)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        CacheDir = cacheDir ?? Path.Combine(Path.GetTempPath(), "Shelly");
    }

    public static AppPaths FromExeFolder(string exeDir)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(exeDir));
        if (string.Equals(Path.GetFileName(root), "build", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(Path.Combine(root, "Scripts")))
        {
            string? parent = Path.GetDirectoryName(root);
            if (parent != null && Directory.Exists(Path.Combine(parent, "Scripts"))) root = parent;
        }
        return new AppPaths(root);
    }

    public void EnsureFolders()
    {
        foreach (var d in new[] { Scripts, Build, CacheDir }) { try { Directory.CreateDirectory(d); } catch { } }
    }

    /// <summary>Path relative to Scripts (Folder\Name.ps1), with backslashes like the PowerShell edition.</summary>
    public string Rel(string full, string? baseDir = null)
    {
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir ?? Scripts));
        string f = Path.GetFullPath(full);
        if (f.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) f = f.Substring(b.Length + 1);
        return f.Replace('/', '\\');
    }

    public string FullFromRel(string rel) => Path.Combine(Scripts, rel.Replace('\\', Path.DirectorySeparatorChar));
}
