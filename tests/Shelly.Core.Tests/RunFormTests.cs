using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.0.9: the compact Run settings dialog.</summary>
public class RunFormTests
{
    static ParamInfo P(string name, string type = "", string kind = "text", string? def = null, string defText = "", bool mandatory = false) =>
        new() { Name = name, Type = type, Kind = kind, Default = def, DefaultText = defText, Mandatory = mandatory };

    [Fact]
    public void Placeholders_show_the_default_or_the_type()
    {
        Loc.Language = "en";
        Assert.Equal("30", RunForm.Placeholder(P("Days", "int", def: "30")));
        Assert.Equal("out.csv", RunForm.Placeholder(P("OutFile", "string", def: "'out.csv'")));
        Assert.Equal("set by the script", RunForm.Placeholder(P("OutFile", "string", def: "(Join-Path $env:TEMP 'x.csv')")));
        Assert.Equal("set by the script", RunForm.Placeholder(P("OutFile", "string", defText: "$env:TEMP")));
        Assert.Equal("number", RunForm.Placeholder(P("Count", "int", mandatory: true)));
        Assert.Null(RunForm.Placeholder(P("Force", kind: "switch")));
        Assert.Null(RunForm.Placeholder(new ParamInfo { Name = "Mode", SetValues = new() { "a", "b" } }));
        string tip = RunForm.Tip(P("Days", "int", def: "30", mandatory: true));
        Assert.Contains("number", tip);
        Assert.Contains("mandatory", tip);
        Assert.Contains("30", tip);
    }

    [Fact]
    public void Two_parameter_script_fits_the_target_height()
    {
        // usd_rub: Days, OutFile; with and without a last run
        Assert.InRange(RunForm.EstimateHeight(2, lastRun: false), 330, 380);
        Assert.InRange(RunForm.EstimateHeight(2, lastRun: true), 330, 380);
        Assert.True(RunForm.EstimateHeight(0, false) < RunForm.EstimateHeight(2, false));
    }

    [Fact]
    public void Dialog_source_is_compact()
    {
        string? dir = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Shelly", "Dialogs", "RunSettingsWindow.cs"))) { dir = Path.Combine(d.FullName, "src", "Shelly"); break; }
        if (dir == null) return;
        string src = File.ReadAllText(Path.Combine(dir, "Dialogs", "RunSettingsWindow.cs"));
        Assert.DoesNotContain("rsIcon", src);            // the icon section is gone (row menu: Change icon…)
        Assert.DoesNotContain("Card(", src);             // no cards
        Assert.DoesNotContain("Section(", src);          // no section titles
        Assert.DoesNotContain("rsParamNote", src);       // placeholders replace the footnote
        Assert.DoesNotContain("RadioButton", src);       // mode is a combo box next to the engine
        Assert.Contains("ContentDialogLayout()", src);
        var m = System.Text.RegularExpressions.Regex.Match(src, @"DialogWidth = (\d+)");
        Assert.InRange(double.Parse(m.Groups[1].Value), 480, 548);
    }

    [Fact]
    public void Texts_exist_in_both_languages()
    {
        try
        {
            foreach (var k in new[] { "phComputed", "rsEngLabel", "rsEngAutoShort", "rsModeLabel", "rsModeNormal", "rsRememberShort", "rsLastEntry", "rsModeTip", "rsRequired" })
            {
                Loc.Language = "en"; string en = Loc.T(k);
                Loc.Language = "ru"; string ru = Loc.T(k);
                Assert.NotEqual(k, en);
                if (k != "rsEngLabel") Assert.NotEqual(en, ru);
            }
        }
        finally { Loc.Language = "en"; }
    }
}
