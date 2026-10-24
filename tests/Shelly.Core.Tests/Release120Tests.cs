using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.2.0: default run mode, notifications, schedules (Task Scheduler), the command line and parameter presets.</summary>
public class Release120Tests
{
    static readonly XNamespace T = TaskXml.Ns;
    const string Exe = @"C:\Tools\Shelly\Shelly.exe";
    const string Sid = "S-1-5-21-1-2-3-1001";
    static readonly DateTime Now = new(2026, 10, 22, 14, 37, 15);

    static Schedule Sched(string kind, Action<Schedule>? set = null)
    {
        var s = new Schedule { Id = "0a1b2c3d", Rel = @"Tools\Backup.ps1", Kind = kind, Time = "09:30" };
        set?.Invoke(s);
        return s;
    }

    static XElement Xml(Schedule s) => XDocument.Parse(TaskXml.Build(s, Exe, Sid, SchTasks.TaskName("1234abcd", s.Id), "desc", Now)).Root!;

    // ---------- Default run mode ----------

    [Theory]
    [InlineData(null, "list", null, null, "window", "window")]       // nothing chosen: the new default
    [InlineData(null, "list", null, null, "inline", "inline")]       // Settings > Default run mode
    [InlineData(null, "list", "inline", null, "window", "inline")]   // the remembered mode is kept
    [InlineData(null, "list", null, "inline", "window", "inline")]   // so is # Shelly-Mode:
    [InlineData(null, "list", "admin", "inline", "window", "admin")] // remembered before the header
    [InlineData(null, "jumplist", null, "window", "inline", "window")]
    [InlineData("inline", "list", "window", "window", "window", "inline")] // an explicit menu choice wins
    [InlineData(null, "tray", "inline", "inline", "window", "window")]     // remembered/header only for list and jump list
    [InlineData(null, "editor", null, "window", "window", "inline")]       // Save and Run stays inline
    [InlineData(null, "list", null, "bogus", "nonsense", "window")]        // invalid values fall back
    public void Mode_fallback(string? requested, string origin, string? remembered, string? header, string def, string expected) =>
        Assert.Equal(expected, RunModes.Resolve(requested, origin, remembered, header, def));

    // ---------- Notifications ----------

    [Fact]
    public void Notification_decision()
    {
        // level
        Assert.False(Notifications.Should("off", 0, "fail", 1, 99999, true, true, false));
        Assert.True(Notifications.Should("errors", 0, "fail", 1, 99999, false, true, false));
        Assert.False(Notifications.Should("errors", 0, "ok", 0, 99999, false, true, false));
        Assert.True(Notifications.Should("always", 0, "ok", 0, 99999, false, true, false));
        // "only if longer than"
        Assert.False(Notifications.Should("always", 20, "ok", 0, 19999, false, false, false));
        Assert.True(Notifications.Should("always", 20, "ok", 0, 20000, false, false, false));
        Assert.True(Notifications.Should("always", 0, "ok", 0, 1, false, false, false));
        // inline output in the active main window is on screen already; a window run is not
        Assert.False(Notifications.Should("always", 0, "ok", 0, 60000, false, true, true));
        Assert.True(Notifications.Should("always", 0, "ok", 0, 60000, false, false, true));
        // scheduled runs: the level alone (length and focus don't matter)
        Assert.True(Notifications.Should("always", 60, "ok", 0, 10, true, true, true));
        Assert.True(Notifications.Should("errors", 60, "fail", -1, 0, true, true, true));
        Assert.False(Notifications.Should("errors", 0, "ok", 0, 10, true, true, false));
        // stopped by the user, still running, unknown: never; a window closed with its X is not an error
        Assert.False(Notifications.Should("always", 0, "stopped", -1, 99999, false, true, false));
        Assert.False(Notifications.Should("always", 0, "started", 0, 99999, true, false, false));
        Assert.False(Notifications.Should("always", 0, "closed", Notifications.WindowClosedCode, 99999, false, false, false));
        Assert.False(Notifications.Should("always", 0, "closed", 0, 99999, false, false, false));
        Assert.True(Notifications.Should("errors", 0, "closed", 3, 99999, false, false, false));
        Assert.True(Notifications.IsFailure("interrupted", 0));
    }

    [Fact]
    public void Notification_texts_fit_a_balloon()
    {
        Loc.Language = "en";
        var e = new RunEntry { Name = new string('n', 100), Preset = "Night", Kind = "fail", Code = 2, Ms = 65000 };
        var (title, text, ok) = NotifyText.ForRun(e, true);
        Assert.False(ok);
        Assert.True(title.Length <= 63);
        Assert.EndsWith("…", title);
        Assert.Contains("2", text);
        Assert.StartsWith(Loc.T("notifScheduled"), text);
        var (t2, x2, ok2) = NotifyText.ForRun(new RunEntry { Name = "Backup", Kind = "ok", Ms = 1500 }, false);
        Assert.True(ok2);
        Assert.Equal(Loc.F("notifTitleOk", "Backup"), t2);
        Assert.DoesNotContain(Loc.T("notifScheduled"), x2);
        var (t3, x3, ok3) = NotifyText.ForBlocked("Backup", new string('r', 400));
        Assert.False(ok3);
        Assert.Contains("Backup", t3);
        Assert.Equal(255, x3.Length);
    }

    // ---------- Schedules: definitions ----------

    [Fact]
    public void Schedule_round_trips_and_rejects_junk()
    {
        var s = Sched("weekly", x => { x.Days = new() { "fri", "mon" }; x.Preset = "Full \"copy\""; x.Window = true; x.Enabled = false; });
        var p = Schedule.Parse(s.ToLine())!;
        Assert.Equal(new[] { "mon", "fri" }, p.Days);   // week order
        Assert.Equal("Full \"copy\"", p.Preset);
        Assert.True(p.Window);
        Assert.False(p.Enabled);
        Assert.Equal(p.ToLine(), Schedule.Parse(p.ToLine())!.ToLine());
        Assert.Null(Schedule.Parse("{\"id\":\"../x\",\"rel\":\"a.ps1\",\"kind\":\"daily\"}"));
        Assert.Null(Schedule.Parse("{\"id\":\"0a1b2c3d\",\"rel\":\"\",\"kind\":\"daily\"}"));
        Assert.Null(Schedule.Parse("{\"id\":\"0a1b2c3d\",\"rel\":\"a.ps1\",\"kind\":\"hourly\"}"));
        Assert.Null(Schedule.Parse("not json"));
        Assert.Matches("^[0-9a-f]{8}$", Schedule.NewId());
    }

    [Fact]
    public void Schedule_validation()
    {
        Assert.Null(Sched("daily").Error());
        Assert.Equal("schedErrTime", Sched("daily", x => x.Time = "25:00").Error());
        Assert.Equal("09:05", Schedule.NormalizeTime(" 9:05 "));
        Assert.Null(Schedule.NormalizeTime("9.05x"));
        Assert.Equal("schedErrDays", Sched("weekly").Error());
        Assert.Equal("schedErrDate", Sched("once").Error());
        Assert.Equal("schedErrPast", Sched("once", x => x.Date = "2026-10-22").Error(Now));            // 09:30 today has passed
        Assert.Null(Sched("once", x => x.Date = "2026-10-23").Error(Now));
        Assert.Equal("schedErrEvery", Sched("interval", x => x.Every = 0).Error());
        Assert.Equal("schedErrEvery", Sched("interval", x => x.Every = 1441).Error());
        Assert.Null(Sched("interval", x => x.Every = 15).Error());
        Assert.Null(Sched("logon").Error());
        Assert.Equal("schedErrScript", Sched("daily", x => x.Rel = "").Error());
    }

    [Fact]
    public void Next_run()
    {
        Assert.Equal(new DateTime(2026, 10, 23, 9, 30, 0), Sched("daily").NextRun(Now));
        Assert.Equal(new DateTime(2026, 10, 22, 18, 0, 0), Sched("daily", x => x.Time = "18:00").NextRun(Now));
        // Thursday 22.10.2026 14:37 → next Monday or Friday at 09:30 is Friday the 23rd
        Assert.Equal(new DateTime(2026, 10, 23, 9, 30, 0), Sched("weekly", x => x.Days = new() { "mon", "fri" }).NextRun(Now));
        Assert.Equal(new DateTime(2026, 10, 26, 9, 30, 0), Sched("weekly", x => x.Days = new() { "mon" }).NextRun(Now));
        Assert.Null(Sched("logon").NextRun(Now));
        Assert.Null(Sched("daily", x => x.Enabled = false).NextRun(Now));
        Assert.Null(Sched("once", x => x.Date = "2026-10-01").NextRun(Now));
        var iv = Sched("interval", x => { x.Every = 15; x.Date = "2026-10-22"; x.Time = "14:00"; });
        Assert.Equal(new DateTime(2026, 10, 22, 14, 45, 0), iv.NextRun(Now));
    }

    [Fact]
    public void Describe_in_both_languages()
    {
        Loc.Language = "en";
        Assert.Equal("Daily at 09:30", Sched("daily").Describe());
        Assert.Equal("Every 15 min", Sched("interval", x => x.Every = 15).Describe());
        Assert.Equal("Mon, Fri at 09:30", Sched("weekly", x => x.Days = new() { "mon", "fri" }).Describe());
        Loc.Language = "ru";
        Assert.Equal("Ежедневно в 09:30", Sched("daily").Describe());
        Assert.Equal("Каждые 15 мин", Sched("interval", x => x.Every = 15).Describe());
        Assert.Equal("При входе в Windows", Sched("logon").Describe());
        Loc.Language = "en";
    }

    [Fact]
    public void Schedule_list_follows_renames_and_presets()
    {
        var st = new Settings();
        ScheduleList.Put(st, Sched("daily", x => x.Preset = "Night"));
        ScheduleList.Put(st, Sched("logon", x => { x.Id = "11111111"; x.Rel = @"Tools\Other.ps1"; }));
        Assert.True(ScheduleList.Has(st, @"tools\backup.ps1"));
        ScheduleList.Put(st, Sched("daily", x => x.Time = "10:00"));   // same id: replaced
        Assert.Equal(2, ScheduleList.All(st).Count);
        Assert.Equal("10:00", ScheduleList.Get(st, "0a1b2c3d")!.Time);
        st.MoveScript(@"Tools\Backup.ps1", @"Tools\Backup2.ps1");
        Assert.Equal(@"Tools\Backup2.ps1", ScheduleList.Get(st, "0a1b2c3d")!.Rel);
        st.MoveFolder("Tools", "Admin");
        Assert.Equal(@"Admin\Backup2.ps1", ScheduleList.Get(st, "0a1b2c3d")!.Rel);
        Assert.Equal(@"Admin\Other.ps1", ScheduleList.Get(st, "11111111")!.Rel);
        Assert.True(ScheduleList.Remove(st, "11111111"));
        Assert.False(ScheduleList.Remove(st, "11111111"));
        Assert.Single(ScheduleList.All(st));
    }

    // ---------- Schedules: Task Scheduler ----------

    [Fact]
    public void Task_names_are_per_copy_and_parsed_back()
    {
        string a = SchTasks.InstanceId(@"C:\Tools\Shelly", "ann"), b = SchTasks.InstanceId(@"D:\Shelly", "ann");
        Assert.Matches("^[0-9a-f]{8}$", a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, SchTasks.InstanceId(@"c:\tools\shelly", "ANN"));
        Assert.Equal(@"\Shelly\" + a + "-0a1b2c3d", SchTasks.TaskName(a, "0a1b2c3d"));
        Assert.Equal("0a1b2c3d", SchTasks.IdOf(@"\Shelly\" + a + "-0a1b2c3d", a));
        Assert.Null(SchTasks.IdOf(@"\Shelly\" + b + "-0a1b2c3d", a));
        Assert.Null(SchTasks.IdOf(@"\Other\" + a + "-0a1b2c3d", a));
    }

    [Fact]
    public void Schtasks_arguments()
    {
        string tn = @"\Shelly\1234abcd-0a1b2c3d";
        Assert.Equal(new[] { "/Create", "/TN", tn, "/XML", @"C:\Temp\t.xml", "/F" }, SchTasks.CreateArgs(tn, @"C:\Temp\t.xml"));
        Assert.Equal(new[] { "/Delete", "/TN", tn, "/F" }, SchTasks.DeleteArgs(tn));
        Assert.Equal(new[] { "/Run", "/TN", tn }, SchTasks.RunArgs(tn));
        Assert.Equal(new[] { "/Query", "/FO", "CSV", "/V", "/NH" }, SchTasks.QueryArgs());
        Assert.Equal("--scheduled 0a1b2c3d", SchTasks.TaskArguments("0a1b2c3d"));
        // No /RU or /RL: never elevated, never another user
        Assert.DoesNotContain(SchTasks.CreateArgs(tn, "x"), a => a is "/RU" or "/RL" or "/RP");
    }

    [Fact]
    public void Task_xml_common_parts()
    {
        var x = Xml(Sched("daily"));
        Assert.Equal("1.2", (string?)x.Attribute("version"));
        var p = x.Element(T + "Principals")!.Element(T + "Principal")!;
        Assert.Equal(Sid, p.Element(T + "UserId")!.Value);
        Assert.Equal("InteractiveToken", p.Element(T + "LogonType")!.Value);
        Assert.Equal("LeastPrivilege", p.Element(T + "RunLevel")!.Value);
        var exec = x.Element(T + "Actions")!.Element(T + "Exec")!;
        Assert.Equal(Exe, exec.Element(T + "Command")!.Value);
        Assert.Equal("--scheduled 0a1b2c3d", exec.Element(T + "Arguments")!.Value);
        Assert.Equal(@"C:\Tools\Shelly", exec.Element(T + "WorkingDirectory")!.Value);
        var set = x.Element(T + "Settings")!;
        Assert.Equal("IgnoreNew", set.Element(T + "MultipleInstancesPolicy")!.Value);
        Assert.Equal("true", set.Element(T + "Enabled")!.Value);
        Assert.Equal("PT0S", set.Element(T + "ExecutionTimeLimit")!.Value);
        Assert.Equal("false", Xml(Sched("daily", s => s.Enabled = false)).Element(T + "Settings")!.Element(T + "Enabled")!.Value);
        Assert.Equal(@"\Shelly\1234abcd-0a1b2c3d", x.Element(T + "RegistrationInfo")!.Element(T + "URI")!.Value);
        Assert.Contains("utf-16", TaskXml.Build(Sched("daily"), Exe, Sid, "n", "d", Now));
    }

    [Fact]
    public void Task_xml_triggers()
    {
        XElement Trigger(Schedule s) => Xml(s).Element(T + "Triggers")!.Elements().Single();
        var once = Trigger(Sched("once", s => s.Date = "2026-12-31"));
        Assert.Equal("TimeTrigger", once.Name.LocalName);
        Assert.Equal("2026-12-31T09:30:00", once.Element(T + "StartBoundary")!.Value);
        Assert.Null(once.Element(T + "Repetition"));
        var daily = Trigger(Sched("daily"));
        Assert.Equal("CalendarTrigger", daily.Name.LocalName);
        Assert.Equal("2026-10-22T09:30:00", daily.Element(T + "StartBoundary")!.Value);
        Assert.Equal("1", daily.Element(T + "ScheduleByDay")!.Element(T + "DaysInterval")!.Value);
        var weekly = Trigger(Sched("weekly", s => s.Days = new() { "mon", "sun" }));
        var days = weekly.Element(T + "ScheduleByWeek")!.Element(T + "DaysOfWeek")!.Elements().Select(e => e.Name.LocalName);
        Assert.Equal(new[] { "Monday", "Sunday" }, days);
        var logon = Trigger(Sched("logon"));
        Assert.Equal("LogonTrigger", logon.Name.LocalName);
        Assert.Equal(Sid, logon.Element(T + "UserId")!.Value);   // this user's sign-in only
        var iv = Trigger(Sched("interval", s => { s.Every = 15; s.Time = ""; }));
        Assert.Equal("TimeTrigger", iv.Name.LocalName);
        Assert.Equal("PT15M", iv.Element(T + "Repetition")!.Element(T + "Interval")!.Value);
        Assert.Equal("2026-10-22T14:38:00", iv.Element(T + "StartBoundary")!.Value);   // the next whole minute
        Assert.Null(iv.Element(T + "Repetition")!.Element(T + "Duration"));            // indefinitely
    }

    [Fact]
    public void Task_xml_escapes_paths_and_descriptions()
    {
        string weird = @"C:\Users\Ann & <Bob>\Shelly ""x""\Shelly.exe";
        var x = XDocument.Parse(TaskXml.Build(Sched("daily"), weird, Sid, "n", "a <b> & \"c\"", Now)).Root!;
        Assert.Equal(weird, x.Element(T + "Actions")!.Element(T + "Exec")!.Element(T + "Command")!.Value);
        Assert.Equal("a <b> & \"c\"", x.Element(T + "RegistrationInfo")!.Element(T + "Description")!.Value);
    }

    [Fact]
    public void Query_output_and_reconcile()
    {
        string inst = "1234abcd";
        // schtasks /Query /FO CSV /V /NH: HostName, TaskName, Next Run Time, Status, Logon Mode, Last Run Time, Last Result, Author, Task To Run, …
        string Line(string name, string run) => "\"PC\",\"" + name + "\",\"N/A\",\"Ready\",\"Interactive only\",\"N/A\",\"0\",\"Shelly\",\"" + run.Replace("\"", "\"\"") + "\",\"N/A\"";
        string ok = "\"" + Exe + "\" --scheduled 0a1b2c3d";
        string output = string.Join("\r\n",
            "",
            Line(@"\Shelly\1234abcd-0a1b2c3d", ok),
            Line(@"\Shelly\1234abcd-0a1b2c3d", ok),   // listed once per trigger
            Line(@"\Shelly\1234abcd-22222222", "\"D:\\Old\\Shelly.exe\" --scheduled 22222222"),
            Line(@"\Shelly\1234abcd-99999999", ok),   // no schedule: leftover
            Line(@"\Shelly\ffffffff-0a1b2c3d", ok),   // another copy: not ours
            Line(@"\Microsoft\Windows\Defrag\ScheduledDefrag", "%windir%\\system32\\defrag.exe -c"));
        var tasks = SchTasks.ParseQuery(output);
        Assert.Equal(5, tasks.Count);
        Assert.Equal(ok, tasks[0].Run);
        var schedules = new[] { Sched("daily"), Sched("daily", s => s.Id = "22222222"), Sched("logon", s => s.Id = "33333333") };
        var r = SchTasks.Reconcile(schedules, tasks, inst, Exe);
        Assert.Equal("ok", r.State["0a1b2c3d"]);
        Assert.Equal("outdated", r.State["22222222"]);
        Assert.Equal("missing", r.State["33333333"]);
        Assert.Equal(new[] { @"\Shelly\1234abcd-99999999" }, r.Orphans);
        Assert.Equal(2, r.Missing);
        // A path outside ASCII may come back in another code page: not compared, never "outdated" by mistake
        var r2 = SchTasks.Reconcile(schedules, tasks, inst, @"C:\Программы\Shelly.exe");
        Assert.Equal("ok", r2.State["22222222"]);
        Assert.Equal(new[] { "a", "b,c", "d\"e", "" }, SchTasks.CsvFields("\"a\",\"b,c\",\"d\"\"e\",\"\""));
    }

    // ---------- Scheduled runs: the gate ----------

    [Fact]
    public void Scheduled_runs_never_ask_they_skip()
    {
        var ok = new ScheduleGate.Input();
        Assert.Null(ScheduleGate.Block(ok));
        Assert.Equal("schedBlockScript", ScheduleGate.Block(ok with { ScriptFound = false }));
        Assert.Equal("schedBlockDisclaimer", ScheduleGate.Block(ok with { DisclaimerAccepted = false }));
        Assert.Equal("schedBlockAdmin", ScheduleGate.Block(ok with { ExplicitMode = "admin" }));
        Assert.Equal("schedBlockAdmin", ScheduleGate.Block(ok with { RequiresElevation = true }));
        Assert.Null(ScheduleGate.Block(ok with { ExplicitMode = "inline" }));
        Assert.Equal("schedBlockMotw", ScheduleGate.Block(ok with { Motw = true }));
        Assert.Equal("schedBlockUnverified", ScheduleGate.Block(ok with { Scan = "unverified" }));
        Assert.Equal("schedBlockUnverified", ScheduleGate.Block(ok with { Scan = "checking" }));
        Assert.Null(ScheduleGate.Block(ok with { Scan = "unverified", SafetyWarnings = false }));
        Assert.Null(ScheduleGate.Block(ok with { Scan = "none" }));   // Python
        Assert.Equal("schedBlockConfirm", ScheduleGate.Block(ok with { NeedsConfirmation = true }));
        Assert.Equal("schedBlockPreset", ScheduleGate.Block(ok with { PresetMissing = true }));
        Assert.Equal("schedBlockParams", ScheduleGate.Block(ok with { MissingMandatory = new[] { "Path" } }));
        // The most fundamental reason first
        Assert.Equal("schedBlockAdmin", ScheduleGate.Block(ok with { RequiresElevation = true, Motw = true, NeedsConfirmation = true }));
        Assert.False(ScheduleGate.CanSchedule("admin", false));
        Assert.False(ScheduleGate.CanSchedule(null, true));
        Assert.True(ScheduleGate.CanSchedule("window", false));
    }

    [Fact]
    public void A_changed_script_needs_trust_again_before_a_scheduled_run()
    {
        // The gate gets Safety.Needed for the hash read just before the run: trusting one version does not cover the next
        var st = new Settings { SafetyWarnings = true };
        var findings = new List<DangerFinding> { new() { Rule = "Download", Sev = "medium", Line = 1 } };
        Safety.AddTrust(st, "a.ps1", Safety.TrustKeyOf("a.ps1", new string('a', 64)));
        Assert.False(Safety.Needed(st, findings, "inline", "a.ps1", "", new string('a', 64)));
        Assert.True(Safety.Needed(st, findings, "inline", "a.ps1", "", new string('b', 64)));
        Assert.True(Safety.Needed(st, new List<DangerFinding>(), "admin", "a.ps1", "", new string('a', 64)));
    }

    // ---------- Command line ----------

    [Fact]
    public void Cli_parsing()
    {
        var c = Cli.Parse(new[] { "--run", @"Tools/Backup.ps1", "--preset", "  Night  run ", "--headless" });
        Assert.Equal(@"Tools\Backup.ps1", c.Run);
        Assert.Equal("Night run", c.Preset);
        Assert.True(c.Headless);
        Assert.True(c.IsUnattended);
        Assert.False(Cli.Parse(new[] { "/run", "a.ps1" }).IsUnattended);   // --run alone: like a click in the list
        Assert.Equal("a.ps1", Cli.Parse(new[] { "-RUN", "a.ps1" }).Run);
        var s = Cli.Parse(new[] { "--scheduled", "0A1B2C3D" });
        Assert.Equal("0a1b2c3d", s.Scheduled);
        Assert.True(s.IsUnattended);
        Assert.Null(Cli.Parse(new[] { "--scheduled", "../../x" }).Scheduled);
        Assert.Null(Cli.Parse(new[] { "--scheduled" }).Scheduled);
        Assert.Null(Cli.Parse(new[] { "--run" }).Run);
        Assert.Null(Cli.Parse(new[] { "--run", "a\tb" }).Run);
        Assert.Equal("20261022-143715-123-ab12", Cli.Parse(new[] { "--history", "20261022-143715-123-ab12" }).History);
        Assert.Null(Cli.Parse(new[] { "--history", "..\\x" }).History);
        var m = Cli.Parse(new[] { "--minimized", "--restarted", "--perf" });
        Assert.True(m.Minimized && m.Restarted && m.Perf);
        Assert.True(Cli.Parse(new[] { "--tray" }).Minimized);
        Assert.False(Cli.Parse(Array.Empty<string>()).IsUnattended);
    }

    [Fact]
    public void Cli_messages_for_the_running_instance()
    {
        Assert.Equal("sched\t0a1b2c3d", Cli.Parse(new[] { "--scheduled", "0a1b2c3d" }).ToMessage());
        Assert.Equal("run\ta.ps1", Cli.Parse(new[] { "--run", "a.ps1" }).ToMessage());   // the 1.1 jump list message
        Assert.Equal("run\ta.ps1\tNight", Cli.Parse(new[] { "--run", "a.ps1", "--preset", "Night" }).ToMessage());
        Assert.Equal("brun\ta.ps1", Cli.Parse(new[] { "--run", "a.ps1", "--headless" }).ToMessage());
        Assert.Equal("history\tabc", Cli.Parse(new[] { "--history", "abc" }).ToMessage());
        Assert.Equal("show", Cli.Parse(new[] { "--minimized" }).ToMessage());
        foreach (var args in new[] { new[] { "--scheduled", "0a1b2c3d" }, new[] { "--run", @"x\a.ps1", "--preset", "P 1" }, new[] { "--run", "a.ps1", "--headless" }, new[] { "--history", "abc" } })
        {
            var a = Cli.Parse(args);
            var b = Cli.FromMessage(a.ToMessage());
            Assert.Equal((a.Run, a.Preset, a.Headless, a.Scheduled, a.History), (b.Run, b.Preset, b.Headless, b.Scheduled, b.History));
        }
        var none = Cli.FromMessage("garbage");
        Assert.Null(none.Run);
        Assert.Null(none.Scheduled);
    }

    // ---------- Parameter presets ----------

    static List<ParamInfo> Params() => new()
    {
        new() { Name = "Path", Mandatory = true },
        new() { Name = "Days", Default = "7" },
        new() { Name = "Force", Kind = "switch" }
    };

    [Fact]
    public void Presets_save_replace_delete()
    {
        var st = new Settings();
        Assert.True(Presets.Save(st, "a.ps1", "  Night\trun  ", new Dictionary<string, object> { ["Path"] = @"D:\Backup", ["Force"] = true }));
        Assert.False(Presets.Save(st, "a.ps1", "   ", new Dictionary<string, object>()));
        Presets.Save(st, "a.ps1", "Day", new Dictionary<string, object> { ["Path"] = "C:\\" });
        Presets.Save(st, "b.ps1", "Night run", new Dictionary<string, object>());
        Assert.Equal(new[] { "Day", "Night run" }, Presets.Names(st, "A.PS1"));
        Presets.Save(st, "a.ps1", "NIGHT RUN", new Dictionary<string, object> { ["Path"] = "E:\\" });   // same name: replaced
        Assert.Equal(2, Presets.For(st, "a.ps1").Count);
        Assert.Equal("E:\\", Presets.Get(st, "a.ps1", "night run")!.Values["Path"]);
        Assert.True(Presets.Delete(st, "a.ps1", "day"));
        Assert.False(Presets.Delete(st, "a.ps1", "day"));
        Assert.Single(Presets.For(st, "a.ps1"));
        Assert.Single(Presets.For(st, "b.ps1"));
        Assert.Equal(new string('x', ParamPreset.MaxName), Presets.CleanName(new string('x', 100)));
        Assert.Null(Presets.CleanName("\r\n"));
    }

    [Fact]
    public void Presets_survive_renames_and_go_with_the_script()
    {
        var st = new Settings();
        Presets.Save(st, @"Tools\a.ps1", "P", new Dictionary<string, object> { ["Path"] = "x" });
        st.MoveScript(@"Tools\a.ps1", @"Tools\b.ps1");
        Assert.True(Presets.Exists(st, @"Tools\b.ps1", "P"));
        st.MoveFolder("Tools", "Admin");
        Assert.True(Presets.Exists(st, @"Admin\b.ps1", "P"));
        st.MoveScript(@"Admin\b.ps1", null);
        Assert.Empty(st.ParamPresets);
    }

    [Fact]
    public void Preset_apply_skips_parameters_that_are_gone_and_merges_over_defaults()
    {
        var preset = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["path"] = @"D:\B", ["Force"] = true, ["Old"] = "1", ["Zeta"] = "2" };
        var (values, ignored) = Presets.Apply(Params(), false, preset);
        Assert.Equal(@"D:\B", values["Path"]);   // the script's spelling
        Assert.True(values.ContainsKey("Path"));
        Assert.Equal(true, values["Force"]);
        Assert.False(values.ContainsKey("Days"));   // not in the preset: the script's default
        Assert.Equal(new[] { "Old", "Zeta" }, ignored);
        Assert.Equal(" -Path 'D:\\B' -Force", RunArgs.Format(Params(), values));
        var merged = Presets.Merge(new Dictionary<string, object> { ["Days"] = "3", ["Path"] = "typed" }, values);
        Assert.Equal("3", merged["Days"]);
        Assert.Equal(@"D:\B", merged["Path"]);
        // Python: the arguments line only
        var (py, pyIgnored) = Presets.Apply(Array.Empty<ParamInfo>(), true, new Dictionary<string, object> { [PythonRun.ArgsKey] = "--fast 2", ["Path"] = "x" });
        Assert.Equal("--fast 2", PythonRun.ArgsFrom(py));
        Assert.Equal(new[] { "Path" }, pyIgnored);
    }

    [Fact]
    public void Preset_lines_round_trip()
    {
        var p = new ParamPreset { Rel = @"a\b.ps1", Name = "Ночь \"2\"" };
        p.Values["Path"] = @"C:\x 'y'";
        p.Values["Force"] = true;
        p.Values["N"] = "5";
        var q = ParamPreset.Parse(p.ToLine())!;
        Assert.Equal(p.Rel, q.Rel);
        Assert.Equal(p.Name, q.Name);
        Assert.Equal(@"C:\x 'y'", q.Values["path"]);
        Assert.Equal(true, q.Values["Force"]);
        Assert.Equal(p.ToLine(), q.ToLine());
        using var _ = JsonDocument.Parse(p.ToLine());
        Assert.Null(ParamPreset.Parse("{\"rel\":\"a.ps1\",\"name\":\" \"}"));
        Assert.Null(ParamPreset.Parse("{\"name\":\"x\"}"));
    }

    // ---------- Settings migration ----------

    [Fact]
    public void Old_settings_load_with_the_new_defaults()
    {
        string dir = TestUtil.TempDir(), f = Path.Combine(dir, "launcher-settings.json");
        // A 1.1.2 file: no defaultMode / notify / presets / schedules
        File.WriteAllText(f, "{\"language\":\"ru\",\"toastOnFinish\":true,\"toastAfterSec\":\"5\",\"favorites\":[\"a.ps1\"]}");
        var s = Settings.Load(f);
        Assert.Equal("window", s.DefaultMode);
        Assert.Equal("always", s.Notify);
        Assert.Equal(5, s.ToastAfterSeconds);   // an old choice is kept
        Assert.Empty(s.ParamPresets);
        Assert.Empty(s.Schedules);
        File.WriteAllText(f, "{\"toastOnFinish\":false}");
        Assert.Equal("off", Settings.Load(f).Notify);   // the old switch was off
        // Invalid values fall back, invalid lines are dropped
        File.WriteAllText(f, "{\"defaultMode\":\"admin\",\"notify\":\"loud\",\"schedules\":[\"junk\"," +
            JsonSerializer.Serialize(Sched("daily").ToLine()) + "],\"paramPresets\":[\"{}\"]}");
        s = Settings.Load(f);
        Assert.Equal("window", s.DefaultMode);
        Assert.Equal("always", s.Notify);
        Assert.Single(s.Schedules);
        Assert.Empty(s.ParamPresets);
    }

    [Fact]
    public void New_settings_round_trip_and_keep_the_old_switch_in_step()
    {
        string dir = TestUtil.TempDir(), f = Path.Combine(dir, "launcher-settings.json");
        var s = new Settings { DefaultMode = "inline", Notify = "errors", ToastAfterSec = "0" };
        Presets.Save(s, "a.ps1", "P", new Dictionary<string, object> { ["X"] = "1" });
        ScheduleList.Put(s, Sched("weekly", x => x.Days = new() { "tue" }));
        s.Save(f);
        var t = Settings.Load(f);
        Assert.Equal("inline", t.DefaultMode);
        Assert.Equal("errors", t.Notify);
        Assert.Equal(0, t.ToastAfterSeconds);
        Assert.Equal("errors", t.Notify);   // "notify" wins over the 1.1 key written next to it
        Assert.Equal(s.ParamPresets, t.ParamPresets);
        Assert.Equal(s.Schedules, t.Schedules);
        s.Notify = "off";
        s.Save(f);
        using var doc = JsonDocument.Parse(File.ReadAllText(f).TrimStart('\uFEFF'));
        Assert.False(doc.RootElement.GetProperty("toastOnFinish").GetBoolean());   // 1.1.x reading this file gets no toasts either
        Assert.Contains(0, Settings.ToastAfterChoices);
    }

    // ---------- History written by two processes ----------

    [Fact]
    public void History_merges_runs_written_by_a_scheduled_process()
    {
        string dir = TestUtil.TempDir();
        var gui = new HistoryStore(dir) { Max = 50 };
        gui.Load();
        gui.Add(new RunEntry { Id = HistoryStore.NewId(), Rel = "a.ps1", Name = "a", Kind = "ok", Start = new DateTime(2026, 10, 22, 10, 0, 0) });
        var removedLater = new RunEntry { Id = "20261022-100500-000-dead", Rel = "c.ps1", Name = "c", Kind = "ok", Start = new DateTime(2026, 10, 22, 10, 5, 0) };
        gui.Add(removedLater);
        // The scheduled process loads, runs, saves
        var headless = new HistoryStore(dir) { Max = 50 };
        headless.Load();
        var sched = new RunEntry { Id = HistoryStore.NewId(), Rel = "b.ps1", Name = "b", Kind = "fail", Code = 3, Origin = "schedule", Preset = "Night", Start = new DateTime(2026, 10, 22, 11, 0, 0) };
        headless.Add(sched);
        // Meanwhile the GUI removed one run and adds another: neither the scheduled run is lost nor the removed one back
        Assert.True(gui.Remove(removedLater));
        gui.Add(new RunEntry { Id = HistoryStore.NewId(), Rel = "a.ps1", Name = "a", Kind = "ok", Start = new DateTime(2026, 10, 22, 12, 0, 0) });
        var check = new HistoryStore(dir);
        check.Load();
        Assert.Equal(3, check.Entries.Count);
        var b = check.Entries.Single(e => e.Rel == "b.ps1");
        Assert.Equal("schedule", b.Origin);
        Assert.Equal("Night", b.Preset);
        Assert.DoesNotContain(check.Entries, e => e.Id == removedLater.Id);
        Assert.Equal(check.Entries.OrderByDescending(e => e.Start).Select(e => e.Id), check.Entries.Select(e => e.Id));
        Assert.Equal(0, gui.MergeFromDisk());   // nothing new any more
    }

    // ---------- Window runs: the end marker ----------

    [Fact]
    public void Window_runs_write_an_end_marker()
    {
        string done = @"C:\Shelly\cache\runs\20261022-1.exit";
        string a = RunArgs.WindowArgs(@"C:\S\a b.ps1", " -Name 'x'", null, done);
        Assert.Contains("-NoExit", a);
        Assert.Contains("-Command", a);
        Assert.Contains("finally", a);
        Assert.Contains("20261022-1.exit", a);
        Assert.Contains("$LASTEXITCODE", a);
        Assert.Contains("-Name 'x'", a);
        // The administrator variant keeps the hash check and reports it as a failure
        string h = new string('A', 64);
        string adm = RunArgs.WindowArgs(@"C:\S\a.ps1", "", h, done);
        Assert.Contains(h, adm);
        Assert.Contains("$sc=1", adm);
        var py = new PythonInfo { Path = @"C:\Py\python.exe", Version = "3.12.1" };
        string p = PythonRun.WindowArgs(py, @"C:\S\t.py", "--x 1", null, done);
        Assert.Contains("finally", p);
        Assert.Contains("$sc=$LASTEXITCODE", p);
    }

    [Fact]
    public void End_marker_is_read_only_when_complete()
    {
        string dir = TestUtil.TempDir(), f = Path.Combine(dir, "x.exit");
        Assert.Null(RunArgs.ReadDone(f));
        File.WriteAllText(f, "");
        Assert.Null(RunArgs.ReadDone(f));
        File.WriteAllText(f, "-1073741510");
        Assert.Equal(-1073741510, RunArgs.ReadDone(f));
        File.WriteAllText(f, "3\r\n");
        Assert.Equal(3, RunArgs.ReadDone(f));
    }

    /// <summary>Runs the window command line the way the console window would, minus -NoExit, and returns the end marker.</summary>
    static int? RunWindowCommand(string pwsh, string args, string done)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(pwsh, args.Replace("-NoExit ", "")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(60000));
        return RunArgs.ReadDone(done);
    }

    [PwshFact]
    public void End_marker_has_the_exit_code_of_the_script()
    {
        string pwsh = TestUtil.RequirePwsh();
        string dir = TestUtil.TempDir();
        string script = Path.Combine(dir, "it's a test.ps1");
        File.WriteAllText(script, "param([int]$Code = 0, [switch]$Throw)\nif ($Throw) { throw 'boom' }\nWrite-Output 'hello'\nexit $Code\n");
        int n = 0;
        int? Run(string argText, string? hash = null)
        {
            string done = Path.Combine(dir, "m" + n++ + ".exit");
            return RunWindowCommand(pwsh, RunArgs.WindowArgs(script, argText, hash, done), done);
        }
        Assert.Equal(0, Run(""));
        Assert.Equal(7, Run(" -Code 7"));
        Assert.Equal(1, Run(" -Throw"));
        // The hash check of administrator runs: another hash means the script does not run, and that is a failure
        Assert.Equal(1, Run("", new string('0', 64)));
        string real = Safety.FileSha256(script).ToUpperInvariant();
        Assert.Equal(0, Run("", real));
    }

    // ---------- Strings ----------

    [Fact]
    public void New_strings_exist_in_both_languages()
    {
        string[] keys =
        {
            "setDefaultMode", "modeDefWindow", "modeDefInline", "ctxRunInline", "setNotify", "notifyOff", "notifyErrors", "notifyAlways",
            "toastAnyLength", "notifTitleOk", "notifTitleFail", "notifText", "notifScheduled", "notifBlockedTitle", "originSchedule",
            "secSchedules", "schedMenu", "schedMenuAdd", "schedKindOnce", "schedKindDaily", "schedKindWeekly", "schedKindLogon",
            "schedKindInterval", "schedAdminNo", "schedRunNow", "schedOrphans", "schedBlockConfirm", "schedBlockedOut", "schedQueryFail",
            "schedBlockPython", "schedBlockArgs", "schedStarted", "presetLabel", "presetSaveAs", "presetRunMenu", "presetIgnored", "presetTip",
            "presetGone", "schedDateHint", "setToastAfter", "setToastAfterNote"
        };
        foreach (var lang in new[] { "en", "ru" })
        {
            Loc.Language = lang;
            foreach (var k in keys) Assert.False(Loc.T(k) == k || Loc.T(k).Length == 0, lang + ": " + k);
        }
        Loc.Language = "ru";
        Assert.Equal("Только если дольше", Loc.T("setToastAfter"));
        Assert.Equal("Режим запуска по умолчанию", Loc.T("setDefaultMode"));
        Loc.Language = "en";
        // The scheduled-run reasons fill in the script name ({0}) and the detail ({1})
        Assert.Contains("Backup", Loc.F("schedBlockConfirm", "Backup", ""));
        Assert.Contains("Path", Loc.F("schedBlockParams", "Backup", "Path"));
    }
}
