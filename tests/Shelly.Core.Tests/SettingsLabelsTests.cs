using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.1.2: Settings > Environment labels (Runtime, PowerShell version: System / PowerShell 5.1 / PowerShell 7+) and
/// the removed "Script icons" row. The stored values of engineDefault and the per-script icons are unchanged.</summary>
public class SettingsLabelsTests
{
    static T InLang<T>(string lang, Func<T> f)
    {
        string old = Loc.Language;
        try { Loc.SetLanguage(lang); return f(); }
        finally { Loc.SetLanguage(old); }
    }

    static string SettingsPageSource()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "src", "Shelly", "Views", "SettingsPage.cs");
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException("src/Shelly/Views/SettingsPage.cs");
    }

    [Theory]
    [InlineData("en", "setShell", "Runtime")]
    [InlineData("en", "setShellNote", "Choose which environment to configure (PowerShell, Python or custom interpreters). Batch files (.bat, .cmd) need no setup: cmd.exe is part of Windows")]
    [InlineData("en", "setEngDefault", "PowerShell version")]
    [InlineData("en", "setEngDefaultNote", "Used for automatic selection or when no version is set explicitly")]
    [InlineData("en", "setEngSystem", "System")]
    [InlineData("en", "setEng51", "PowerShell 5.1")]
    [InlineData("en", "setEng7", "PowerShell 7+")]
    [InlineData("ru", "setShell", "Среда выполнения")]
    [InlineData("ru", "setShellNote", "Выберите среду настройки параметров (PowerShell, Python или свои интерпретаторы). Bat-файлам (.bat, .cmd) настройка не нужна: cmd.exe есть в Windows")]
    [InlineData("ru", "setEngDefault", "Версия PowerShell")]
    [InlineData("ru", "setEngDefaultNote", "Используется при автоматическом выборе или если версия не задана явно")]
    [InlineData("ru", "setEngSystem", "Системная")]
    [InlineData("ru", "setEng51", "PowerShell 5.1")]
    [InlineData("ru", "setEng7", "PowerShell 7+")]
    public void Environment_labels(string lang, string key, string expected) => Assert.Equal(expected, InLang(lang, () => Loc.T(key)));

    [Fact]
    public void Shared_engine_names_are_unchanged()
    {
        // The run settings window, the row marks and the output header keep their own texts
        Assert.Equal("Windows PowerShell 5.1", InLang("en", () => Loc.T("eng51")));
        Assert.Equal("PowerShell 7", InLang("en", () => Loc.T("eng7")));
        Assert.Equal("Auto ({0})", InLang("en", () => Loc.T("rsEngAutoShort")));
        Assert.Equal("Авто ({0})", InLang("ru", () => Loc.T("rsEngAutoShort")));
        Assert.False(Loc.Has("engAuto"));   // only the settings list used it; replaced by setEngSystem
    }

    [Fact]
    public void Script_icons_row_is_gone()
    {
        Assert.False(Loc.Has("setIcons"));
        Assert.False(Loc.Has("setIconsNote"));
        string src = SettingsPageSource();
        Assert.DoesNotContain("setIcons", src);
        // The list stores the same values as before
        Assert.Contains("(\"auto\", Loc.T(\"setEngSystem\")), (\"5.1\", Loc.T(\"setEng51\")), (\"7\", Loc.T(\"setEng7\"))", src);
        // Changing a script's icon from its context menu still works
        Assert.True(Loc.Has("ctxIcon"));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("5.1")]
    [InlineData("7")]
    public void Stored_engine_default_values_are_unchanged(string v)
    {
        var s = new Settings();
        s.LoadJson("{\"engineDefault\": \"" + v + "\"}");
        Assert.Equal(v, s.EngineDefault);
        var t = new Settings();
        t.LoadJson(s.ToJson());
        Assert.Equal(v, t.EngineDefault);
    }

    [Fact]
    public void Old_settings_with_script_icons_still_load()
    {
        var s = new Settings();
        s.LoadJson("{\"scriptIcons\": [\"a.ps1|assets\\\\script-icons\\\\a.png\"], \"engineDefault\": \"auto\"}");
        Assert.Equal("assets\\script-icons\\a.png", s.GetIcon("a.ps1"));
        Assert.Contains("\"scriptIcons\"", s.ToJson());
    }
}
