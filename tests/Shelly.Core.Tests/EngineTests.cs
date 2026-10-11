using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

public class EngineTests
{
    static readonly List<ParseError> None = new();
    static readonly List<ParseError> Some = new() { new ParseError { Line = 1, Message = "x" } };
    static RequiresInfo Req(string ver = "", params string[] eds) => new() { Version = ver, Editions = eds.ToList() };

    [Theory]
    [InlineData("7", true, "7", false, "manual")]
    [InlineData("7", false, "7", true, "manual")]
    [InlineData("5.1", true, "5.1", false, "manual")]
    public void Manual_choice(string choice, bool pwsh, string engine, bool missing, string reason)
    {
        var r = EngineResolver.Resolve(choice, "auto", pwsh, null, new[] { "??" }, Array.Empty<string>(), None, None);
        Assert.Equal(engine, r.Engine);
        Assert.False(r.Auto);
        Assert.Equal(missing, r.Missing);
        Assert.Equal(reason, r.Reason);
    }

    [Fact]
    public void Auto_rules_in_order()
    {
        string[] none = Array.Empty<string>();
        // #Requires -Version 7 needs 7, even when missing
        var r = EngineResolver.Resolve("auto", "auto", false, Req("7.2"), none, none, None, None);
        Assert.Equal(("7", "erReq", true, true), (r.Engine, r.Reason, r.Required, r.Missing));
        r = EngineResolver.Resolve("auto", "auto", true, Req("", "Core"), none, none, None, None);
        Assert.Equal(("7", "erReq", false), (r.Engine, r.Reason, r.Missing));
        r = EngineResolver.Resolve("auto", "7", true, Req("", "Desktop"), none, none, None, None);
        Assert.Equal(("5.1", "erReq"), (r.Engine, r.Reason));
        r = EngineResolver.Resolve("auto", "auto", true, Req("5.1"), none, none, None, None);
        Assert.Equal(("5.1", "erReq"), (r.Engine, r.Reason));
        // PowerShell 7 syntax
        r = EngineResolver.Resolve("auto", "5.1", true, null, new[] { "??", "&&" }, none, None, None);
        Assert.Equal(("7", "er7", "??, &&"), (r.Engine, r.Reason, r.Data));
        // parses only in 7
        r = EngineResolver.Resolve("auto", "5.1", true, null, none, none, Some, None);
        Assert.Equal(("7", "erParse51"), (r.Engine, r.Reason));
        // 5.1-only commands
        r = EngineResolver.Resolve("auto", "7", true, null, none, new[] { "Get-WmiObject" }, None, None);
        Assert.Equal(("5.1", "erWinPs"), (r.Engine, r.Reason));
        // parses only in 5.1
        r = EngineResolver.Resolve("auto", "auto", true, null, none, none, None, Some);
        Assert.Equal(("5.1", "erParse7"), (r.Engine, r.Reason));
        // defaults
        Assert.Equal(("5.1", "erDefault"), Pick(EngineResolver.Resolve("auto", "5.1", true, null, none, none, None, None)));
        Assert.Equal(("7", "erDefault"), Pick(EngineResolver.Resolve("auto", "7", true, null, none, none, None, None)));
        Assert.Equal(("7", "erPrefer7"), Pick(EngineResolver.Resolve("auto", "auto", true, null, none, none, None, None)));
        Assert.Equal(("5.1", "erNo7"), Pick(EngineResolver.Resolve("auto", "auto", false, null, none, none, None, None)));
        // unknown parse results (null) never decide
        Assert.Equal(("7", "erPrefer7"), Pick(EngineResolver.Resolve("auto", "auto", true, null, none, none, Some, null)));
    }

    static (string, string) Pick(EngineInfo r) => (r.Engine, r.Reason);

    [Fact]
    public void Issues_for_engine()
    {
        var ei = EngineResolver.Resolve("5.1", "auto", true, Req("7.0"), new[] { "??" }, Array.Empty<string>(), None, None);
        var issues = ScriptIssue.ForEngine(ei, Req("7.0"), true);
        Assert.Contains(issues, i => i.Kind == "core" && i.Level == "error");
        ei = EngineResolver.Resolve("5.1", "auto", true, null, new[] { "??" }, Array.Empty<string>(), None, None);
        Assert.Contains(ScriptIssue.ForEngine(ei, null, true), i => i.Kind == "syntax7");
        ei = EngineResolver.Resolve("auto", "auto", false, Req("7.2"), Array.Empty<string>(), Array.Empty<string>(), None, None);
        var need = ScriptIssue.ForEngine(ei, Req("7.2"), false);
        Assert.Contains(need, i => i.Kind == "need7");
        Assert.Contains("#Requires -Version 7.2", need.Single(i => i.Kind == "need7").Text());
        ei = EngineResolver.Resolve("7", "auto", true, null, Array.Empty<string>(), new[] { "Get-WmiObject" }, None, None);
        ei.Errors = Some;
        var w = ScriptIssue.ForEngine(ei, null, true);
        Assert.Contains(w, i => i.Kind == "winps");
        Assert.Contains(w, i => i.Kind == "parse");
    }

    [Fact]
    public void Run_engine_falls_back_to_the_installed_one()
    {
        var eng = new AnalysisService.Engines { Ps51 = "C:\\ps.exe", Pwsh = null };
        var ei = new EngineInfo { Engine = "7" };
        Assert.Equal(("5.1", "C:\\ps.exe", true), AnalysisService.RunEngine(ei, eng));
        eng = new AnalysisService.Engines { Ps51 = null, Pwsh = "pwsh" };
        Assert.Equal(("7", "pwsh", false), AnalysisService.RunEngine(ei, eng));
        Assert.Equal(("5.1", "powershell.exe", false), AnalysisService.RunEngine(ei, new AnalysisService.Engines()));
    }

    [Fact]
    public void Meta_from_real_analyzer()
    {
        string? pwsh = TestUtil.Pwsh();
        if (pwsh == null) return;
        string root = TestUtil.TempDir();
        var paths = new AppPaths(root, Path.Combine(root, "cache"));
        paths.EnsureFolders();
        File.WriteAllText(Path.Combine(paths.Scripts, "a.ps1"), "param([string]$X)\n$y = $x ?? 'd'\nRemove-Item C:\\ -Recurse");
        Directory.CreateDirectory(Path.Combine(paths.Scripts, "Sub"));
        File.WriteAllText(Path.Combine(paths.Scripts, "Sub", "b.ps1"), "# B\nWrite-Host hi");
        var files = ScriptCatalog.Scan(paths);
        Assert.Equal(new[] { "a.ps1", "Sub\\b.ps1" }, files.Select(f => f.Rel));
        var svc = new AnalysisService(new AnalysisService.Engines { Pwsh = pwsh }, Path.Combine(paths.CacheDir, "a.json"), paths.CacheDir);
        var s = new Settings();
        // before the analysis: the built-in parser
        var m0 = svc.Meta(files[0], s, true, false);
        Assert.True(m0.FallbackOnly);
        Assert.Equal("X", m0.Params.Single().Name);
        Assert.True(svc.WaitAsync(files[0], TimeSpan.FromSeconds(120)).Result);
        var m = svc.Meta(files[0], s, true, false);
        Assert.False(m.FallbackOnly);
        Assert.Equal("7", m.Engine.Engine);
        Assert.Equal("er7", m.Engine.Reason);
        Assert.Equal("high", m.DangerLevel);
        svc.SaveCache();
        // a new service reads the disk cache: no PowerShell needed
        var svc2 = new AnalysisService(new AnalysisService.Engines { Pwsh = pwsh }, Path.Combine(paths.CacheDir, "a.json"), paths.CacheDir);
        Assert.False(svc2.IsPending(files[0]));
        Assert.Equal("high", svc2.Meta(files[0], s, true, false).DangerLevel);
    }
}
