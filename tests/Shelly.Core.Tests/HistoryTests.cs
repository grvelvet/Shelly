using Shelly.Core;
using Xunit;

namespace Shelly.Core.Tests;

public class HistoryTests
{
    // history.json as written by Shelly 0.9.8
    const string Ps098 = "\uFEFF[\r\n" +
        "  {\"id\":\"20261010-140500-123-ab12\",\"rel\":\"usd_rub.ps1\",\"name\":\"usd_rub\",\"engine\":\"5.1\",\"args\":\"\",\"mode\":\"inline\",\"origin\":\"list\",\"start\":\"2026-10-10T14:05:00.123\",\"ms\":3120,\"code\":0,\"kind\":\"ok\",\"truncated\":false,\"output\":true},\r\n" +
        "  {\"id\":\"20261010-150000-000-cd34\",\"rel\":\"Examples\\\\Hello with parameters.ps1\",\"name\":\"Hello with parameters\",\"engine\":\"7\",\"args\":\" -Name 'Ann'\",\"mode\":\"inline\",\"origin\":\"rerun\",\"start\":\"2026-10-10T15:00:00.000\",\"ms\":0,\"code\":0,\"kind\":\"running\",\"truncated\":false,\"output\":false},\r\n" +
        "  {\"id\":\"20261010-160000-000-ef56\",\"rel\":\"x.ps1\",\"name\":\"x\",\"engine\":\"5.1\",\"args\":\"\",\"mode\":\"window\",\"origin\":\"tray\",\"start\":\"2026-10-10T16:00:00.000\",\"ms\":0,\"code\":0,\"kind\":\"running\",\"truncated\":false,\"output\":false},\r\n" +
        "  {\"id\":\"../evil\",\"rel\":\"y.ps1\",\"kind\":\"ok\",\"start\":\"2026-10-10T16:00:00.000\"},\r\n" +
        "  {\"id\":\"20261010-170000-000-0000\",\"rel\":\"z.ps1\",\"kind\":\"weird\",\"start\":\"2026-10-10T16:00:00.000\"}\r\n]\r\n";

    [Fact]
    public void Reads_PowerShell_edition_history()
    {
        string dir = TestUtil.TempDir();
        File.WriteAllText(Path.Combine(dir, "history.json"), Ps098);
        File.WriteAllText(Path.Combine(dir, "20261010-140500-123-ab12.log"), "hello");
        File.WriteAllText(Path.Combine(dir, "orphan.log"), "x");
        var h = new HistoryStore(dir);
        h.Load();
        Assert.Equal(3, h.Entries.Count);                        // bad id and unknown kind are dropped
        Assert.Equal("20261010-160000-000-ef56", h.Entries[0].Id);  // newest first
        Assert.Equal("started", h.Entries[0].Kind);              // window run that was open when Shelly quit
        Assert.Equal("interrupted", h.Entries[1].Kind);          // inline run cut off
        Assert.Equal("rerun", h.Entries[1].Origin);
        Assert.Equal(" -Name 'Ann'", h.Entries[1].Args);
        Assert.Equal("hello", h.ReadOutput(h.Entries[2]));
        Assert.False(File.Exists(Path.Combine(dir, "orphan.log")));
        // the rewritten file loads again the same way
        var h2 = new HistoryStore(dir);
        h2.Load();
        Assert.Equal(h.Entries.Select(HistoryStore.ToJsonLine), h2.Entries.Select(HistoryStore.ToJsonLine));
    }

    [Fact]
    public void Limit_keeps_running_entries()
    {
        string dir = TestUtil.TempDir();
        var h = new HistoryStore(dir) { Max = 2 };
        var running = new RunEntry { Id = HistoryStore.NewId(), Rel = "r.ps1", Name = "r", Start = DateTime.Now.AddMinutes(-10), Kind = "running" };
        h.Add(running);
        for (int i = 0; i < 4; i++)
        {
            var e = new RunEntry { Id = "id" + i, Rel = "a.ps1", Name = "a", Start = DateTime.Now.AddMinutes(i), Kind = "ok" };
            h.SaveOutput(e, "out" + i);
            h.Add(e);
        }
        Assert.Equal(3, h.Entries.Count);
        Assert.Contains(running, h.Entries);
        Assert.False(File.Exists(h.OutputPath("id0")));
        Assert.True(File.Exists(h.OutputPath("id3")));
        Assert.False(h.Remove(running));
        Assert.Equal(2, h.Clear());
        Assert.Single(h.Entries);
    }

    [Fact]
    public void Filters_and_rename()
    {
        var a = new RunEntry { Id = "1", Rel = "A\\a.ps1", Name = "a", Kind = "ok", Mode = "inline", Args = " -Name 'Zed'" };
        var b = new RunEntry { Id = "2", Rel = "b.ps1", Name = "b", Kind = "fail", Code = 2, Mode = "window" };
        Assert.True(HistoryStore.Matches(a, "ok", null, null));
        Assert.False(HistoryStore.Matches(b, "ok", null, null));
        Assert.True(HistoryStore.Matches(b, "window", null, null));
        Assert.True(HistoryStore.Matches(b, "fail", "B.ps1", null));
        Assert.True(HistoryStore.Matches(a, "all", null, "zed"));
        Assert.False(HistoryStore.Matches(a, "all", null, "nope"));
        var h = new HistoryStore(TestUtil.TempDir());
        h.Entries.Add(a); h.Entries.Add(b);
        Assert.Equal(1, h.MoveRel("A\\", "Renamed\\"));
        Assert.Equal("Renamed\\a.ps1", a.Rel);
        Assert.Equal(1, h.MoveRel("b.ps1", "c.ps1"));
        Assert.Equal("c", b.Name);
    }

    [Fact]
    public void Run_log_keeps_head_and_tail()
    {
        var log = new RunLog { MaxChars = 100, MaxLines = 5, TailLines = 3 };
        for (int i = 0; i < 20; i++) log.Add("line " + i);
        string t = log.GetText("... {0} skipped");
        Assert.StartsWith("line 0\r\n", t);
        Assert.Contains("... 12 skipped", t);
        Assert.EndsWith("line 19\r\n", t);
        Assert.True(log.Truncated);
        Assert.Equal(20, log.TotalLines);
    }

    [Fact]
    public void Last_runs()
    {
        var s = new Settings();
        var map = LastRun.Import(s);
        LastRun.Set(s, map, new LastRun { Rel = "a.ps1", Kind = "fail", Code = 3, Time = new DateTime(2026, 10, 10, 14, 5, 0), Ms = 1500, Mode = "inline" });
        Assert.Equal("a.ps1|fail|3|2026-10-10T14:05:00|1500|inline", s.LastRuns.Single());
        var back = LastRun.Import(s)["A.PS1"];
        Assert.Equal(3, back.Code);
        Assert.Null(LastRun.Parse("a.ps1|weird|0|2026-10-10T14:05:00|0|inline"));
        Assert.Null(LastRun.Parse("a.ps1|ok|0|yesterday|0|inline"));
    }

    [Fact]
    public void Friendly_results()
    {
        Loc.Language = "en";
        Assert.Equal("Success", RunResult.Format("ok", 0).Text);
        Assert.Equal("Failed (code 2)", RunResult.Format("fail", 2).Text);
        Assert.Equal("Window closed", RunResult.Format("fail", 0xC000013A).Text);
        Assert.Equal("Window closed", RunResult.Format("fail", -1073741510).Text);
        Assert.Contains("stack overflow", RunResult.Format("fail", -1073741571).Text);
        Assert.Equal("Failed (code 0x80004005)", RunResult.Format("fail", 0x80004005).Text);
        Assert.Contains("0xC000013A", RunResult.Format("fail", -1073741510).Tip);
        Assert.Equal("0:07", RunResult.FormatElapsed(7.9));
        Assert.Equal("1:02:03", RunResult.FormatElapsed(3723));
        Assert.Equal("stopped", RunResult.KindFor(1, true));
        Assert.Equal("fail", RunResult.KindFor(1, false));
    }
}
