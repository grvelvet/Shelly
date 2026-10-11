using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

public class MiscTests
{
    static List<ParamInfo> Ps() => new()
    {
        new() { Name = "Name", Kind = "text", Mandatory = true },
        new() { Name = "Force", Kind = "switch" },
        new() { Name = "Quiet", Kind = "switch", DefaultBool = true },
        new() { Name = "Enabled", Kind = "bool" },
        new() { Name = "Items", Kind = "array" },
    };

    [Fact]
    public void Formats_arguments()
    {
        var vals = new Dictionary<string, object> { ["Name"] = "O'Brien", ["Force"] = true, ["Quiet"] = false, ["Enabled"] = false, ["Items"] = "a, b ,,c" };
        Assert.Equal(" -Name 'O''Brien' -Force -Quiet:$false -Enabled:$false -Items 'a','b','c'", RunArgs.Format(Ps(), vals));
        Assert.Equal("", RunArgs.Format(Ps(), new Dictionary<string, object> { ["Force"] = false, ["Name"] = "" }));
        Assert.Equal(new[] { "Name" }, RunArgs.MissingMandatory(Ps(), new Dictionary<string, object> { ["Name"] = "  " }));
        Assert.Empty(RunArgs.MissingMandatory(Ps(), new Dictionary<string, object> { ["Name"] = "x" }));
    }

    [Fact]
    public void Quoting_rules()
    {
        Assert.Equal("\"a b\"", Quoting.NativeArg("a b"));
        Assert.Equal("\"C:\\dir\\\\\"", Quoting.NativeArg("C:\\dir\\"));
        Assert.Equal("\"say \\\"hi\\\"\"", Quoting.NativeArg("say \"hi\""));
        Assert.Equal("'it''s'", Quoting.PsString("it's"));
        Assert.Equal("'a\u2019\u2019b'", Quoting.PsString("a\u2019b"));
        Assert.Equal("\"a\\\\b\\\"c\\n\"", Quoting.CsString("a\\b\"c\n"));
        string cmd = RunArgs.InlineCommand("C:\\S\\it's.ps1", " -X 'y'");
        Assert.Contains("& 'C:\\S\\it''s.ps1' -X 'y';", cmd);
        Assert.StartsWith("-NoProfile -NoExit -ExecutionPolicy Bypass -File ", RunArgs.WindowArgs("C:\\a.ps1", "", false));
        Assert.Contains("Set-Location -LiteralPath", RunArgs.WindowArgs("C:\\a.ps1", "", true));
    }

    [Fact]
    public void Script_names()
    {
        string scripts = Path.Combine(TestUtil.TempDir(), "Scripts");
        Assert.Null(ScriptCatalog.ResolveNewScript(scripts, "  "));
        var r = ScriptCatalog.ResolveNewScript(scripts, "Tools\\Sub/My: script?.ps1")!;
        Assert.Equal("", r.Error);
        Assert.Equal("My script", r.Name);
        Assert.Equal(Path.Combine(scripts, "Tools", "Sub"), r.Dir);
        Assert.Equal("escape", ScriptCatalog.ResolveNewScript(scripts, "..\\x")!.Error);
        Assert.Equal("escape", ScriptCatalog.ResolveNewScript(scripts, "C:\\x")!.Error);
        Assert.Equal("reserved", ScriptCatalog.ResolveNewScript(scripts, "con")!.Error);
        Assert.Equal("reserved", ScriptCatalog.ResolveNewScript(scripts, "LPT1.txt")!.Error);
        Assert.Equal("bad", ScriptCatalog.ResolveNewScript(scripts, "???")!.Error);
        Assert.Equal("nofolder", ScriptCatalog.CheckRename("a\\b")!.Error);
        Assert.Equal("x", ScriptCatalog.CheckRename(".x.ps1")!.Name);
        Assert.Equal("fnHidden", ScriptCatalog.CheckFolderName(".git", out _));
        Assert.Equal("fnTrail", ScriptCatalog.CheckFolderName("a.", out _));
        Assert.Equal("fnReserved", ScriptCatalog.CheckFolderName("aux", out _));
        Assert.Equal("", ScriptCatalog.CheckFolderName(" Tools ", out var clean));
        Assert.Equal("Tools", clean);
    }

    [Fact]
    public void Scan_groups_and_layout()
    {
        string root = TestUtil.TempDir();
        var paths = new AppPaths(root);
        paths.EnsureFolders();
        File.WriteAllText(Path.Combine(paths.Scripts, "b.ps1"), "");
        File.WriteAllText(Path.Combine(paths.Scripts, ".hidden.ps1"), "");
        File.WriteAllText(Path.Combine(paths.Scripts, "notes.txt"), "");
        Directory.CreateDirectory(Path.Combine(paths.Scripts, "Z", "Deep"));
        Directory.CreateDirectory(Path.Combine(paths.Scripts, "Empty"));
        File.WriteAllText(Path.Combine(paths.Scripts, "Z", "Deep", "a.ps1"), "");
        var files = ScriptCatalog.Scan(paths);
        Assert.Equal(new[] { "b.ps1", "Z\\Deep\\a.ps1" }, files.Select(f => f.Rel));
        Assert.Equal("Z\\Deep", files[1].Folder);
        Assert.Equal(new[] { "Empty", "Z", "Z\\Deep" }, ScriptCatalog.Folders(paths));
        Assert.EndsWith(Path.Combine("build", "Z", "Deep", "a.exe"), ExeBuilder.OutPath(paths, files[1]));
        // Shelly.exe inside build\ (0.9.x layout) uses the parent folder
        Assert.Equal(paths.Root, AppPaths.FromExeFolder(paths.Build).Root);
    }

    [Fact]
    public void Exe_stub_source()
    {
        string src = ExeBuilder.MakeSource(System.Text.Encoding.UTF8.GetBytes("Write-Host 'hi'"), "7", "1.0.0.0");
        Assert.Contains("const string Engine = \"7\";", src);
        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Write-Host 'hi'")), src);
        Assert.DoesNotContain("%SCRIPT%", src);
        Assert.DoesNotContain("%MSG_", src);
        Assert.Contains("const string Engine = \"auto\";", ExeBuilder.MakeSource(new byte[0], "bogus", "1.0.0.0"));
    }

    [Fact]
    public void Update_check()
    {
        Assert.True(UpdateChecker.IsNewer("v1.0.1", "1.0.0"));
        Assert.True(UpdateChecker.IsNewer("1.1", "1.0.9"));
        Assert.False(UpdateChecker.IsNewer("1.0.0", "1.0.0"));
        Assert.False(UpdateChecker.IsNewer("0.9.8", "1.0.0"));
        Assert.False(UpdateChecker.IsNewer("garbage", "1.0.0"));
        Assert.False(UpdateChecker.IsRepoSet("OWNER/shelly"));
        Assert.True(UpdateChecker.IsRepoSet("someone/shelly"));
        var r = UpdateChecker.Parse("{\"tag_name\":\"v1.2.0\",\"html_url\":\"https://github.com/x/shelly/releases/tag/v1.2.0\",\"name\":\"Shelly 1.2.0\",\"draft\":false,\"prerelease\":false," +
            "\"assets\":[{\"name\":\"shelly-1.2.0.zip\",\"browser_download_url\":\"https://dl/shelly-1.2.0.zip\"},{\"name\":\"shelly-1.2.0-selfcontained.zip\",\"browser_download_url\":\"https://dl/sc.zip\"}]}")!;
        Assert.Equal("1.2.0", r.Version);
        Assert.Equal("https://dl/shelly-1.2.0.zip", r.AssetUrl);
        Assert.Null(UpdateChecker.Parse("{\"tag_name\":\"v2.0.0\",\"prerelease\":true}"));
        Assert.True(UpdateChecker.Due("", DateTime.UtcNow));
        Assert.False(UpdateChecker.Due(DateTime.UtcNow.AddHours(-1).ToString("o"), DateTime.UtcNow));
        Assert.True(UpdateChecker.Due(DateTime.UtcNow.AddDays(-2).ToString("o"), DateTime.UtcNow));
    }

    [Fact]
    public void Localization()
    {
        Loc.Language = "ru";
        try
        {
            Assert.Equal("Вкл", Loc.T("toggleOn"));
            Assert.Equal("Скрипты", Loc.T("title"));
            Assert.Equal("nonexistentKey", Loc.T("nonexistentKey"));
        }
        finally { Loc.Language = "en"; }
        Assert.Equal("Scripts", Loc.T("title"));
    }
}
