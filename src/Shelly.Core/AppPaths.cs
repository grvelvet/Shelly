namespace Shelly.Core;

/// <summary>Folder layout: Shelly.exe in the Shelly folder (portable), Scripts\ for scripts, build\ for built exes,
/// launcher-settings.json and launcher-history\ next to it.</summary>
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

    public void EnsureFolders()
    {
        foreach (var d in new[] { Scripts, Build, CacheDir }) { try { Directory.CreateDirectory(d); } catch { } }
    }

    /// <summary>Path relative to Scripts (Folder\Name.ps1), with backslashes.</summary>
    public string Rel(string full, string? baseDir = null)
    {
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir ?? Scripts));
        string f = Path.GetFullPath(full);
        if (f.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) f = f.Substring(b.Length + 1);
        return f.Replace('/', '\\');
    }

    public string FullFromRel(string rel) => Path.Combine(Scripts, rel.Replace('\\', Path.DirectorySeparatorChar));
}
