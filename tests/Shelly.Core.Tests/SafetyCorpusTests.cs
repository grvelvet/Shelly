using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Shelly.Core.Tests;

/// <summary>The bypass corpus (TestData/Safety): every file says what must be found ("# expect: Rule/sev, ...",
/// or "none" = nothing at warning or danger level), optionally what the built-in fallback finds instead
/// ("# fallback: ...") and the URL a download-and-run finding must show ("# url: ...").
/// The same files run through Analyzer.ps1 (pwsh) and through FallbackScanner.</summary>
public class SafetyCorpusTests
{
    readonly ITestOutputHelper log;
    public SafetyCorpusTests(ITestOutputHelper log) { this.log = log; }

    static string Dir => Path.Combine(AppContext.BaseDirectory, "TestData", "Safety");

    public sealed record Case(string Name, string Path, string Text, string[] Expect, string[] Fallback, string? Url);

    public static List<Case> Corpus() => Directory.GetFiles(Dir, "*.ps1").OrderBy(p => p, StringComparer.Ordinal).Select(p =>
    {
        string t = TextFile.ReadAllText(p);
        string[] Read(string key)
        {
            var m = Regex.Match(t, @"#\s*" + key + @":\s*(.+)");
            return !m.Success || m.Groups[1].Value.Trim() == "none" ? Array.Empty<string>() : m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
        var e = Read("expect");
        var f = Regex.IsMatch(t, @"#\s*fallback:") ? Read("fallback") : e;
        var u = Regex.Match(t, @"#\s*url:\s*(\S+)");
        return new Case(System.IO.Path.GetFileNameWithoutExtension(p), p, t, e, f, u.Success ? u.Groups[1].Value : null);
    }).ToList();

    static string? Check(Case c, string[] expect, List<DangerFinding> got)
    {
        var have = got.Select(d => d.Rule + "/" + d.Sev).ToList();
        if (expect.Length == 0)
        {
            var bad = got.Where(d => d.Sev != "low").ToList();
            return bad.Count == 0 ? null : c.Name + ": expected none, got " + string.Join(", ", have);
        }
        var miss = expect.Where(x => !have.Contains(x)).ToList();
        if (miss.Count > 0) return c.Name + ": missing " + string.Join(", ", miss) + "; got " + (have.Count == 0 ? "-" : string.Join(", ", have));
        if (c.Url != null && !got.Any(d => d.Rule == "DlExec" && d.Detail == c.Url))
            return c.Name + ": URL " + c.Url + " not shown; got " + string.Join(", ", got.Where(d => d.Rule == "DlExec").Select(d => d.Detail));
        return null;
    }

    [Fact]
    public void Corpus_is_there() => Assert.True(Corpus().Count >= 100);

    [PwshFact]
    public void Analyzer_finds_every_bypass_and_no_false_alarms()
    {
        var corpus = Corpus();
        string pwsh = TestUtil.RequirePwsh();
        string dir = TestUtil.TempDir();
        var res = AnalyzerRunner.RunAsync(pwsh, corpus.Select(c => c.Path).ToList(), dir, TimeSpan.FromSeconds(300)).GetAwaiter().GetResult();
        Assert.Equal(corpus.Count, res.Count);
        var fails = new List<string>();
        foreach (var c in corpus)
        {
            var r = res.Single(x => x.Path == c.Path);
            var err = Check(c, c.Expect, r.Danger);
            if (err != null) fails.Add(err);
        }
        log.WriteLine("analyzer: " + (corpus.Count - fails.Count) + "/" + corpus.Count + " pass");
        Assert.True(fails.Count == 0, string.Join("\n", fails));
    }

    [Fact]
    public void Fallback_follows_the_same_rules()
    {
        var corpus = Corpus();
        var fails = new List<string>();
        foreach (var c in corpus)
        {
            var err = Check(c, c.Fallback, FallbackScanner.Scan(c.Text));
            if (err != null) fails.Add(err);
        }
        log.WriteLine("fallback: " + (corpus.Count - fails.Count) + "/" + corpus.Count + " pass");
        Assert.True(fails.Count == 0, string.Join("\n", fails));
    }
}
