using System.Text.RegularExpressions;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.1.2: drop-down widths (static checks of the WPF sources; WPF itself can't run here). Every drop-down of
/// Settings uses the shared SettingsPage.ComboWidth, the History filter too, and the run settings boxes trim their text
/// with an ellipsis instead of cutting it off.</summary>
public class ComboWidthTests
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

    static T InLang<T>(string lang, Func<T> f)
    {
        string old = Loc.Language;
        try { Loc.SetLanguage(lang); return f(); }
        finally { Loc.SetLanguage(old); }
    }

    static double SharedWidth()
    {
        var m = Regex.Match(AppFile("Views", "SettingsPage.cs"), @"public const double ComboWidth = (\d+);");
        Assert.True(m.Success, "SettingsPage.ComboWidth");
        return double.Parse(m.Groups[1].Value);
    }

    [Fact]
    public void Settings_drop_downs_use_the_shared_width()
    {
        string src = AppFile("Views", "SettingsPage.cs");
        Assert.Contains("static ComboBox Combo(IEnumerable<(string Value, string Text)> items, string current, Action<string> changed)", src);
        Assert.Contains("new ComboBox { MinWidth = ComboWidth", src);
        Assert.Single(Regex.Matches(src, @"new ComboBox\b"));   // only the helper makes one
        Assert.DoesNotMatch(new Regex(@"\}\s*,\s*\d+\s*\)\s*\)?\s*;"), src);   // no per-call width like "..., 180));" any more
        Assert.Matches(new Regex(@"<ComboBox x:Name=""Filter"" MinWidth=""\{x:Static v:SettingsPage\.ComboWidth\}"""), AppFile("Views", "HistoryPage.xaml"));
    }

    [Fact]
    public void Run_settings_boxes_trim_long_text()
    {
        string src = AppFile("Dialogs", "RunSettingsWindow.cs");
        Assert.Contains("TextTrimming.CharacterEllipsis", src);
        Assert.DoesNotContain("new ComboBoxItem", src);   // all items go through Item(), which sets the trimming template
    }

    /// <summary>A rough guard for new texts: 14 px Segoe UI averages under 7.5 px per character for these strings, plus
    /// about 52 px of Fluent padding and chevron. An estimate, not a measurement.</summary>
    [Fact]
    public void Longest_settings_items_fit_the_shared_width()
    {
        string[] keys = { "setThemeSystem", "setThemeLight", "setThemeDark", "setEngSystem", "setEng51", "setEng7",
            "histFilterAll", "histFilterOk", "histFilterFail", "histFilterStopped", "histFilterWindow" };
        double width = SharedWidth();
        foreach (var lang in new[] { "en", "ru" })
            foreach (var k in keys)
            {
                string t = InLang(lang, () => Loc.T(k));
                Assert.Equal(148, width);
                Assert.True(t.Length * 6.4 + 32 <= width, lang + ": " + k + " = " + t);
            }
    }
}
