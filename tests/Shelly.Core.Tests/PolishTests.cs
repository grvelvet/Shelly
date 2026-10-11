using System.Text.RegularExpressions;
using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.0.5: accent shades, the engine mark of script rows, the title bar on every window, motion hooks.</summary>
public class PolishTests
{
    static int Dist(Rgb a, Rgb b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    [Fact]
    public void Shades_of_the_Windows_blue_are_close_to_what_Windows_computes()
    {
        var s = AccentPalette.Shades(Rgb.Parse("#0078D4"));
        // Windows: Light2 #4CC2FF is hue-shifted, so allow some distance; the lightness order must hold exactly
        Assert.True(Dist(s["Light2"], Rgb.Parse("#4CC2FF")) < 70, s["Light2"].ToString());
        Assert.True(Dist(s["Dark1"], Rgb.Parse("#005FB8")) < 30, s["Dark1"].ToString());
        string[] order = { "Dark3", "Dark2", "Dark1", "Base", "Light1", "Light2", "Light3" };
        for (int i = 1; i < order.Length; i++)
            Assert.True(AccentPalette.Luminance(s[order[i]]) > AccentPalette.Luminance(s[order[i - 1]]), order[i]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Accent_fill_keeps_its_text_readable_for_every_swatch(bool dark)
    {
        foreach (var hex in Settings.AccentChoices.Append("#FFB900").Append("#0078D4").Append("#000000").Append("#FFFFFF"))
        {
            var shades = AccentPalette.Shades(Rgb.Parse(hex));
            var fill = AccentPalette.Fill(shades, dark);
            double c = AccentPalette.Contrast(fill, AccentPalette.OnAccentText(dark));
            Assert.True(c >= 4.5, $"{hex} {(dark ? "dark" : "light")}: {fill} {c:0.00}");
            // Dark theme uses the lighter shade, light theme the darker one
            if (dark) Assert.True(AccentPalette.Luminance(fill) >= AccentPalette.Luminance(shades["Base"]) - 1e-9, hex);
            else Assert.True(AccentPalette.Luminance(fill) <= AccentPalette.Luminance(shades["Base"]) + 1e-9, hex);
        }
    }

    [Fact]
    public void Hsl_round_trip()
    {
        foreach (var hex in Settings.AccentChoices)
        {
            var c = Rgb.Parse(hex);
            var (h, s, l) = AccentPalette.ToHsl(c);
            Assert.True(Dist(c, AccentPalette.FromHsl(h, s, l)) <= 1, hex);
        }
    }

    [Theory]
    // engine, auto, missing, pwshOk, ps51Ok → shown, text, problem
    [InlineData("7", true, false, true, true, false, "", false)]        // Auto → 7: nothing
    [InlineData("5.1", true, false, true, true, true, "PS 5.1", false)] // resolves to 5.1
    [InlineData("7", false, false, true, true, true, "PS 7", false)]    // set by hand
    [InlineData("5.1", false, false, true, true, true, "PS 5.1", false)]
    [InlineData("7", true, true, false, true, true, "PS 7", true)]      // needs 7, not installed
    [InlineData("5.1", true, false, true, false, true, "PS 5.1", true)] // 5.1 missing
    public void Engine_mark_only_when_informative(string engine, bool auto, bool missing, bool pwsh, bool ps51, bool show, string text, bool problem)
    {
        var ei = new EngineInfo { Engine = engine, Auto = auto, Missing = missing };
        var m = ei.RowMark(pwsh, ps51);
        Assert.Equal(show, m.Show);
        Assert.Equal(text, m.Text);
        Assert.Equal(problem, m.Problem);
    }

    static string AppDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "src", "Shelly");
            if (File.Exists(Path.Combine(p, "Shelly.csproj"))) return p;
        }
        throw new DirectoryNotFoundException("src/Shelly");
    }

    [Fact]
    public void Every_window_gets_the_integrated_title_bar()
    {
        int n = 0;
        foreach (var f in Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("obj") && !f.Contains("bin")))
        {
            string src = File.ReadAllText(f);
            if (!Regex.IsMatch(src, @"class\s+\w+\s*:\s*Window\b")) continue;
            n++;
            Assert.Matches(@"TitleBar\.Attach\(this[,)]", src);
        }
        Assert.True(n >= 3, "main window, dialogs, editor");
    }

    [Fact]
    public void Script_rows_have_no_engine_pill_and_hover_fills_fade()
    {
        string rows = File.ReadAllText(Path.Combine(AppDir(), "Views", "ScriptsPage.xaml"));
        Assert.DoesNotContain("Binding Engine}", rows);
        Assert.Contains("EngineMark", rows);
        string styles = File.ReadAllText(Path.Combine(AppDir(), "Styles.xaml"));
        // (the fallback caption buttons switch instantly, like the classic system ones)
        styles = Regex.Replace(styles, @"<Style x:Key=""Caption(Close)?Button"".*?</Style>", "", RegexOptions.Singleline);
        // Hover fills go through Motion (83 ms, off with Windows animation effects), not an instant Background setter
        Assert.DoesNotMatch(new Regex(@"Property=""IsMouseOver""[^>]*>\s*<Setter TargetName=""Bd"" Property=""Background"" Value=""\{DynamicResource (Subtle|Control)FillColorSecondaryBrush\}"""), styles);
        Assert.True(Regex.Matches(styles, "v:Motion.Show").Count >= 6);
    }

    // ---------- 1.0.7: Wintoys / NavigationView look ----------

    [Fact]
    public void Focus_rings_only_for_keyboard_and_cards_select_with_an_accent_border()
    {
        string styles = File.ReadAllText(Path.Combine(AppDir(), "Styles.xaml"));
        foreach (var f in Directory.EnumerateFiles(AppDir(), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains("obj") && !f.Contains("bin")))
        {
            string src = File.ReadAllText(f);
            // A trigger on IsKeyboardFocused also fires on a mouse click; rings go through FocusRing.Show
            Assert.DoesNotContain("Property=\"IsKeyboardFocused\"", src);
            Assert.DoesNotContain("Property = IsKeyboardFocusedProperty", src);
        }
        int a = styles.IndexOf("x:Key=\"CardRowItem\""), b = styles.IndexOf("</Style>", a);
        string card = styles.Substring(a, b - a);
        Assert.DoesNotContain("Pill", card);
        Assert.Matches(@"IsSelected""\s+Value=""True"">\s*<Setter TargetName=""Bd"" Property=""BorderBrush"" Value=""\{DynamicResource AccentFillColorDefaultBrush\}""", card);
    }

    [Fact]
    public void Content_is_a_separate_layer_below_a_tall_title_row()
    {
        string xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"Body\"", xaml);
        Assert.Contains("ShellyLayerFillBrush", xaml);
        Assert.Contains("CornerRadius=\"8,0,0,0\" BorderThickness=\"1,1,0,0\"", xaml);
        // the status line is inside the layer (after Body opens, before it closes)
        int body = xaml.IndexOf("x:Name=\"Body\""), status = xaml.IndexOf("x:Name=\"StatusBar\""), end = xaml.IndexOf("</Border>", status);
        Assert.True(body > 0 && status > body && end > status);
        string cs = File.ReadAllText(Path.Combine(AppDir(), "Views", "MainWindow.xaml.cs"));
        Assert.Contains("TitleBar.Attach(this, TitleBar.TallHeight)", cs);
        string theme = File.ReadAllText(Path.Combine(AppDir(), "Services", "ThemeManager.cs"));
        Assert.Contains("Color.FromArgb(0x4C, 0x3A, 0x3A, 0x3A) : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)", theme);
    }
}
