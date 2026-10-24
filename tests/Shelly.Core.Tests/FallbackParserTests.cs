using Xunit;

namespace Shelly.Core.Tests;

public class FallbackParserTests
{
    const string Script = @"#Requires -Version 5.1
# Greets someone (description line)
<# block
   comment #>
[CmdletBinding()]
param(
    # who to greet
    [Parameter(Mandatory = $true, HelpMessage = 'Your name')]
    [string]$Name,
    [ValidateSet('Hi', ""Hello"", 'Hey')]
    [string]$Greeting = 'Hello',
    [switch]$Loud,
    [bool]$Polite = $true,
    [int[]]$Times = @(1, 2),
    [Parameter()][datetime]$When,
    $Untyped = ""a, b (c)"",
    [string]$Computed = (Get-Date).ToString()
)
Write-Host ""$Greeting, $Name""
";

    [Fact]
    public void Reads_param_block()
    {
        var ps = FallbackParser.Params(Script);
        Assert.Equal(new[] { "Name", "Greeting", "Loud", "Polite", "Times", "When", "Untyped", "Computed" }, ps.Select(p => p.Name));
        var name = ps[0];
        Assert.True(name.Mandatory);
        Assert.Equal("Your name", name.Help);
        Assert.Equal("string", name.Type);
        Assert.Equal("text", name.Kind);
        Assert.Equal(new[] { "Hi", "Hello", "Hey" }, ps[1].SetValues);
        Assert.Equal("Hello", ps[1].Default);
        Assert.Equal("switch", ps[2].Kind);
        Assert.False(ps[2].DefaultBool);
        Assert.Equal("bool", ps[3].Kind);
        Assert.True(ps[3].DefaultBool);
        Assert.Equal("array", ps[4].Kind);
        Assert.Equal("1, 2", ps[4].Default);
        Assert.False(ps[5].Mandatory);
        Assert.Equal("datetime", ps[5].Type);
        Assert.Equal("a, b (c)", ps[6].Default);
        Assert.Null(ps[7].Default);                 // not a constant: no value, like SafeGetValue
        Assert.Equal("(Get-Date).ToString()", ps[7].DefaultText);
    }

    [Fact]
    public void Description_and_requires()
    {
        Assert.Equal("Greets someone (description line)", FallbackParser.Description(Script));
        var r = FallbackParser.Requires("#Requires -Version 7.2\n#requires -PSEdition Core\n#Requires -RunAsAdministrator\n");
        Assert.Equal("7.2", r.Version);
        Assert.Equal(new[] { "Core" }, r.Editions);
        Assert.True(r.Elevation);
    }

    [Fact]
    public void No_param_block_or_param_later_in_the_script()
    {
        Assert.Empty(FallbackParser.Params("Write-Host 'hi'\nfunction f { param($x) }"));
        Assert.Empty(FallbackParser.Params(""));
        Assert.Single(FallbackParser.Params("using namespace System.IO\nparam([string]$p)\n"));
    }

    [Fact]
    public void Mandatory_forms_and_brackets_in_strings()
    {
        var ps = FallbackParser.Params("param([Parameter(Mandatory)]$A, [Parameter(Mandatory=$false)]$B, [string]$C = ')]', $D = 'it''s')");
        Assert.True(ps[0].Mandatory);
        Assert.False(ps[1].Mandatory);
        Assert.Equal(")]", ps[2].Default);
        Assert.Equal("it's", ps[3].Default);
    }

    [Fact]
    public void Real_example_script()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Hello with parameters.ps1");
        var r = FallbackParser.ParseFile(path);
        Assert.NotEmpty(r.Params);
        Assert.True(r.Fallback);
        Assert.False(string.IsNullOrEmpty(r.Desc));
    }

    [Fact]
    public void Decodes_encodings()
    {
        Assert.Equal("привет", TextFile.Decode(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("привет")).ToArray(), out var e1));
        Assert.Equal("utf8bom", e1);
        TextFile.Decode(System.Text.Encoding.UTF8.GetBytes("привет"), out var e2);
        Assert.Equal("utf8", e2);
        TextFile.Decode(System.Text.Encoding.ASCII.GetBytes("hello"), out var e3);
        Assert.Equal("ascii", e3);
        TextFile.Decode(new byte[] { 0xCF, 0xF0, 0xE8 }, out var e4);   // cp1251, not valid UTF-8
        Assert.Equal("ansi", e4);
    }
}
