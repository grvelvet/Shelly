using System.Diagnostics;
using System.Text;
using Shelly.Core;
using Xunit;
using Xunit.Abstractions;

namespace Shelly.Core.Tests;

/// <summary>1.0.6: adaptive layout rules, script header directives, the grouped list and a synthetic benchmark.</summary>
public class AdaptiveTests
{
    readonly ITestOutputHelper output;
    public AdaptiveTests(ITestOutputHelper o) { output = o; }

    // ---------- Narrow window (500 / 640 / 800 / 1200 px) ----------

    // The page content width at a window width: nav pane inline + the page's 36 px margins on both sides
    static double ListWidth(double window, bool userCompact = false)
    {
        double pane = Layout.NavFor(window, userCompact) == Layout.Nav.Expanded ? Layout.PaneOpen : Layout.PaneCompact;
        return window - pane - 2 * 36 - 2;   // 1 px content frame border, scroll bar overlay ignored
    }

    // Header room for the search box: content width - title (~"Scripts" 28 px semibold ≈ 100) - its 24 px margin
    // - refresh + folders (2 × 40 + 4) - new script + new folder (2 × 40 + 8)
    static double SearchRoom(double window) => ListWidth(window) - 100 - 24 - 84 - 88;

    [Theory]
    [InlineData(500, Layout.Nav.Compact, true, false, false, false)]
    [InlineData(640, Layout.Nav.Compact, false, false, true, true)]
    [InlineData(800, Layout.Nav.Expanded, false, false, true, true)]
    [InlineData(953, Layout.Nav.Expanded, false, true, true, true)]   // the width of the user's Wintoys comparison
    [InlineData(1200, Layout.Nav.Expanded, false, true, true, true)]
    public void Breakpoints(double window, Layout.Nav nav, bool searchButton, bool engine, bool last, bool danger)
    {
        Assert.Equal(nav, Layout.NavFor(window, false));
        Assert.Equal(Layout.Nav.Compact, Layout.NavFor(window, true));
        Assert.Equal(window < 800, Layout.NavOverlays(window));
        var (w, collapsed) = Layout.Search(SearchRoom(window));
        Assert.Equal(searchButton, collapsed);
        if (!collapsed) Assert.InRange(w, Layout.SearchMin, Layout.SearchMax);
        var slots = Layout.RowSlots(ListWidth(window));
        Assert.Equal((engine, last, danger), slots);
        output.WriteLine($"{window,5}px: nav {nav}, list {ListWidth(window)}, search {(collapsed ? "button" : w + " px")}, slots {slots}");
    }

    [Fact]
    public void Search_box_shrinks_then_collapses()
    {
        Assert.Equal((340d, false), Layout.Search(900));
        Assert.Equal((200d, false), Layout.Search(200.6));
        Assert.Equal((160d, false), Layout.Search(160));
        Assert.True(Layout.Search(159.9).Collapsed);
        Assert.True(Layout.MinWindowWidth <= 500 && Layout.MinWindowHeight <= 420);
    }

    [Fact]
    public void Row_slots_drop_in_priority_order()
    {
        // engine goes first, then the last-run mark, the warning shield last
        bool prevE = true, prevL = true, prevD = true;
        for (double w = 1000; w >= 200; w -= 10)
        {
            var (e, l, d) = Layout.RowSlots(w);
            Assert.True(!e || l, "engine shown while last-run hidden");
            Assert.True(!l || d, "last-run shown while warnings hidden");
            Assert.True(prevE || !e); Assert.True(prevL || !l); Assert.True(prevD || !d);
            (prevE, prevL, prevD) = (e, l, d);
        }
    }

    // ---------- # Shelly-Icon ----------

    [Fact]
    public void Icon_directive()
    {
        Assert.Equal("\uE9D2", ScriptHeader.IconGlyph("#Requires -Version 5.1\r\n# Shelly-Icon: E9D2\r\nparam()"));
        Assert.Equal("\uE77B", ScriptHeader.IconGlyph("<#\n.SYNOPSIS\nHi\n#>\n# shelly-icon: u+e77b\n"));
        Assert.Null(ScriptHeader.IconGlyph("# Shelly-Icon: 0041\n"));          // outside the private-use range
        Assert.Null(ScriptHeader.IconGlyph("# Shelly-Icon: zzzz\n"));
        Assert.Null(ScriptHeader.IconGlyph("Write-Host 1\n# Shelly-Icon: E9D2\n"));   // after code: not a header
        Assert.True(ScriptHeader.IsDirective("# Shelly-Icon: E9D2"));
        Assert.False(ScriptHeader.IsDirective("# Shows the shell icon"));
        // the directive is not the description
        Assert.Equal("Converts money", FallbackParser.Description("# Shelly-Icon: E9D2\n# Converts money\nparam()"));
    }

    [Fact]
    public void Bundled_scripts_have_distinct_icons()
    {
        string? scripts = FindUp("github/shelly/Scripts") ?? FindUp("Scripts");
        if (scripts == null || !File.Exists(Path.Combine(scripts, "usd_rub.ps1"))) return;   // only in the repo layout
        string G(string rel) => ScriptHeader.IconGlyph(TextFile.ReadAllText(Path.Combine(scripts, rel))) ?? "";
        var g = new[] { G("usd_rub.ps1"), G(Path.Combine("Examples", "Hello with parameters.ps1")), G(Path.Combine("System", "System summary.ps1")) };
        Assert.All(g, x => Assert.NotEqual("", x));
        Assert.Equal(3, g.Distinct().Count());
    }

    static string? FindUp(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, rel);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    // ---------- Empty states ----------

    [Fact]
    public void Empty_state_texts_exist_in_both_languages()
    {
        var keys = new[] { "favEmptyTitle", "favEmptyText", "goScripts", "histEmptyTitle", "histEmptyText", "histNoMatchTitle", "histNoMatchText",
            "clearSearch", "searchEmptyTitle", "searchEmptyText", "scriptsEmptyTitle", "scriptsEmptyText", "openScriptsFolder", "new",
            "filterEmptyTitle", "filterEmptyText", "clearFilters", "rowIconTip", "navOpen", "navClose", "searchOpenTip", "searchCloseTip" };
        try
        {
            foreach (var k in keys)
            {
                Loc.Language = "en"; string en = Loc.T(k);
                Loc.Language = "ru"; string ru = Loc.T(k);
                Assert.NotEqual(k, en);
                Assert.NotEqual(en, ru);
            }
            Loc.Language = "en";
            Assert.Contains("“abc”", Loc.F("searchEmptyText", "abc"));
        }
        finally { Loc.Language = "en"; }
    }

    // ---------- Grouped list ----------

    /// <summary>The pre-1.0.6 algorithm (a scan of all scripts per folder), as the reference.</summary>
    static List<ListEntry> Naive(List<ScriptFile> scripts, List<string> folders, HashSet<string> coll)
    {
        var res = new List<ListEntry>();
        var root = scripts.Where(s => s.Folder == "").ToList();
        if (folders.Count > 0 && root.Count > 0)
        {
            res.Add(new ListEntry(true, "", "root", root.Count, 0, coll.Contains("*root"), null));
            if (!coll.Contains("*root")) foreach (var f in root) res.Add(new ListEntry(false, f.Rel, f.Name, 0, 0, false, f));
        }
        else foreach (var f in root) res.Add(new ListEntry(false, f.Rel, f.Name, 0, 0, false, f));
        foreach (var folder in folders)
        {
            if (coll.Any(c => folder.StartsWith(c + "\\", StringComparison.OrdinalIgnoreCase))) continue;
            int depth = folder.Count(ch => ch == '\\');
            int count = scripts.Count(s => s.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase) || s.Folder.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase));
            bool c = coll.Contains(folder);
            res.Add(new ListEntry(true, folder, folder.Split('\\').Last(), count, depth, c, null));
            if (c) continue;
            foreach (var f in scripts.Where(s => s.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)))
                res.Add(new ListEntry(false, f.Rel, f.Name, 0, depth + 1, false, f));
        }
        return res;
    }

    static (AppPaths Paths, int Folders) Generate(int scripts, int topFolders, int subPerTop)
    {
        var paths = new AppPaths(TestUtil.TempDir());
        paths.EnsureFolders();
        var dirs = new List<string>();
        for (int t = 0; t < topFolders; t++)
        {
            string top = Path.Combine(paths.Scripts, $"Area {t:00}");
            dirs.Add(top);
            for (int s = 0; s < subPerTop; s++) dirs.Add(Path.Combine(top, $"Topic {s}"));
        }
        foreach (var d in dirs) Directory.CreateDirectory(d);
        var rnd = new Random(42);
        for (int i = 0; i < scripts; i++)
        {
            string dir = i % 25 == 0 ? paths.Scripts : dirs[rnd.Next(dirs.Count)];
            var sb = new StringBuilder();
            sb.Append("#Requires -Version 5.1\r\n");
            if (i % 3 == 0) sb.Append("# Shelly-Icon: E9D2\r\n");
            sb.Append($"<#\r\n.SYNOPSIS\r\nTask number {i}: rotates logs, copies files and reports.\r\n.PARAMETER Path\r\nWhere to work.\r\n#>\r\n");
            sb.Append("[CmdletBinding()]\r\nparam(\r\n    [Parameter(Mandatory)][string]$Path,\r\n    [ValidateSet('a','b','c')][string]$Mode = 'a',\r\n    [int]$Days = 7,\r\n    [switch]$WhatIf\r\n)\r\n");
            for (int l = 0; l < 40; l++) sb.Append($"Get-ChildItem -Path $Path -Recurse | Where-Object {{ $_.Length -gt {l} }} | Select-Object -First 5\r\n");
            File.WriteAllText(Path.Combine(dir, $"Script {i:0000}.ps1"), sb.ToString(), new UTF8Encoding(true));
        }
        return (paths, dirs.Count);
    }

    [Fact]
    public void Grouping_matches_the_reference()
    {
        var (paths, _) = Generate(120, 4, 2);
        var scripts = ScriptCatalog.Scan(paths);
        var folders = ScriptCatalog.Folders(paths);
        foreach (var coll in new[] { new HashSet<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Area 01", "Area 02\\Topic 1", "*root" } })
        {
            var a = ScriptGrouping.Build(scripts, folders, Array.Empty<ScriptFile>(), coll, "fav", "root");
            var b = Naive(scripts, folders, coll);
            Assert.Equal(b, a);
        }
        Directory.Delete(paths.Root, true);
    }

    /// <summary>Synthetic library: 1000 scripts in 50 folders (10 areas × 4 topics + the areas), ~50 lines each.
    /// Times are printed (dotnet test --logger "console;verbosity=detailed") and appended to shelly-bench.txt in the
    /// temp folder; the asserts are loose ceilings so a slow CI box doesn't fail.</summary>
    [Fact]
    public void Benchmark_1000_scripts_50_folders()
    {
        var (paths, nFolders) = Generate(1000, 10, 4);
        Assert.Equal(50, nFolders);
        var sw = Stopwatch.StartNew();
        double T() { double ms = sw.Elapsed.TotalMilliseconds; sw.Restart(); return ms; }

        ScriptCatalog.Scan(paths); ScriptCatalog.Folders(paths); T();   // warm the file system cache and the JIT
        var scripts = ScriptCatalog.Scan(paths); double scan = T();
        var folders = ScriptCatalog.Folders(paths); double fold = T();
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var favs = scripts.Take(10).ToList();
        ScriptGrouping.Build(scripts, folders, favs, empty, "fav", "root"); T();
        const int reps = 20;
        for (int i = 0; i < reps; i++) ScriptGrouping.Build(scripts, folders, favs, empty, "fav", "root");
        double build = T() / reps;
        var naiveColl = new HashSet<string>();
        Naive(scripts, folders, naiveColl); T();
        for (int i = 0; i < 5; i++) Naive(scripts, folders, naiveColl);
        double naive = T() / 5;
        var descs = scripts.ToDictionary(s => s.Rel, s => "Task number " + s.Name);
        ScriptGrouping.Search(scripts, "script 07", f => descs[f.Rel]); T();
        for (int i = 0; i < reps; i++) ScriptGrouping.Search(scripts, "script 07", f => descs[f.Rel]);
        double search = T() / reps;
        foreach (var s in scripts) FallbackParser.ParseFile(s.Path);
        double parse = T();
        foreach (var s in scripts) ScriptHeader.GlyphOf(s);
        double glyphCold = T();
        foreach (var s in scripts) ScriptHeader.GlyphOf(s);
        double glyphWarm = T();

        string report = $"{DateTime.Now:yyyy-MM-dd HH:mm} {Environment.OSVersion.Platform} {Environment.ProcessorCount} cpu  1000 scripts / 50 folders: " +
                        $"scan {scan:0.0} ms, folders {fold:0.0} ms, group {build:0.00} ms (old algorithm {naive:0.00} ms), search {search:0.00} ms, " +
                        $"fallback parse all {parse:0} ms, icon header cold {glyphCold:0.0} ms / cached {glyphWarm:0.00} ms";
        output.WriteLine(report);
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "shelly-bench.txt"), report + Environment.NewLine);
        Assert.Equal(1000, scripts.Count);
        Assert.InRange(build, 0, 100);
        Assert.InRange(search, 0, 100);
        Assert.InRange(scan, 0, 5000);
        Directory.Delete(paths.Root, true);
    }

    // ---------- 1.0.8: nav pane animation plan ----------

    const double O = Layout.PaneOpen, C = Layout.PaneCompact;

    [Fact]
    public void Inline_open_slides_the_layer_and_commits_the_margin_once_at_the_end()
    {
        var p = Layout.Plan(C, O, overlay: false, currentMargin: C);
        Assert.Equal((C, O), (p.From, p.To));
        Assert.Equal(C, p.MarginStart);                  // no layout at the start
        Assert.Equal(O, p.MarginEnd);                    // one layout at the end
        Assert.Equal((0d, O - C), (p.ShiftFrom, p.ShiftTo));
        // the layer's edge (margin + shift) is the pane's edge at both ends: no gap, no overlap
        Assert.Equal(p.From, p.MarginStart + p.ShiftFrom);
        Assert.Equal(p.To, p.MarginStart + p.ShiftTo);
        Assert.False(p.SurfaceDuring); Assert.False(p.SurfaceAfter);   // inline: plain Mica, no frame of its own
        Assert.Equal(1, p.Labels);
    }

    [Fact]
    public void Inline_close_commits_the_margin_at_the_start_and_slides_back()
    {
        var p = Layout.Plan(O, C, overlay: false, currentMargin: O);
        Assert.Equal(C, p.MarginStart);
        Assert.Equal(C, p.MarginEnd);
        Assert.Equal(p.From, p.MarginStart + p.ShiftFrom);
        Assert.Equal(p.To, p.MarginStart + p.ShiftTo);
        Assert.False(p.SurfaceDuring); Assert.False(p.SurfaceAfter);
        Assert.Equal(0, p.Labels);
    }

    [Fact]
    public void Overlay_floats_over_the_page_on_a_surface()
    {
        var open = Layout.Plan(C, O, overlay: true, currentMargin: C);
        Assert.Equal((C, C), (open.MarginStart, open.MarginEnd));
        Assert.Equal((0d, 0d), (open.ShiftFrom, open.ShiftTo));
        Assert.True(open.SurfaceDuring); Assert.True(open.SurfaceAfter);
        var close = Layout.Plan(O, C, overlay: false, currentMargin: C);
        Assert.Equal((C, C), (close.MarginStart, close.MarginEnd));
        Assert.True(close.SurfaceDuring); Assert.False(close.SurfaceAfter);
        // reversing halfway starts from where the pane is
        var mid = Layout.Plan(150, C, overlay: false, currentMargin: C);
        Assert.Equal(150, mid.From);
    }

    /// <summary>The pane width lives in one place (Layout.PaneOpen / PaneCompact): no width, margin or animation
    /// target in MainWindow is a number of its own (1.0.7 left a 240 in the XAML next to the constant).</summary>
    [Fact]
    public void Pane_animation_targets_derive_from_the_single_width_constant()
    {
        string dir = Path.Combine(FindUp("src") ?? "", "Shelly", "Views");
        if (!Directory.Exists(dir)) return;
        string xaml = File.ReadAllText(Path.Combine(dir, "MainWindow.xaml"));
        string cs = File.ReadAllText(Path.Combine(dir, "MainWindow.xaml.cs"));
        var pane = System.Text.RegularExpressions.Regex.Match(xaml, @"<Grid x:Name=""Pane""[^>]*>").Value;
        var body = System.Text.RegularExpressions.Regex.Match(xaml, @"<Border x:Name=""Body""[^>]*>").Value;
        Assert.DoesNotContain("Width=", pane);
        Assert.DoesNotContain("Margin=", body);
        foreach (var n in new[] { "240", "280", "48" })
        {
            Assert.DoesNotMatch(@"\bWidth\s*=\s*" + n + @"\b", cs);
            Assert.DoesNotMatch(@"FromTo\([^;]*\b" + n + @"\b", cs);
            Assert.DoesNotMatch(@"new Thickness\(" + n + @"\b", cs);
        }
        Assert.Contains("Layout.Plan(", cs);
        Assert.Contains("Motion.FromTo(plan.From, plan.To", cs);
        Assert.Contains("Motion.FromTo(plan.ShiftFrom, plan.ShiftTo", cs);
        // the old bug: a From-less animation started right after setting the local value animates nothing
        Assert.DoesNotContain("Motion.To(w,", cs);
        // inline the pane has no stroke frame; the overlay surface has no stroke either
        var surface = System.Text.RegularExpressions.Regex.Match(xaml, @"<Border x:Name=""PaneSurface""[^>]*>").Value;
        Assert.DoesNotContain("BorderThickness", surface);
        Assert.Contains("ShellyPaneOverlayBrush", surface);
    }
}
