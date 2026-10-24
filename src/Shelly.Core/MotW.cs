using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>A file's Mark of the Web: the NTFS alternate data stream <c>file:Zone.Identifier</c> that browsers, mail
/// and chat programs add to downloaded files.</summary>
public sealed class ZoneInfo
{
    /// <summary>0 local computer, 1 local intranet, 2 trusted sites, 3 internet, 4 restricted sites.</summary>
    public int ZoneId;
    public string ReferrerUrl = "";
    public string HostUrl = "";
    /// <summary>Internet or restricted: Shelly does not run or build the script until it is unblocked.</summary>
    public bool Blocks => ZoneId >= 3;
    /// <summary>Intranet or trusted sites: only an info note.</summary>
    public bool Note => ZoneId is 1 or 2;
    /// <summary>Where the file came from: the download URL, else the page it was downloaded from.</summary>
    public string Source => HostUrl.Length > 0 ? HostUrl : ReferrerUrl;
}

/// <summary>Reads and removes Mark of the Web. Shelly never removes it on its own: only the Unblock button of the
/// "Downloaded from the internet" dialog does. Copying, renaming and saving in the editor keep it.</summary>
public static class MotW
{
    public const string Stream = ":Zone.Identifier";

    /// <summary>Reads the Zone.Identifier stream of a file (null = none). Replaceable for tests: the stream exists only
    /// on NTFS under Windows.</summary>
    public static Func<string, string?> Reader = DefaultReader;
    /// <summary>Deletes the stream. Replaceable for tests.</summary>
    public static Action<string> Remover = DefaultRemover;
    /// <summary>Writes the stream back (the editor keeps a downloaded script marked after a save). Replaceable for tests.</summary>
    public static Action<string, string> Writer = DefaultWriter;

    static readonly Dictionary<string, (long Ticks, ZoneInfo? Zone)> cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object gate = new();

    static string? DefaultReader(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return File.ReadAllText(path + Stream); } catch { return null; }
    }

    static void DefaultRemover(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        File.Delete(path + Stream);
    }

    static void DefaultWriter(string path, string text)
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(path + Stream, text);
    }

    /// <summary>Parses the stream's text ([ZoneTransfer] ZoneId=3, ReferrerUrl=..., HostUrl=...). Null when there is no ZoneId.</summary>
    public static ZoneInfo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var z = new ZoneInfo();
        bool any = false;
        foreach (var raw in text.Split('\n'))
        {
            string l = raw.Trim().TrimStart('\uFEFF');
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
            if (k.Equals("ZoneId", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out int id)) { z.ZoneId = id; any = true; }
            else if (k.Equals("ReferrerUrl", StringComparison.OrdinalIgnoreCase)) z.ReferrerUrl = v;
            else if (k.Equals("HostUrl", StringComparison.OrdinalIgnoreCase)) z.HostUrl = v;
        }
        return any && z.ZoneId > 0 ? z : null;
    }

    /// <summary>The Mark of the Web of a file, or null (none, zone 0, not NTFS). Cached per file time; Unblock clears it.</summary>
    public static ZoneInfo? Read(string path)
    {
        long ticks;
        try { ticks = File.GetLastWriteTimeUtc(path).Ticks; } catch { return null; }
        lock (gate) if (cache.TryGetValue(path, out var c) && c.Ticks == ticks) return c.Zone;
        ZoneInfo? z;
        try { z = Parse(Reader(path)); } catch { z = null; }
        lock (gate) cache[path] = (ticks, z);
        return z;
    }

    public static void Forget(string path) { lock (gate) cache.Remove(path); }

    /// <summary>Removes the mark (the user's explicit Unblock). true = the file is no longer marked.</summary>
    public static bool Unblock(string path)
    {
        try { Remover(path); } catch { }
        Forget(path);
        return Read(path) == null;
    }

    /// <summary>Runs a write to the file and puts its mark back when the write dropped it.</summary>
    public static void KeepWhile(string path, Action write)
    {
        string? before = null;
        try { before = Reader(path); } catch { }
        write();
        try { if (before != null && Reader(path) == null) Writer(path, before); } catch { }
        Forget(path);
    }

    static readonly Regex LocalRef = new(@"(?im)^\s*(?:\.|&|Import-Module|ipmo)\s+(?:-Name\s+)?[""']?((?:\$PSScriptRoot|\$\{PSScriptRoot\}|\.{1,2})?[\\/]?[^""'\s|;)]+\.ps[md]?1)[""']?");

    /// <summary>Local scripts and modules this script dot-sources, calls or imports by a literal path (relative to the
    /// script or $PSScriptRoot) that carry a blocking mark. Paths built at run time are not followed.</summary>
    public static List<(string Path, ZoneInfo Zone)> MarkedReferences(string scriptPath, string text)
    {
        var res = new List<(string, ZoneInfo)>();
        string dir = System.IO.Path.GetDirectoryName(scriptPath) ?? "";
        foreach (Match m in LocalRef.Matches(text ?? ""))
        {
            string p = m.Groups[1].Value;
            p = Regex.Replace(p, @"^(\$PSScriptRoot|\$\{PSScriptRoot\})", ".", RegexOptions.IgnoreCase);
            if (p.Contains('$')) continue;
            p = p.Replace('\\', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);
            string full;
            try { full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(p) ? p : System.IO.Path.Combine(dir, p)); } catch { continue; }
            if (!File.Exists(full) || string.Equals(full, scriptPath, StringComparison.OrdinalIgnoreCase)) continue;
            var z = Read(full);
            if (z != null && z.Blocks && !res.Any(r => string.Equals(r.Item1, full, StringComparison.OrdinalIgnoreCase))) res.Add((full, z));
        }
        return res;
    }
}
