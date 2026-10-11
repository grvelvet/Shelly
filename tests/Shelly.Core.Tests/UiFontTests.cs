using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>Static checks of the WPF sources (WPF itself can't run here): the symbol font (Segoe Fluent Icons) may only
/// be set on TextBlocks that show a glyph. A ToolTip, ContextMenu or Popup inherits FontFamily from the element it
/// belongs to, so a symbol font on a Button, a MenuItem or a glyph that has a tooltip turns its text into boxes.</summary>
public class UiFontTests
{
    static string AppDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "src", "Shelly");
            if (File.Exists(Path.Combine(p, "Shelly.csproj"))) return p;
        }
        throw new DirectoryNotFoundException("src/Shelly");
    }

    static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(AppDir(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    static readonly string[] GlyphStyles = { "Glyph", "Spinner" };

    static bool UsesIconFont(XElement e)
    {
        string? style = (string?)e.Attribute("Style");
        if (style != null && GlyphStyles.Any(g => Regex.IsMatch(style, @"\b" + g + @"\}"))) return true;
        if (((string?)e.Attribute("FontFamily"))?.Contains("IconFont") == true) return true;
        // An inline <X.Style><Style BasedOn="{StaticResource Glyph}"> or a FontFamily setter
        foreach (var st in e.Elements().Where(c => c.Name.LocalName.EndsWith(".Style")).SelectMany(c => c.Elements()))
        {
            string? based = (string?)st.Attribute("BasedOn");
            if (based != null && GlyphStyles.Any(g => based.Contains(g + "}"))) return true;
            if (st.Elements().Any(s => (string?)s.Attribute("Property") == "FontFamily" && ((string?)s.Attribute("Value"))?.Contains("IconFont") == true)) return true;
        }
        return false;
    }

    static bool HasPopupText(XElement e) =>
        e.Attribute("ToolTip") != null || e.Attribute("ContextMenu") != null ||
        e.Elements().Any(c => c.Name.LocalName.EndsWith(".ToolTip") || c.Name.LocalName.EndsWith(".ContextMenu"));

    [Fact]
    public void Xaml_symbol_font_only_on_glyph_TextBlocks_without_tooltips_or_menus()
    {
        int glyphs = 0;
        foreach (var file in Files("*.xaml"))
        {
            var doc = XDocument.Load(file);
            foreach (var e in doc.Descendants())
            {
                string name = e.Name.LocalName;
                // Styles: a FontFamily = IconFont setter is allowed only in TextBlock styles
                if (name == "Style")
                {
                    bool iconSetter = e.Elements().Any(s => s.Name.LocalName == "Setter" && (string?)s.Attribute("Property") == "FontFamily"
                        && ((string?)s.Attribute("Value"))?.Contains("IconFont") == true);
                    if (iconSetter) Assert.True((string?)e.Attribute("TargetType") == "TextBlock", $"{Path.GetFileName(file)}: style {(string?)e.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Key")} sets the symbol font on {(string?)e.Attribute("TargetType")}");
                    continue;
                }
                if (name.Contains('.') || !UsesIconFont(e)) continue;
                glyphs++;
                Assert.True(name == "TextBlock", $"{Path.GetFileName(file)}: symbol font on a {name}");
                Assert.False(HasPopupText(e), $"{Path.GetFileName(file)}: glyph TextBlock '{(string?)e.Attribute("Text")}' has a tooltip or menu; put it on a parent element");
            }
            // Templates may apply the symbol font to the content only (TextElement.FontFamily on a ContentPresenter)
            foreach (var e in doc.Descendants().Where(x => x.Attributes().Any(a => a.Name.LocalName == "TextElement.FontFamily" && a.Value.Contains("IconFont"))))
                Assert.Equal("ContentPresenter", e.Name.LocalName);
        }
        Assert.True(glyphs > 10, "the check found the glyphs");
    }

    [Fact]
    public void Icon_button_styles_do_not_set_the_symbol_font_on_the_button()
    {
        var doc = XDocument.Load(Path.Combine(AppDir(), "Styles.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var key in new[] { "IconButton", "IconToggle" })
        {
            var style = doc.Descendants().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(x + "Key") == key);
            Assert.DoesNotContain(style.Elements(), s => (string?)s.Attribute("Property") is "FontFamily" or "FontSize");
            Assert.Contains(style.Descendants(), e => e.Name.LocalName == "ContentPresenter" && e.Attributes().Any(a => a.Name.LocalName == "TextElement.FontFamily"));
        }
    }

    [Fact]
    public void Code_sets_the_symbol_font_only_on_TextBlocks_without_tooltips()
    {
        int n = 0;
        foreach (var file in Files("*.cs"))
        {
            string src = File.ReadAllText(file);
            string fn = Path.GetFileName(file);
            Assert.DoesNotMatch(new Regex(@"FontFamily\s*=\s*[^;]*(IconFont|Fluent Icons|MDL2)"), src);
            foreach (Match m in Regex.Matches(src, @"(\w+)\.SetResourceReference\(\s*([\w.]*)FontFamilyProperty\s*,\s*""IconFont""\s*\)"))
            {
                n++;
                Assert.True(m.Groups[2].Value == "TextBlock.", $"{fn}: {m.Value} must target TextBlock.FontFamilyProperty");
                string v = m.Groups[1].Value;
                // The variable must be declared as a TextBlock and get no tooltip or menu
                Assert.Matches(new Regex(@"\b" + v + @"\s*=\s*new\s+TextBlock\b|TextBlock\s+" + v + @"\b"), src);
                Assert.DoesNotMatch(new Regex(@"\b" + v + @"\.(ToolTip|ContextMenu)\s*="), src);
                var decl = Regex.Match(src, @"\b" + v + @"\s*=\s*new\s+TextBlock\s*\{[^}]*\}");
                if (decl.Success) Assert.DoesNotContain("ToolTip", decl.Value);
            }
        }
        Assert.True(n >= 3, "the check found the code glyphs");
    }
}
