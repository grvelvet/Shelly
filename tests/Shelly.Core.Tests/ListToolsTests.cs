using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.0.10: sort, filters, collapse-all keys, header overflow and the description rules.</summary>
public class ListToolsTests
{
    static ScriptFile F(string rel, int day)
    {
        string folder = rel.Contains('\\') ? rel.Substring(0, rel.LastIndexOf('\\')) : "";
        string name = Path.GetFileNameWithoutExtension(rel.Substring(rel.LastIndexOf('\\') + 1));
        return new ScriptFile { Rel = rel, Name = name, Folder = folder, Modified = new DateTime(2026, 1, day) };
    }

    static readonly ScriptFile A = F("alpha.ps1", 3), B = F("beta.ps1", 1), C = F("gamma.ps1", 2), D = F("delta.ps1", 2);
    static readonly Dictionary<string, ScriptFacts> Facts = new()
    {
        ["alpha.ps1"] = new(new DateTime(2026, 2, 1), 5, false, true, false, true),
        ["beta.ps1"] = new(null, 0, true, false, false, false),
        ["gamma.ps1"] = new(new DateTime(2026, 3, 1), 1, false, false, true, false),
        ["delta.ps1"] = new(null, 0, false, false, false, false),
    };
    static ScriptFacts Of(ScriptFile f) => Facts[f.Rel];
    static string Names(IEnumerable<ScriptFile> l) => string.Join(",", l.Select(f => f.Name));

    [Fact]
    public void Sort_orders_by_each_key_and_direction()
    {
        var l = new[] { C, A, D, B };
        Assert.Equal("alpha,beta,delta,gamma", Names(ScriptOrder.Sort(l, "name", false, Of)));
        Assert.Equal("gamma,delta,beta,alpha", Names(ScriptOrder.Sort(l, "name", true, Of)));
        Assert.Equal("beta,delta,gamma,alpha", Names(ScriptOrder.Sort(l, "modified", false, Of)));   // tie → by name
        Assert.Equal("alpha,delta,gamma,beta", Names(ScriptOrder.Sort(l, "modified", true, Of)));
        Assert.Equal("alpha,gamma,beta,delta", Names(ScriptOrder.Sort(l, "lastrun", false, Of)));    // never run stay last
        Assert.Equal("gamma,alpha,beta,delta", Names(ScriptOrder.Sort(l, "lastrun", true, Of)));
        Assert.Equal("alpha,gamma,beta,delta", Names(ScriptOrder.Sort(l, "runcount", true, Of)));
        Assert.Equal("beta,delta,gamma,alpha", Names(ScriptOrder.Sort(l, "runcount", false, Of)));
    }

    [Fact]
    public void Build_sorts_within_groups_and_keeps_favorites_on_top()
    {
        var scripts = new List<ScriptFile> { A, B, F("Tools\\zeta.ps1", 1), F("Tools\\eta.ps1", 5) };
        var folders = new List<string> { "Tools" };
        var e = ScriptGrouping.Build(scripts, folders, new[] { A }, new HashSet<string>(), "fav", "root",
            l => l.OrderByDescending(f => f.Modified));
        Assert.Equal(new[] { "fav", "alpha", "root", "alpha", "beta", "Tools", "eta", "zeta" }, e.Select(x => x.Title));
    }

    [Fact]
    public void Filters_combine_ran_or_never_and_narrow_the_rest()
    {
        bool M(ScriptFile f, params string[] on) => ScriptFilters.Matches(Of(f), on);
        Assert.True(M(B));
        Assert.True(M(A, "ran")); Assert.False(M(B, "ran"));
        Assert.True(M(A, "ran", "never")); Assert.True(M(B, "ran", "never"));
        Assert.True(M(B, "never", "ps51")); Assert.False(M(D, "never", "ps51"));
        Assert.True(M(A, "danger", "fav")); Assert.False(M(A, "danger", "admin"));
        Assert.True(M(C, "admin"));
        foreach (var k in ScriptFilters.All)
        {
            Loc.Language = "en"; string en = Loc.T("filter_" + k);
            Loc.Language = "ru"; string ru = Loc.T("filter_" + k);
            Assert.NotEqual("filter_" + k, en); Assert.NotEqual(en, ru);
        }
        Loc.Language = "en";
    }

    [Fact]
    public void Folders_with_matches_and_group_keys()
    {
        var all = new List<string> { "A", "A\\B", "A\\B\\C", "D" };
        var s = new List<ScriptFile> { F("A\\B\\C\\x.ps1", 1), F("y.ps1", 1) };
        Assert.Equal(new[] { "A", "A\\B", "A\\B\\C" }, ScriptGrouping.FoldersWith(s, all));
        Assert.Equal(new[] { "*fav", "*root", "A", "D" }, ScriptGrouping.GroupKeys(s, new List<string> { "A", "D" }, true));
        Assert.Equal(new string[0], ScriptGrouping.GroupKeys(new List<ScriptFile> { F("y.ps1", 1) }, new List<string>(), false));
    }

    [Fact]
    public void Header_moves_tools_into_overflow_before_the_search_collapses()
    {
        var wide = Layout.Header(1000);
        Assert.False(wide.Overflow); Assert.Equal(Layout.SearchMax, wide.SearchWidth); Assert.False(wide.SearchCollapsed);
        var mid = Layout.Header(Layout.ToolbarFull + Layout.SearchOverflowAt);
        Assert.False(mid.Overflow); Assert.Equal(Layout.SearchOverflowAt, mid.SearchWidth);
        var tight = Layout.Header(Layout.ToolbarFull + Layout.SearchOverflowAt - 1);
        Assert.True(tight.Overflow); Assert.False(tight.SearchCollapsed);
        var narrow = Layout.Header(Layout.ToolbarCompact + Layout.SearchMin - 1);
        Assert.True(narrow.Overflow); Assert.True(narrow.SearchCollapsed);
        Assert.Equal(0, Layout.ToolbarFull % 4); Assert.Equal(0, Layout.ToolbarCompact % 4);
    }

    [Theory]
    [InlineData("# Description: Cleans temp files\nWrite-Host 1", "Cleans temp files")]
    [InlineData("# описание:  Чистит temp\nWrite-Host 1", "Чистит temp")]
    [InlineData("# What this script does\n", "What this script does")]
    [InlineData("<#\n.SYNOPSIS\n  Backs up the profile\n  to a zip.\n\n.DESCRIPTION\n  Long text\n#>\nparam()", "Backs up the profile to a zip.")]
    [InlineData("#Requires -Version 5.1\n<# .SYNOPSIS\nShort one\n.EXAMPLE\nx #>\n", "Short one")]
    [InlineData("# First line wins\n<#\n.SYNOPSIS\nNot this\n#>", "First line wins")]
    [InlineData("<# just a block #>\n# After the block\n", "After the block")]
    [InlineData("<#\n.DESCRIPTION\nNo synopsis\n#>\nWrite-Host", null)]
    [InlineData("Write-Host 1\n# too late", null)]
    public void Description_rules(string text, string? expected) =>
        Assert.Equal(expected, FallbackParser.Description(text));
}
