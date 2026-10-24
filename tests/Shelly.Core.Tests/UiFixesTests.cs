using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.1.2 UI fixes: the scroll-bar gutter (lists no longer run under the overlay scroll bar of the Fluent
/// ScrollViewer), spacing checks on the WPF sources, and the Favorites sort.</summary>
public class UiFixesTests
{
    static string AppFile(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(new[] { d.FullName, "src", "Shelly" }.Concat(parts).ToArray());
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    // ---------- Scroll bar gutter ----------

    [Theory]
    [InlineData(500, 488, 16)]   // Fluent: content under the whole 12 px bar -> 12 + 4
    [InlineData(494, 488, 10)]   // content already padded 6 px (output panel) -> 6 + 4
    [InlineData(488, 488, 0)]    // classic bar in its own column: no overlap, no gutter
    [InlineData(480, 488, 0)]
    [InlineData(500.3, 488, 17)] // fractional overlap rounds up
    public void Scroll_gutter_is_the_overlap_plus_a_gap(double contentRight, double barLeft, double expected)
        => Assert.Equal(expected, Layout.ScrollGutter(contentRight, barLeft));

    [Fact]
    public void Lists_reserve_room_for_the_scroll_bar()
    {
        Assert.Matches(@"<ListBox x:Name=""List""[^>]*v:ScrollGutter\.Enabled=""True""", AppFile("Views", "ScriptsPage.xaml"));
        Assert.Matches(@"<ListBox x:Name=""OmniList""[^>]*v:ScrollGutter\.Enabled=""True""", AppFile("Views", "ScriptsPage.xaml"));
        Assert.Matches(@"<ListBox x:Name=""List""[^>]*v:ScrollGutter\.Enabled=""True""", AppFile("Views", "HistoryPage.xaml"));
        Assert.Matches(@"<ScrollViewer[^>]*v:ScrollGutter\.Enabled=""True""", AppFile("Views", "FavoritesPage.xaml"));
        Assert.Contains("ScrollGutter.SetEnabled(sv, true)", AppFile("Dialogs", "Dlg.cs"));
        Assert.Contains("ScrollGutter.SetEnabled(sv, true)", AppFile("Dialogs", "ImportFolderDialog.cs"));
        Assert.Contains("ScrollGutter.SetEnabled(ed, true)", AppFile("Views", "OutputView.cs"));
    }

    [Fact]
    public void Selection_and_focus_borders_stay_inside_the_list_items()
    {
        string styles = AppFile("Styles.xaml");
        foreach (var key in new[] { "RowItem", "CardRowItem", "GroupRowItem" })
        {
            var m = Regex.Match(styles, $@"<Style x:Key=""{key}"" TargetType=""ListBoxItem"">(.*?)</Style>", RegexOptions.Singleline);
            Assert.True(m.Success, key);
            Assert.DoesNotMatch(@"Margin=""-", m.Groups[1].Value);   // nothing drawn outside the item (the list clips there)
            Assert.Contains(@"<Setter Property=""FocusVisualStyle"" Value=""{x:Null}"" />", m.Groups[1].Value);
        }
    }

    [Fact]
    public void Spacing_follows_the_4_px_scale()
    {
        // Gap from a group header to its first card: 4 + 4 for every header (the first one had 2 + 4)
        Assert.Matches(@"x:Key=""FirstGroupRowItem""[^>]*>\s*<Setter Property=""Margin"" Value=""0,0,0,4"" />", AppFile("Styles.xaml"));
        Assert.Contains(@"<DockPanel Height=""52"" Margin=""16,0,16,0""", AppFile("Views", "HistoryPage.xaml"));
        // No 10 / 6 / 5 px gaps left in the dialogs and settings changed in 1.1.2
        foreach (var f in new[] { new[] { "Dialogs", "Dlg.cs" }, new[] { "Dialogs", "ImportFolderDialog.cs" }, new[] { "Views", "SettingsPage.cs" } })
        {
            string src = AppFile(f);
            foreach (Match m in Regex.Matches(src, @"Margin = new Thickness\(([^)]*)\)"))
                foreach (var v in m.Groups[1].Value.Split(',').Select(x => double.TryParse(x.Trim(), CultureInfo.InvariantCulture, out var d) ? d : 0))
                    Assert.True(Math.Abs(v) % 4 == 0 || Math.Abs(v) <= 2, $"{string.Join("/", f)}: Margin {m.Groups[1].Value}");
        }
    }

    // ---------- Favorites sort ----------

    static ScriptFile F(string rel) => new() { Rel = rel, Name = Path.GetFileNameWithoutExtension(rel) };
    static string Names(IEnumerable<ScriptFile> l) => string.Join(",", l.Select(f => f.Name));

    static T InCulture<T>(string name, Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo(name); return f(); }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Favorites_sort_by_each_key()
    {
        var gamma = F("gamma.py"); var alpha = F("alpha.ps1"); var beta = F("beta.ps1"); var delta = F("delta.py");
        var manual = new[] { gamma, alpha, beta, delta };
        var facts = new Dictionary<ScriptFile, ScriptFacts>
        {
            [gamma] = new(new DateTime(2026, 3, 1), 2, false, false, false, true),
            [alpha] = new(new DateTime(2026, 4, 1), 2, false, false, false, true),
            [beta] = new(null, 0, false, false, false, true),
            [delta] = new(new DateTime(2026, 1, 1), 7, false, false, false, true),
        };
        ScriptFacts Of(ScriptFile f) => facts[f];
        Assert.Equal("gamma,alpha,beta,delta", Names(FavoriteOrder.Sort(manual, "manual", Of)));
        Assert.Equal("alpha,beta,delta,gamma", Names(FavoriteOrder.Sort(manual, "name", Of)));
        Assert.Equal("alpha,gamma,delta,beta", Names(FavoriteOrder.Sort(manual, "lastrun", Of)));   // never run last
        Assert.Equal("delta,alpha,gamma,beta", Names(FavoriteOrder.Sort(manual, "runcount", Of)));  // tie 2/2 by name
        Assert.Equal("alpha,beta,delta,gamma", Names(FavoriteOrder.Sort(manual, "type", Of)));      // .ps1 then .py
        Assert.Equal("gamma,alpha,beta,delta", Names(FavoriteOrder.Sort(manual, "bogus", Of)));     // unknown = manual
        Assert.Empty(FavoriteOrder.Sort(Array.Empty<ScriptFile>(), "name", Of));
    }

    [Fact]
    public void Favorites_sort_is_stable_and_culture_aware()
    {
        var manual = new[] { F("Ёлка.ps1"), F("жук.ps1"), F("Арбуз.ps1"), F("same.ps1"), F("Same.py") };
        var none = new ScriptFacts(null, 0, false, false, false, true);
        // ru-RU: Ё sorts with Е (after А, before Ж); ordinal order would put Ё (U+0401) before А (U+0410).
        // The Russian collation puts Cyrillic before Latin; same / Same are equal ignoring case and keep the manual order.
        Assert.Equal("Арбуз,Ёлка,жук,same,Same", InCulture("ru-RU", () => Names(FavoriteOrder.Sort(manual, "name", _ => none))));
        // Equal keys keep the manual order (same / Same compare equal ignoring case)
        var rev = manual.Reverse().ToArray();
        Assert.Equal("Same,same", string.Join(",", InCulture("ru-RU", () => FavoriteOrder.Sort(rev, "name", _ => none)).Where(f => f.Name.Equals("same", StringComparison.OrdinalIgnoreCase)).Select(f => f.Name)));
        // Never run, no runs: ties by name, then the manual order
        Assert.Equal("Арбуз,Ёлка,жук,Same,same", InCulture("ru-RU", () => Names(FavoriteOrder.Sort(rev, "lastrun", _ => none))));
        Assert.Equal("Арбуз,Ёлка,жук,Same,same", InCulture("ru-RU", () => Names(FavoriteOrder.Sort(rev, "runcount", _ => none))));
    }

    [Fact]
    public void Favorites_sort_setting_round_trips_and_validates()
    {
        var s = new Settings();
        Assert.Equal("manual", s.FavoritesSort);
        s.LoadJson("{\"favoritesSort\":\"runcount\"}");
        Assert.Equal("runcount", s.FavoritesSort);
        Assert.Contains("\"favoritesSort\": \"runcount\"", s.ToJson());
        var t = new Settings();
        t.LoadJson("{\"favoritesSort\":\"size\"}");
        Assert.Equal("manual", t.FavoritesSort);
        Assert.Equal(new[] { "manual", "name", "lastrun", "runcount", "type" }, Settings.FavoritesSortChoices);
    }

    [Fact]
    public void Favorites_move_in_the_manual_order()
    {
        var s = new Settings { Favorites = new() { "a.ps1", "b.ps1", "c.py" } };
        Assert.True(s.MoveFavorite("C.PY", -1));
        Assert.Equal(new[] { "a.ps1", "c.py", "b.ps1" }, s.Favorites);
        Assert.False(s.MoveFavorite("a.ps1", -1));
        Assert.False(s.MoveFavorite("b.ps1", +1));
        Assert.False(s.MoveFavorite("x.ps1", +1));
        Assert.True(s.MoveFavorite("a.ps1", +1));
        Assert.Equal(new[] { "c.py", "a.ps1", "b.ps1" }, s.Favorites);
    }

    [Fact]
    public void Favorites_sort_texts_exist_in_both_languages()
    {
        foreach (var lang in new[] { "en", "ru" })
        {
            string old = Loc.Language;
            try
            {
                Loc.SetLanguage(lang);
                foreach (var k in new[] { "favSortManual", "favSortName", "favSortLastRun", "favSortRunCount", "favSortType", "favMoveEarlier", "favMoveLater" })
                    Assert.NotEqual(k, Loc.T(k));
            }
            finally { Loc.SetLanguage(old); }
        }
        Assert.Equal("Вручную", InRu("favSortManual"));
        Assert.Equal("По частоте запусков", InRu("favSortRunCount"));
    }

    // ---------- Favorites tile size ----------

    [Fact]
    public void Favorites_size_defaults_to_the_old_tile()
    {
        Assert.Equal("medium", new Settings().FavoritesSize);
        var m = Layout.FavoriteTileFor("medium");
        // The tile before 1.1.2: 248 x 132, padding 16,12,8,8, 32 px icon (glyph 15, image 28), name 14, description 12
        Assert.Equal((248.0, 132.0, new Layout.Thickness4(16, 12, 8, 8), 32.0, 15.0, 28.0, 12.0, 14.0, 12.0, 8.0),
            (m.Width, m.Height, m.Padding, m.Icon, m.Glyph, m.Image, m.IconGap, m.NameSize, m.DescSize, m.DescGap));
        // Old settings files (no key) load as medium
        var s = new Settings();
        s.LoadJson("{\"favorites\":[\"a.ps1\"]}");
        Assert.Equal("medium", s.FavoritesSize);
    }

    [Fact]
    public void Favorites_size_round_trips_and_validates()
    {
        var s = new Settings();
        s.LoadJson("{\"favoritesSize\":\"large\"}");
        Assert.Equal("large", s.FavoritesSize);
        Assert.Contains("\"favoritesSize\": \"large\"", s.ToJson());
        foreach (var bad in new[] { "huge", "", "Large", "42" })
        {
            var t = new Settings();
            t.LoadJson("{\"favoritesSize\":\"" + bad + "\"}");
            Assert.Equal("medium", t.FavoritesSize);
        }
        Assert.Equal("medium", Layout.FavoriteTileFor("nonsense").Key);
        Assert.Equal("medium", Layout.FavoriteTileFor(null).Key);
    }

    [Fact]
    public void Favorites_size_presets_grow_and_stay_on_the_spacing_scale()
    {
        Assert.Equal(new[] { "small", "medium", "large" }, Layout.FavoriteTiles.Select(t => t.Key));
        for (int i = 1; i < Layout.FavoriteTiles.Length; i++)
        {
            var a = Layout.FavoriteTiles[i - 1]; var b = Layout.FavoriteTiles[i];
            Assert.True(b.Width > a.Width && b.Height > a.Height && b.Icon > a.Icon && b.Glyph > a.Glyph && b.Image > a.Image && b.NameSize > a.NameSize && b.DescSize >= a.DescSize);
        }
        foreach (var t in Layout.FavoriteTiles)
        {
            foreach (var v in new[] { t.Padding.Left, t.Padding.Top, t.Padding.Right, t.Padding.Bottom, t.IconGap, t.DescGap, t.Width, t.Height, t.Icon })
                Assert.True(v % 4 == 0, $"{t.Key}: {v}");
            Assert.True(t.Image < t.Icon && t.DescSize >= 12);
            // Room for the header row, the description and the 32 px button row
            Assert.True(t.Height - t.Padding.Top - t.Padding.Bottom - t.Icon - t.DescGap - 32 >= 2 * t.DescSize, t.Key);
        }
    }

    [Fact]
    public void Ctrl_wheel_steps_the_size_and_stops_at_the_ends()
    {
        Assert.Equal("large", Layout.StepFavoriteTile("medium", 120));
        Assert.Equal("small", Layout.StepFavoriteTile("medium", -120));
        Assert.Equal("large", Layout.StepFavoriteTile("large", 120));
        Assert.Equal("small", Layout.StepFavoriteTile("small", -240));
        Assert.Equal("medium", Layout.StepFavoriteTile("small", 1));
        Assert.Equal("large", Layout.StepFavoriteTile("bogus", 120));   // unknown = medium, one step up
    }

    [Fact]
    public void Favorites_size_ui_uses_the_presets()
    {
        string xaml = AppFile("Views", "FavoritesPage.xaml");
        Assert.Contains(@"x:Name=""SizeBtn""", xaml);
        Assert.Contains(@"Width=""{DynamicResource FavTileWidth}"" Height=""{DynamicResource FavTileHeight}""", xaml);
        Assert.DoesNotContain(@"Width=""248""", xaml);
        Assert.Matches(@"<ScrollViewer[^>]*v:ScrollGutter\.Enabled=""True""", xaml);
        string cs = AppFile("Views", "FavoritesPage.xaml.cs");
        Assert.Contains("PreviewMouseWheel", cs);
        Assert.Contains("ModifierKeys.Control", cs);
        foreach (var lang in new[] { "en", "ru" })
            foreach (var k in new[] { "favSizeTip", "favSizeSmall", "favSizeMedium", "favSizeLarge", "favSizeWheel" })
            {
                string old = Loc.Language;
                try { Loc.SetLanguage(lang); Assert.NotEqual(k, Loc.T(k)); }
                finally { Loc.SetLanguage(old); }
            }
        Assert.Equal("Размер: Крупные", InRuF("favSizeTip", InRu("favSizeLarge")));
        Assert.Equal("Мелкие", InRu("favSizeSmall"));
        Assert.Equal("Средние", InRu("favSizeMedium"));
    }

    static string InRuF(string key, string arg)
    {
        string old = Loc.Language;
        try { Loc.SetLanguage("ru"); return Loc.F(key, arg); }
        finally { Loc.SetLanguage(old); }
    }

    static string InRu(string key)
    {
        string old = Loc.Language;
        try { Loc.SetLanguage("ru"); return Loc.T(key); }
        finally { Loc.SetLanguage(old); }
    }
}
