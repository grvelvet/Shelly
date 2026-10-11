using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.0.11 safety features without PowerShell: trusted sources, Mark of the Web, settings, severities.</summary>
public class SafetyFeaturesTests
{
    // ---------- Trusted sources ----------

    [Theory]
    [InlineData("https://christitus.com/win", "christitus.com")]
    [InlineData("https://www.christitus.com/win", "christitus.com")]          // subdomain
    [InlineData("https://CHRISTITUS.COM/win", "christitus.com")]
    [InlineData("https://get.scoop.sh", "get.scoop.sh")]
    [InlineData("https://community.chocolatey.org/install.ps1", "community.chocolatey.org/install.ps1")]
    [InlineData("https://raw.githubusercontent.com/PowerShell/PowerShell/master/tools/install-powershell.ps1", "raw.githubusercontent.com/PowerShell/PowerShell/")]
    [InlineData("https://astral.sh/uv/install.ps1", "astral.sh")]
    public void Built_in_sources_match(string url, string pattern)
    {
        Assert.Equal(pattern, TrustedSources.Match(url, new Settings()));
    }

    [Theory]
    [InlineData("http://christitus.com/win")]                        // plain http is never trusted
    [InlineData("christitus.com/win")]                               // no scheme = http for irm
    [InlineData("https://christitus.com.evil.example/win")]           // not a subdomain
    [InlineData("https://evilchristitus.com/win")]
    [InlineData("https://christitus.com@evil.example/win")]           // user info trick
    [InlineData("https://raw.githubusercontent.com/evil/PowerShell/x.ps1")]
    [InlineData("https://raw.githubusercontent.com/PowerShell/PowerShellEvil/x.ps1")]
    [InlineData("https://community.chocolatey.org/other.ps1")]
    [InlineData("https://massgrave.dev/get")]
    [InlineData("https://get.activated.win")]
    [InlineData("https://1.2.3.4/x.ps1")]
    public void Untrusted_urls_do_not_match(string url)
    {
        Assert.Null(TrustedSources.Match(url, new Settings()));
    }

    [Fact]
    public void Built_ins_can_be_turned_off_and_user_entries_added()
    {
        var s = new Settings();
        TrustedSources.SetBuiltIn(s, "christitus.com", false);
        Assert.Null(TrustedSources.Match("https://christitus.com/win", s));
        Assert.Contains("christitus.com", s.DisabledBuiltinSources);
        TrustedSources.Add(s, "christitus.com");                    // adding a built-in turns it back on
        Assert.Equal("christitus.com", TrustedSources.Match("https://christitus.com/win", s));
        Assert.Empty(s.TrustedSources);
        TrustedSources.Add(s, "https://Example.org/");
        Assert.Equal(new[] { "example.org" }, s.TrustedSources);
        Assert.Equal("example.org", TrustedSources.Match("https://cdn.example.org/a.ps1", s));
        TrustedSources.Remove(s, "example.org");
        Assert.Empty(s.TrustedSources);
    }

    [Theory]
    [InlineData("http://example.org", null)]
    [InlineData("github.com", null)]                                  // a whole shared host
    [InlineData("raw.githubusercontent.com", null)]
    [InlineData("github.com/owner", null)]
    [InlineData("github.com/owner/repo", "github.com/owner/repo/")]
    [InlineData("https://raw.githubusercontent.com/o/r/main/x.ps1", "raw.githubusercontent.com/o/r/main/x.ps1")]
    [InlineData("pastebin.com", null)]
    [InlineData("127.0.0.1", null)]
    [InlineData("localhost", null)]
    [InlineData("*.example.org", "example.org")]
    public void Normalize_rejects_risky_patterns(string input, string? expected)
    {
        Assert.Equal(expected, TrustedSources.Normalize(input));
    }

    [Theory]
    [InlineData("https://example.org/a/b.ps1", "example.org")]
    [InlineData("https://raw.githubusercontent.com/o/r/main/x.ps1", "raw.githubusercontent.com/o/r/")]
    [InlineData("http://example.org/x", null)]
    public void Suggest_offers_host_or_owner_repo(string url, string? expected)
    {
        Assert.Equal(expected, TrustedSources.Suggest(url));
    }

    [Fact]
    public void Every_built_in_is_valid_https_with_evidence_and_no_piracy_tools()
    {
        foreach (var b in TrustedSources.BuiltIn)
        {
            Assert.Equal(b.Pattern, TrustedSources.Normalize(b.Pattern));
            Assert.StartsWith("https://", b.Evidence);
            Assert.DoesNotContain("massgrave", b.Pattern);
            Assert.DoesNotContain("activated", b.Pattern);
        }
    }

    [Fact]
    public void Trusted_download_becomes_info_and_keeps_the_url()
    {
        var s = new Settings();
        var f = new List<DangerFinding>
        {
            new() { Rule = "DlExec", Sev = "high", Line = 1, Detail = "https://christitus.com/win" },
            new() { Rule = "DlExec", Sev = "high", Line = 2, Detail = "https://evil.example/x" },
        };
        var r = Safety.ApplySources(f, s);
        Assert.Equal("DlTrusted", r[0].Rule);
        Assert.Equal("low", r[0].Sev);
        Assert.Contains("https://christitus.com/win", r[0].Detail);
        Assert.Equal("DlExec", r[1].Rule);
        Assert.Equal("high", Safety.Level(r));
        Assert.Equal(new[] { "evil.example" }, Safety.SuggestSources(r));
        Assert.Empty(Safety.Blocking(r.Take(1)));
    }

    // ---------- Mark of the Web (fake reader: the stream exists only on NTFS under Windows) ----------

    [Fact]
    public void Parses_zone_identifier()
    {
        var z = MotW.Parse("\uFEFF[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.org/page\r\nHostUrl=https://example.org/x.ps1\r\n");
        Assert.NotNull(z);
        Assert.Equal(3, z!.ZoneId);
        Assert.True(z.Blocks);
        Assert.Equal("https://example.org/x.ps1", z.Source);
        Assert.Equal("https://example.org/page", z.ReferrerUrl);
        Assert.True(MotW.Parse("[ZoneTransfer]\nZoneId=4")!.Blocks);
        var intranet = MotW.Parse("[ZoneTransfer]\nZoneId=1\nReferrerUrl=file://server/share");
        Assert.False(intranet!.Blocks);
        Assert.True(intranet.Note);
        Assert.Equal("file://server/share", intranet.Source);
        Assert.True(MotW.Parse("[ZoneTransfer]\nZoneId=2")!.Note);
        Assert.Null(MotW.Parse("[ZoneTransfer]\nZoneId=0"));
        Assert.Null(MotW.Parse(""));
        Assert.Null(MotW.Parse("garbage"));
    }

    [Fact]
    public void Reads_unblocks_and_keeps_the_mark_with_a_fake_stream()
    {
        var streams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var (r, rm, w) = (MotW.Reader, MotW.Remover, MotW.Writer);
        MotW.Reader = p => streams.TryGetValue(p, out var t) ? t : null;
        MotW.Remover = p => streams.Remove(p);
        MotW.Writer = (p, t) => streams[p] = t;
        try
        {
            string dir = TestUtil.TempDir();
            string a = Path.Combine(dir, "a.ps1"), lib = Path.Combine(dir, "lib.ps1"), mod = Path.Combine(dir, "m.psm1");
            File.WriteAllText(a, ". \"$PSScriptRoot\\lib.ps1\"\nImport-Module .\\m.psm1\n& \"$x\\other.ps1\"");
            File.WriteAllText(lib, "");
            File.WriteAllText(mod, "");
            Assert.Null(MotW.Read(a));
            streams[a] = "[ZoneTransfer]\nZoneId=3\nHostUrl=https://example.org/a.ps1";
            streams[lib] = "[ZoneTransfer]\nZoneId=3";
            MotW.Forget(a);
            Assert.True(MotW.Read(a)!.Blocks);
            var refs = MotW.MarkedReferences(a, File.ReadAllText(a));
            Assert.Single(refs);
            Assert.Equal(lib, refs[0].Path);
            // A save that drops the stream gets it back; only Unblock removes it
            MotW.KeepWhile(a, () => { streams.Remove(a); File.WriteAllText(a, "x"); });
            Assert.True(MotW.Read(a)!.Blocks);
            Assert.True(MotW.Unblock(a));
            Assert.Null(MotW.Read(a));
            Assert.False(streams.ContainsKey(a));
        }
        finally { (MotW.Reader, MotW.Remover, MotW.Writer) = (r, rm, w); }
    }

    // ---------- Settings ----------

    [Fact]
    public void Settings_keep_sources_and_disclaimer()
    {
        string dir = TestUtil.TempDir();
        string path = Path.Combine(dir, "settings.json");
        var s = new Settings();
        TrustedSources.Add(s, "example.org");
        TrustedSources.SetBuiltIn(s, "bun.sh", false);
        s.DisclaimerAccepted = "1.0.11";
        s.Save(path);
        var l = Settings.Load(path);
        Assert.Equal(new[] { "example.org" }, l.TrustedSources);
        Assert.Equal(new[] { "bun.sh" }, l.DisabledBuiltinSources);
        Assert.Equal("1.0.11", l.DisclaimerAccepted);
        Assert.Equal("", new Settings().DisclaimerAccepted ?? "");
    }

    // ---------- The WinUtil example ----------

    [Fact]
    public void WinUtil_example_runs_as_admin_from_a_trusted_source()
    {
        string text = "# WinUtil\r\n# Shelly-Icon: E90F\r\n# Shelly-Mode: admin\r\n\r\nirm https://christitus.com/win | iex\r\n";
        Assert.Equal("admin", ScriptHeader.ModeIn(text));
        Assert.True(ScriptHeader.IsDirective("# Shelly-Mode: admin"));
        Assert.Null(ScriptHeader.ModeIn("# x\n# Shelly-Mode: root\n"));
        var f = FallbackScanner.Scan(text);
        var dl = Assert.Single(f, x => x.Rule == "DlExec");
        Assert.Equal("high", dl.Sev);
        Assert.Equal("https://christitus.com/win", dl.Detail);
        var applied = Safety.ApplySources(f, new Settings());
        Assert.Equal("", Safety.Level(Safety.Blocking(applied)));
        Assert.Contains(applied, x => x.Rule == "DlTrusted" && x.Detail.Contains("https://christitus.com/win"));
        // Without the scheme irm uses http: never trusted
        Assert.Equal("high", Safety.Level(Safety.ApplySources(FallbackScanner.Scan("irm christitus.com/win | iex"), new Settings())));
    }
}
