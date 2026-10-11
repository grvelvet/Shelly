using System.Security.Cryptography;

namespace Shelly.Core;

/// <summary>Safety warnings. Findings come from the analyzer (Analyzer.ps1, PowerShell's own parser; the source of truth)
/// and, until it has reported, from the built-in <see cref="FallbackScanner"/>. Severities: high (danger),
/// medium (warning), low (info; never asks). "Don't warn again for this version" = Scripts-relative path + SHA-256 of the
/// file, so any edit brings the check back. This is a guardrail that reads the script, not a sandbox.</summary>
public static class Safety
{
    public static string Level(IEnumerable<DangerFinding> findings)
    {
        var f = findings.ToList();
        if (f.Any(x => x.Sev == "high")) return "high";
        if (f.Any(x => x.Sev == "medium")) return "medium";
        return f.Count > 0 ? "low" : "";
    }

    /// <summary>Findings that ask before a run (danger and warning; info does not).</summary>
    public static List<DangerFinding> Blocking(IEnumerable<DangerFinding> findings) => findings.Where(f => f.Sev is "high" or "medium").ToList();

    static int Rank(string sev) => sev == "high" ? 0 : sev == "medium" ? 1 : 2;

    public static List<DangerFinding> Sorted(IEnumerable<DangerFinding> findings) =>
        findings.OrderBy(f => Rank(f.Sev)).ThenBy(f => f.Line).ToList();

    /// <summary>Download-and-run findings whose URL is a trusted source become info notices (rule DlTrusted).</summary>
    public static List<DangerFinding> ApplySources(IEnumerable<DangerFinding> findings, Settings s)
    {
        var res = new List<DangerFinding>();
        foreach (var f in findings)
        {
            string? src = f.Rule == "DlExec" ? TrustedSources.Match(f.Detail, s) : null;
            res.Add(src == null ? f : new DangerFinding { Rule = "DlTrusted", Sev = "low", Line = f.Line, Code = f.Code, Detail = src + " · " + f.Detail });
        }
        return res;
    }

    /// <summary>The remote sources of the download-and-run findings that could be trusted with one click (HTTPS URLs).</summary>
    public static List<string> SuggestSources(IEnumerable<DangerFinding> findings) =>
        findings.Where(f => f.Rule == "DlExec").Select(f => TrustedSources.Suggest(f.Detail)).Where(x => x != null).Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string FileSha256(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { return ""; }
    }

    public static string TrustKey(string rel, string path)
    {
        string h = FileSha256(path);
        return h.Length == 0 ? "" : rel.ToLowerInvariant() + "|" + h;
    }

    public static void AddTrust(Settings s, string rel, string key)
    {
        if (key.Length == 0) return;
        string pre = rel.ToLowerInvariant() + "|";
        s.Trusted = s.Trusted.Where(t => !t.StartsWith(pre, StringComparison.OrdinalIgnoreCase)).Append(key).ToList();
    }

    public static bool IsTrusted(Settings s, string key) => key.Length > 0 && s.Trusted.Any(t => string.Equals(t, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Can "don't warn again" be offered? Not for scripts with a danger (red) finding.</summary>
    public static bool CanTrust(IEnumerable<DangerFinding> findings) => Level(findings) != "high";

    /// <summary>Is the safety dialog needed before this action? action: inline | window | admin | build.
    /// Danger (red) findings ask on every run: they cannot be trusted and the Safety warnings switch does not turn
    /// them off. Administrator runs always ask (a confirmation when nothing was found); info findings never ask.</summary>
    public static bool Needed(Settings s, IReadOnlyCollection<DangerFinding> findings, string action, string rel, string path)
    {
        if (Level(findings) == "high") return true;
        if (!s.SafetyWarnings) return false;
        if (Blocking(findings).Count == 0 && action != "admin") return false;
        return !IsTrusted(s, TrustKey(rel, path));
    }
}

/// <summary>One trusted remote source: a host (with its subdomains) or, on shared hosts, a host plus a path prefix.</summary>
public sealed record TrustedSource(string Pattern, string Name, string Evidence);

/// <summary>Remote sources whose download-and-run one-liners are shown as info instead of a danger. Matching is HTTPS only:
/// an exact host or a subdomain of it; on shared hosts (GitHub, URL shorteners, file hosts) only host + path prefix,
/// never the whole host. Trusting a source means trusting whatever it serves at run time.</summary>
public static class TrustedSources
{
    /// <summary>Shipped with Shelly; each one is the install command documented by the project itself (Evidence).
    /// The user can turn any of them off in Settings.</summary>
    public static readonly TrustedSource[] BuiltIn =
    {
        new("christitus.com", "Chris Titus Tech WinUtil", "https://github.com/ChrisTitusTech/winutil#quick-start"),
        new("get.scoop.sh", "Scoop", "https://github.com/ScoopInstaller/Install#typical-installation"),
        new("community.chocolatey.org/install.ps1", "Chocolatey", "https://chocolatey.org/install"),
        new("aka.ms/install-powershell.ps1", "PowerShell (Microsoft)", "https://github.com/PowerShell/PowerShell/blob/master/tools/install-powershell.ps1-README.md"),
        new("raw.githubusercontent.com/PowerShell/PowerShell/", "PowerShell repository (Microsoft)", "https://github.com/PowerShell/PowerShell/blob/master/tools/install-powershell.ps1-README.md"),
        new("dot.net/v1/dotnet-install.ps1", ".NET install script (Microsoft)", "https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script"),
        new("astral.sh", "uv (Astral)", "https://docs.astral.sh/uv/getting-started/installation/"),
        new("deno.land/install.ps1", "Deno", "https://docs.deno.com/runtime/getting_started/installation/"),
        new("bun.sh", "Bun", "https://bun.sh/docs/installation"),
        new("bun.com", "Bun", "https://bun.sh/docs/installation"),
        new("get.pnpm.io", "pnpm", "https://pnpm.io/installation"),
        new("ohmyposh.dev", "Oh My Posh", "https://ohmyposh.dev/docs/installation/windows"),
    };

    /// <summary>Hosts that serve content of many unrelated owners: trusted only with a path prefix.</summary>
    public static readonly string[] SharedHosts =
    {
        "github.com", "raw.githubusercontent.com", "gist.github.com", "gist.githubusercontent.com", "objects.githubusercontent.com",
        "codeload.github.com", "github.io", "gitlab.com", "bitbucket.org", "codeberg.org", "sourceforge.net", "aka.ms", "bit.ly",
        "tinyurl.com", "t.co", "is.gd", "goo.gl", "pastebin.com", "paste.ee", "hastebin.com", "cdn.jsdelivr.net", "unpkg.com",
        "dropbox.com", "dl.dropboxusercontent.com", "1drv.ms", "onedrive.live.com", "drive.google.com", "docs.google.com",
        "blob.core.windows.net", "s3.amazonaws.com", "amazonaws.com", "cloudfront.net", "azureedge.net", "web.app", "firebaseapp.com",
        "pages.dev", "workers.dev", "netlify.app", "vercel.app", "herokuapp.com", "deno.land", "transfer.sh", "discord.com", "cdn.discordapp.com",
    };

    /// <summary>The trusted patterns in effect: built-ins the user did not turn off, then the user's own.</summary>
    public static List<string> Active(Settings s) =>
        BuiltIn.Select(b => b.Pattern).Where(p => !IsDisabled(s, p)).Concat(s.TrustedSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsDisabled(Settings s, string pattern) => s.DisabledBuiltinSources.Any(d => string.Equals(d, pattern, StringComparison.OrdinalIgnoreCase));

    public static bool IsBuiltIn(string pattern) => BuiltIn.Any(b => string.Equals(b.Pattern, pattern, StringComparison.OrdinalIgnoreCase));

    static bool IsShared(string host) => SharedHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));

    /// <summary>The URL as an absolute HTTPS URI; null for anything else (no scheme means http to Invoke-WebRequest).</summary>
    public static Uri? Https(string url)
    {
        url = (url ?? "").Trim().Trim('"', '\'');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps || u.UserInfo.Length > 0 || u.HostNameType != UriHostNameType.Dns) return null;
        return u;
    }

    /// <summary>The trusted pattern that covers this URL, or null.</summary>
    public static string? Match(string url, Settings s) => Match(url, Active(s));

    public static string? Match(string url, IEnumerable<string> patterns)
    {
        var u = Https(url);
        if (u == null) return null;
        string host = u.IdnHost.ToLowerInvariant().TrimEnd('.');
        string path = Uri.UnescapeDataString(u.AbsolutePath);
        if (path.Contains("/../") || path.EndsWith("/..")) return null;
        foreach (var raw in patterns)
        {
            var p = Normalize(raw);
            if (p == null) continue;
            int slash = p.IndexOf('/');
            string ph = slash < 0 ? p : p.Substring(0, slash);
            string pp = slash < 0 ? "" : p.Substring(slash);   // "/owner/repo/" or "/install.ps1"
            if (pp.Length == 0)
            {
                if (host == ph || host.EndsWith("." + ph, StringComparison.Ordinal)) return raw;
                continue;
            }
            if (host != ph) continue;
            if (pp.EndsWith("/") ? path.StartsWith(pp, StringComparison.OrdinalIgnoreCase)
                                 : path.Equals(pp, StringComparison.OrdinalIgnoreCase) || path.StartsWith(pp + "/", StringComparison.OrdinalIgnoreCase))
                return raw;
        }
        return null;
    }

    /// <summary>A pattern as the user typed it, cleaned: "https://Example.com/" -> "example.com". Null when it is not
    /// acceptable (http, a whole shared host, an IP address, no dot).</summary>
    public static string? Normalize(string input)
    {
        string t = (input ?? "").Trim().Trim('"', '\'');
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return null;
        if (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) t = t.Substring(8);
        if (t.StartsWith("*.")) t = t.Substring(2);
        int q = t.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) t = t.Substring(0, q);
        int slash = t.IndexOf('/');
        string host = (slash < 0 ? t : t.Substring(0, slash)).ToLowerInvariant().TrimEnd('.');
        string path = slash < 0 ? "" : t.Substring(slash);
        if (host.Length == 0 || !host.Contains('.') || host.Contains(':') || host.Contains('@') || Uri.CheckHostName(host) != UriHostNameType.Dns) return null;
        if (System.Net.IPAddress.TryParse(host, out _)) return null;
        while (path.Contains("//")) path = path.Replace("//", "/");
        if (path == "/") path = "";
        if (IsShared(host))
        {
            var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            bool git = host.Contains("github") || host is "gitlab.com" or "bitbucket.org" or "codeberg.org";
            // GitHub and co.: at least owner/repo; other shared hosts: a full path to one file
            if (git ? segs.Length < 2 : segs.Length < 1) return null;
            if (git && segs.Length == 2 && !path.EndsWith("/")) path += "/";
        }
        return host + path;
    }

    /// <summary>The pattern the "Trust this source" button offers for a URL: the host, or on shared hosts host/owner/repo/
    /// (GitHub) or the full path. Null for non-HTTPS URLs.</summary>
    public static string? Suggest(string url)
    {
        var u = Https(url);
        if (u == null) return null;
        string host = u.IdnHost.ToLowerInvariant().TrimEnd('.');
        if (!IsShared(host)) return Normalize(host);
        var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool git = host.Contains("github") || host is "gitlab.com" or "bitbucket.org" or "codeberg.org";
        if (git) return segs.Length >= 2 ? Normalize(host + "/" + segs[0] + "/" + segs[1] + "/") : null;
        return segs.Length >= 1 ? Normalize(host + u.AbsolutePath) : null;
    }

    public static void SetBuiltIn(Settings s, string pattern, bool on)
    {
        s.DisabledBuiltinSources = s.DisabledBuiltinSources.Where(d => !string.Equals(d, pattern, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!on) s.DisabledBuiltinSources.Add(pattern);
    }

    public static void Remove(Settings s, string pattern) =>
        s.TrustedSources = s.TrustedSources.Where(x => !string.Equals(x, pattern, StringComparison.OrdinalIgnoreCase)).ToList();

    public static void Add(Settings s, string pattern)
    {
        var p = Normalize(pattern);
        if (p == null) return;
        if (IsBuiltIn(p)) { s.DisabledBuiltinSources = s.DisabledBuiltinSources.Where(d => !string.Equals(d, p, StringComparison.OrdinalIgnoreCase)).ToList(); return; }
        if (!s.TrustedSources.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) s.TrustedSources.Add(p);
    }
}
