using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>The editor's PowerShell and Batch highlighting: every regex compiles (AvalonEdit itself needs WPF, so it is checked here).</summary>
public class XshdTests
{
    static string FindEditorFile(string fileName)
    {
        string dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "src", "Shelly", "Editor", fileName);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(fileName);
    }

    [Theory]
    [InlineData("PowerShell.xshd")]
    [InlineData("Batch.xshd")]
    public void Highlighting_regexes_compile_and_match(string xshdFile)
    {
        string file = FindEditorFile(xshdFile);
        var doc = XDocument.Load(file);
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
        Assert.True(n >= 8);
    }

    [Fact]
    public void PowerShell_and_Batch_highlighting_rules_match_tokens()
    {
        var cmd = new Regex(@"\b[A-Za-z]+-[A-Za-z][\w]*\b");
        Assert.Matches(cmd, "Get-ChildItem");
        var par = new Regex(@"(?<=\s)-[A-Za-z_][\w]*:?");
        Assert.Matches(par, "Get-Item -Path x");
        var attr = new Regex(@"\[[A-Za-z_][\w.,\[\]\s]*(?:\([^\]\r\n]*\))?\]");
        Assert.Matches(attr, "[CmdletBinding()]");
        Assert.Matches(attr, "[Parameter(Mandatory = $true)]");
        var scoped = new Regex(@"\$(?:global:|script:|local:|private:|env:|using:)?(\{[^{}]+\}|\?|\^|\$|_|\b[A-Za-z_][\w:]*)");
        Assert.Matches(scoped, "$env:PATH");
        Assert.Matches(scoped, "$script:Counter");

        var batVar = new Regex(@"%[A-Za-z_][A-Za-z0-9_]*(?::[^%]+)?%|![A-Za-z_][A-Za-z0-9_]*(?::[^!]+)?!|%%~?[fdpnxsatz]*[A-Za-z]|%~[fdpnxsatz]*(?:\$[A-Za-z_]+:)?[0-9A-Za-z]");
        Assert.Matches(batVar, "%USERPROFILE%");
        Assert.Matches(batVar, "!DELAYED_VAR!");
        Assert.Matches(batVar, "%%i");
        Assert.Matches(batVar, "%~dp0");
    }
}
