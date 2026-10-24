using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>Asks GitHub Releases for the latest version. It only reports: nothing is downloaded or installed.</summary>
public static class UpdateChecker
{
    public sealed record Release(string Version, string Tag, string Url, string Name, string? AssetUrl);

    public static bool IsRepoSet(string? repo) => !string.IsNullOrWhiteSpace(repo) && Regex.IsMatch(repo, @"^[\w.-]+/[\w.-]+$") && !repo.StartsWith("OWNER/", StringComparison.OrdinalIgnoreCase);

    public static async Task<Release?> GetLatestAsync(string repo, HttpClient? http = null, CancellationToken ct = default)
    {
        if (!IsRepoSet(repo)) return null;
        if (http != null) return await GetLatestAsync(http, repo, ct);
        using var own = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        return await GetLatestAsync(own, repo, ct);
    }

    /// <summary>The User-Agent GitHub asks for: Shelly and its version (1.3.1; it was a fixed "1.0").</summary>
    public static readonly string UserAgentVersion = AppVersion();

    static string AppVersion()
    {
        var v = typeof(UpdateChecker).Assembly.GetName().Version;
        return v == null ? "1.0" : v.ToString(3);
    }

    static async Task<Release?> GetLatestAsync(HttpClient http, string repo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repo + "/releases/latest");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Shelly", UserAgentVersion));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await http.SendAsync(req, ct);
        if ((int)resp.StatusCode == 404) return null;   // no releases yet
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    public static Release? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string url = r.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        string name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
        string? asset = null;
        if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string an = a.TryGetProperty("name", out var x) ? x.GetString() ?? "" : "";
                if (Regex.IsMatch(an, @"^shelly-[\d.]+\.zip$", RegexOptions.IgnoreCase))
                {
                    asset = a.TryGetProperty("browser_download_url", out var b) ? b.GetString() : null;
                    break;   // the first matching asset
                }
            }
        }
        string ver = tag.TrimStart('v', 'V');
        return ParseVersion(ver) == null ? null : new Release(ver, tag, url, name, asset);
    }

    public static Version? ParseVersion(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var m = Regex.Match(s.Trim().TrimStart('v', 'V'), @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        int patch = 0;
        // TryParse: a tag like v99999999999.0 is "not a version", not an exception
        if (!int.TryParse(m.Groups[1].Value, out int major) || !int.TryParse(m.Groups[2].Value, out int minor) ||
            (m.Groups[3].Success && !int.TryParse(m.Groups[3].Value, out patch))) return null;
        return new Version(major, minor, patch);
    }

    /// <summary>Hosts a download or release page may be opened from: GitHub (only below /owner/repo/), the release assets host,
    /// aka.ms and microsoft.com.</summary>
    static readonly string[] OtherHosts = { "objects.githubusercontent.com", "release-assets.githubusercontent.com", "aka.ms", "microsoft.com" };

    /// <summary>The address as a URI when it is safe to hand to the shell: absolute https, default port, no user name, one of the
    /// hosts above (github.com only under /repo/). Null otherwise.</summary>
    public static Uri? SafeUrl(string? url, string repo)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps || u.UserInfo.Length > 0 || !u.IsDefaultPort || u.HostNameType != UriHostNameType.Dns) return null;
        string host = u.IdnHost.ToLowerInvariant().TrimEnd('.');
        if (host == "github.com")
            return IsRepoSet(repo) && u.AbsolutePath.StartsWith("/" + repo + "/", StringComparison.OrdinalIgnoreCase) ? u : null;
        return OtherHosts.Any(h => host == h || (h == "microsoft.com" && host.EndsWith(".microsoft.com", StringComparison.Ordinal))) ? u : null;
    }

    public static bool IsNewer(string latest, string current)
    {
        var a = ParseVersion(latest);
        var b = ParseVersion(current);
        return a != null && b != null && a > b;
    }

    /// <summary>About once a day: at most every 20 hours, so a check at roughly the same time each day is not skipped (the time of the last check is in settings).</summary>
    public static bool Due(string lastCheckIso, DateTime now)
    {
        if (!DateTime.TryParse(lastCheckIso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) return true;
        return (now - t).TotalHours >= 20;
    }
}
