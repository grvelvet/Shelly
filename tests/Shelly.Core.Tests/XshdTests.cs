using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>The editor's PowerShell highlighting: every regex compiles (AvalonEdit itself needs WPF, so it is checked here).</summary>
public class XshdTests
{
    [Fact]
    public void Highlighting_regexes_compile_and_match()
    {
        string dir = AppContext.BaseDirectory;
        string? file = null;
        for (var d = new DirectoryInfo(dir); d != null && file == null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "src", "Shelly", "Editor", "PowerShell.xshd");
            if (File.Exists(p)) file = p;
        }
        Assert.NotNull(file);
        var doc = XDocument.Load(file!);
        XNamespace ns = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";
        var colors = doc.Descendants(ns + "Color").Select(c => (string)c.Attribute("name")!).ToHashSet();
        int n = 0;
        foreach (var e in doc.Descendants())
        {
            foreach (var a in e.Attributes().Where(a => a.Name == "begin" || a.Name == "end")) { _ = new Regex(a.Value, RegexOptions.IgnoreCase | RegexOptions.Multiline); n++; }
            if (e.Name == ns + "Begin" || e.Name == ns + "End" || e.Name == ns + "Rule") { _ = new Regex(e.Value, RegexOptions.IgnoreCase | RegexOptions.Multiline); n++; }
            var c = (string?)e.Attribute("color");
            if (c != null) Assert.Contains(c, colors);
        }
        Assert.True(n > 10);
        var cmd = new Regex(@"\b[A-Za-z]+-[A-Za-z][\w]*\b");
        Assert.Matches(cmd, "Get-ChildItem");
        var par = new Regex(@"(?<=\s)-[A-Za-z_][\w]*:?");
        Assert.Matches(par, "Get-Item -Path x");
    }
}
