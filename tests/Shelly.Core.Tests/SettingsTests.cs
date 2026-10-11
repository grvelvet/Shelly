using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

public class SettingsTests
{
    // A launcher-settings.json as written by Shelly 0.9.8 (PowerShell edition)
    const string Ps098 = "\uFEFF{\r\n  \"language\": \"ru\",\r\n  \"theme\": \"dark\",\r\n  \"accent\": \"#8764B8\",\r\n  \"startMinimized\": false,\r\n  \"closeToTray\": true,\r\n  \"hotkey\": \"Ctrl+Alt+L\",\r\n" +
        "  \"favorites\": [\r\n    \"System\\\\System summary.ps1\",\r\n    \"usd_rub.ps1\"\r\n  ],\r\n  \"favoritesBase\": \"Scripts\",\r\n  \"trayHintShown\": true,\r\n" +
        "  \"historyMax\": \"200\",\r\n  \"historyOutputKB\": \"5120\",\r\n  \"collapsedGroups\": [\r\n    \"Examples\"\r\n  ],\r\n  \"safetyWarnings\": true,\r\n" +
        "  \"trusted\": [\r\n    \"tools\\\\clean.ps1|ABCDEF\"\r\n  ],\r\n  \"lastRuns\": [\r\n    \"usd_rub.ps1|ok|0|2026-10-10T14:05:00|3120|inline\"\r\n  ],\r\n" +
        "  \"engineDefault\": \"auto\",\r\n  \"engines\": [\r\n    \"usd_rub.ps1|5.1\",\r\n    \"bad line\"\r\n  ],\r\n  \"launcherHost\": \"7\",\r\n  \"lastVersion\": \"0.9.8\",\r\n" +
        "  \"runPresets\": [\r\n    \"{\\\"rel\\\":\\\"Examples\\\\\\\\Hello with parameters.ps1\\\",\\\"mode\\\":\\\"window\\\",\\\"values\\\":{\\\"Name\\\":\\\"Ann\\\",\\\"Loud\\\":true}}\"\r\n  ]\r\n}\r\n";

    [Fact]
    public void Reads_PowerShell_edition_file()
    {
        var s = new Settings();
        s.LoadJson(Ps098.TrimStart('\uFEFF'));
        Assert.Equal("ru", s.Language);
        Assert.Equal("dark", s.Theme);
        Assert.Equal("#8764B8", s.Accent);
        Assert.Equal(new[] { "System\\System summary.ps1", "usd_rub.ps1" }, s.Favorites);
        Assert.Equal("200", s.HistoryMax);
        Assert.Equal(200, s.HistoryMaxValue);
        Assert.Equal(5120 * 1024, s.HistoryOutputChars);
        Assert.Equal(new[] { "usd_rub.ps1|5.1" }, s.Engines);   // invalid line dropped
        Assert.Equal("5.1", s.GetEngineChoice("USD_RUB.ps1"));
        Assert.Equal("auto", s.GetEngineChoice("other.ps1"));
        Assert.False(s.Unknown.ContainsKey("launcherHost"));   // obsolete key: read, not kept
        Assert.True(s.Dirty);
        Assert.True(s.MigratedFromPs);
        var p = RunPreset.Get(s, "examples\\hello with parameters.ps1");
        Assert.NotNull(p);
        Assert.Equal("window", p!.Mode);
        Assert.Equal("Ann", p.Values["Name"]);
        Assert.Equal(true, p.Values["Loud"]);
        // new keys get their defaults
        Assert.True(s.ToastOnFinish);
        Assert.True(s.JumpList);
    }

    [Fact]
    public void Load_file_with_BOM_and_round_trip()
    {
        string dir = TestUtil.TempDir();
        string f = Path.Combine(dir, "launcher-settings.json");
        File.WriteAllText(f, Ps098);
        var s = Settings.Load(f);
        Assert.Equal("ru", s.Language);
        s.SetIcon("usd_rub.ps1", "assets\\script-icons\\usd.png");
        s.Save(f);
        var bytes = File.ReadAllBytes(f);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());   // UTF-8 with BOM like 0.9.x
        var t = Settings.Load(f);
        Assert.Equal(s.ToJson(), t.ToJson());
        Assert.Equal("assets\\script-icons\\usd.png", t.GetIcon("USD_RUB.PS1"));
        Assert.DoesNotContain("launcherHost", File.ReadAllText(f));   // obsolete PowerShell edition keys are dropped on save
        Assert.Contains("\"listView\": \"cards\"", File.ReadAllText(f));
    }

    [Fact]
    public void Obsolete_keys_are_dropped_and_unknown_keys_kept()
    {
        var s = new Settings();
        s.LoadJson("{\"detailsOpen\":true,\"historyThisScript\":true,\"migrationDeclined\":true,\"favTilesCollapsed\":true,\"detailsCollapsed\":true,\"launcherHost\":\"7\",\"perfLog\":true,\"listSort\":\"runcount\",\"listSortDesc\":true,\"listView\":\"compact\",\"favoritesBase\":\"Scripts\"}");
        string json = s.ToJson();
        foreach (var k in Settings.ObsoleteKeys) Assert.DoesNotContain("\"" + k + "\"", json);
        Assert.Contains("\"perfLog\": true", json);
        Assert.Equal("runcount", s.ListSort);
        Assert.True(s.ListSortDesc);
        Assert.Equal("compact", s.ListView);
        var t = new Settings();
        t.LoadJson("{\"listSort\":\"size\",\"listView\":\"tiles\"}");
        Assert.Equal("name", t.ListSort);
        Assert.Equal("cards", t.ListView);
    }

    [Fact]
    public void Invalid_values_fall_back_to_defaults()
    {
        var s = new Settings();
        s.LoadJson("{\"language\":\"de\",\"theme\":\"pink\",\"accent\":\"blue\",\"historyMax\":\"-5\",\"historyOutputKB\":\"999999\",\"outputHeight\":\"5\",\"engineDefault\":\"6\",\"historyFilter\":\"x\",\"favoritesBase\":\"Scripts\"}");
        Assert.Equal("en", s.Language);
        Assert.Equal("system", s.Theme);
        Assert.Equal("system", s.Accent);
        Assert.Equal("100", s.HistoryMax);
        Assert.Equal("102400", s.HistoryOutputKB);
        Assert.Equal("240", s.OutputHeight);
        Assert.Equal("auto", s.EngineDefault);
        Assert.Equal("all", s.HistoryFilter);
    }

    [Fact]
    public void Old_favorites_relative_to_Shelly_folder_are_migrated()
    {
        var s = new Settings();
        s.LoadJson("{\"favorites\":[\"Scripts\\\\a.ps1\",\".\\\\Scripts\\\\Sub\\\\b.ps1\",\"c.ps1\"]}");
        Assert.Equal(new[] { "a.ps1", "Sub\\b.ps1", "c.ps1" }, s.Favorites);
        Assert.Equal("Scripts", s.FavoritesBase);
        Assert.True(s.Dirty);
    }

    [Fact]
    public void Single_value_instead_of_array_and_unknown_keys()
    {
        var s = new Settings();
        s.LoadJson("{\"favorites\":\"one.ps1\",\"futureKey\":{\"a\":1},\"closeToTray\":\"False\",\"startMinimized\":1,\"favoritesBase\":\"Scripts\"}");
        Assert.Equal(new[] { "one.ps1" }, s.Favorites);
        Assert.False(s.CloseToTray);
        Assert.True(s.StartMinimized);
        Assert.Contains("\"futureKey\": {\"a\":1}", s.ToJson());
    }

    [Fact]
    public void Missing_or_broken_file_gives_defaults()
    {
        string dir = TestUtil.TempDir();
        Assert.Equal("en", Settings.Load(Path.Combine(dir, "none.json")).Language);
        File.WriteAllText(Path.Combine(dir, "bad.json"), "{ not json");
        Assert.Equal("Ctrl+Alt+L", Settings.Load(Path.Combine(dir, "bad.json")).Hotkey);
    }

    [Fact]
    public void Rename_moves_everything()
    {
        var s = new Settings();
        s.LoadJson(Ps098.TrimStart('\uFEFF'));
        s.SetIcon("usd_rub.ps1", "x.png");
        s.MoveScript("usd_rub.ps1", "Money\\usd.ps1");
        Assert.Contains("Money\\usd.ps1", s.Favorites);
        Assert.DoesNotContain("usd_rub.ps1", s.Favorites);
        Assert.Equal("5.1", s.GetEngineChoice("Money\\usd.ps1"));
        Assert.Equal("x.png", s.GetIcon("Money\\usd.ps1"));
        Assert.Null(s.GetIcon("usd_rub.ps1"));
        s.MoveScript("Money\\usd.ps1", null);
        Assert.DoesNotContain("Money\\usd.ps1", s.Favorites);
        Assert.Equal("auto", s.GetEngineChoice("Money\\usd.ps1"));
    }

    [Fact]
    public void Folder_rename_moves_entries_below_it()
    {
        var s = new Settings();
        s.LoadJson(Ps098.TrimStart('\uFEFF'));
        s.MoveFolder("Examples", "Demos");
        Assert.NotNull(RunPreset.Get(s, "Demos\\Hello with parameters.ps1"));
        Assert.Contains("Demos", s.CollapsedGroups);
        s.MoveFolder("System", "Sys");
        Assert.Contains("Sys\\System summary.ps1", s.Favorites);
    }

    [Fact]
    public void Run_presets_round_trip()
    {
        var s = new Settings();
        RunPreset.Set(s, "a.ps1", "admin", new Dictionary<string, object> { ["Path"] = "C:\\x \"y\"", ["Force"] = false });
        var p = RunPreset.Get(s, "A.PS1")!;
        Assert.Equal("admin", p.Mode);
        Assert.Equal("C:\\x \"y\"", p.Values["path"]);
        Assert.Equal(false, p.Values["Force"]);
        RunPreset.Set(s, "a.ps1", "bogus", new Dictionary<string, object>());
        Assert.Equal("inline", RunPreset.Get(s, "a.ps1")!.Mode);
        RunPreset.Remove(s, "a.ps1");
        Assert.Null(RunPreset.Get(s, "a.ps1"));
        Assert.Empty(s.RunPresets);
    }
}
