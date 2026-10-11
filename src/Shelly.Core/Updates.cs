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
        bool own = http == null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repo + "/releases/latest");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Shelly", "1.0"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.SendAsync(req, ct);
            if ((int)resp.StatusCode == 404) return null;   // no releases yet
            resp.EnsureSuccessStatusCode();
            return Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        finally { if (own) http.Dispose(); }
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
                    asset = a.TryGetProperty("browser_download_url", out var b) ? b.GetString() : null;
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
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
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
