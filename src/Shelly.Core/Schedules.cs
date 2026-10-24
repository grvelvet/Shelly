using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Shelly.Core;

/// <summary>A script run on a schedule (1.2.0). The definitions live in launcher-settings.json ("schedules", one compact
/// JSON line each) and are the source of truth; Windows Task Scheduler holds one task per schedule (\Shelly\…) that starts
/// "Shelly.exe --scheduled &lt;id&gt;". The task refers to the schedule by id, so renaming the script or the preset does not
/// touch the task.</summary>
public sealed class Schedule
{
    public static readonly string[] Kinds = { "once", "daily", "weekly", "logon", "interval" };
    /// <summary>Monday first, as in the Russian and ISO week.</summary>
    public static readonly string[] DayKeys = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
    public const int MinEvery = 1, MaxEvery = 1440;

    public string Id = "";
    /// <summary>The script (Scripts-relative path or ext:… key, like everywhere else in the settings).</summary>
    public string Rel = "";
    /// <summary>Name of a parameter preset of the script; "" = no preset (the remembered values, or the script's defaults).</summary>
    public string Preset = "";
    /// <summary>once | daily | weekly | logon | interval</summary>
    public string Kind = "daily";
    /// <summary>yyyy-MM-dd: the day of a one-time run; for "every N minutes" the first run (with <see cref="Time"/>).</summary>
    public string Date = "";
    /// <summary>HH:mm (24 h).</summary>
    public string Time = "09:00";
    /// <summary>Weekly: mon … sun.</summary>
    public List<string> Days = new();
    /// <summary>Interval: minutes between runs.</summary>
    public int Every = 60;
    public bool Enabled = true;
    /// <summary>Run in its own console window instead of hidden with the output kept in the history.</summary>
    public bool Window;

    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    public Schedule Clone()
    {
        var c = (Schedule)MemberwiseClone();
        c.Days = new List<string>(Days);
        return c;
    }

    public static Schedule? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var s = new Schedule { Id = S("id"), Rel = S("rel"), Preset = S("preset"), Kind = S("kind"), Date = S("date"), Time = S("time") };
            if (!Regex.IsMatch(s.Id, "^[0-9a-f]{8}$") || s.Rel.Length == 0 || !Kinds.Contains(s.Kind)) return null;
            if (r.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Array)
                foreach (var x in d.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String && DayKeys.Contains(x.GetString()) && !s.Days.Contains(x.GetString()!)) s.Days.Add(x.GetString()!);
            s.Days = DayKeys.Where(s.Days.Contains).ToList();
            if (r.TryGetProperty("every", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int n)) s.Every = n;
            s.Enabled = !r.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
            s.Window = r.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.True;
            return s;
        }
        catch { return null; }
    }

    public string ToLine()
    {
        string Q(string v) => Settings.JsonString(v);
        return "{\"id\":" + Q(Id) + ",\"rel\":" + Q(Rel) + ",\"preset\":" + Q(Preset) + ",\"kind\":" + Q(Kind) + ",\"date\":" + Q(Date) +
               ",\"time\":" + Q(Time) + ",\"days\":[" + string.Join(",", Days.Select(Q)) + "],\"every\":" + Every.ToString(CultureInfo.InvariantCulture) +
               ",\"enabled\":" + (Enabled ? "true" : "false") + ",\"window\":" + (Window ? "true" : "false") + "}";
    }

    public static bool TryTime(string? t, out TimeSpan ts)
    {
        ts = default;
        var m = Regex.Match(t ?? "", @"^\s*([01]?\d|2[0-3])[:.]([0-5]\d)\s*$");
        if (!m.Success) return false;
        ts = new TimeSpan(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), 0);
        return true;
    }

    /// <summary>"9:5" is not accepted; "9:05" and "09.05" become "09:05". Null when it is not a time.</summary>
    public static string? NormalizeTime(string? t) => TryTime(t, out var ts) ? ts.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : null;

    public static bool TryDate(string? d, out DateTime date) =>
        DateTime.TryParseExact((d ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>The date + time of a one-time run, or the first run of an interval schedule.</summary>
    public DateTime? Start => TryDate(Date, out var d) && TryTime(Time, out var t) ? d.Date + t : null;

    /// <summary>A Loc key when the definition can't be used, else null. now: a one-time run must be in the future.</summary>
    public string? Error(DateTime? now = null)
    {
        if (Rel.Length == 0) return "schedErrScript";
        switch (Kind)
        {
            case "once":
                if (!TryDate(Date, out _)) return "schedErrDate";
                if (!TryTime(Time, out _)) return "schedErrTime";
                if (now != null && Start <= now) return "schedErrPast";
                return null;
            case "daily":
                return TryTime(Time, out _) ? null : "schedErrTime";
            case "weekly":
                if (!TryTime(Time, out _)) return "schedErrTime";
                return Days.Count == 0 ? "schedErrDays" : null;
            case "interval":
                return Every is < MinEvery or > MaxEvery ? "schedErrEvery" : null;
            case "logon":
                return null;
            default:
                return "schedErrKind";
        }
    }

    static DayOfWeek Dow(string key) => key switch
    {
        "mon" => DayOfWeek.Monday, "tue" => DayOfWeek.Tuesday, "wed" => DayOfWeek.Wednesday, "thu" => DayOfWeek.Thursday,
        "fri" => DayOfWeek.Friday, "sat" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday
    };

    /// <summary>The next time Task Scheduler will start it (null: at logon, a past one-time run, disabled, or invalid).</summary>
    public DateTime? NextRun(DateTime now)
    {
        if (!Enabled || Error() != null) return null;
        TryTime(Time, out var t);
        switch (Kind)
        {
            case "once":
                return Start > now ? Start : null;
            case "daily":
            {
                var d = now.Date + t;
                return d > now ? d : d.AddDays(1);
            }
            case "weekly":
                for (int i = 0; i < 8; i++)
                {
                    var d = now.Date.AddDays(i) + t;
                    if (d > now && Days.Any(k => Dow(k) == d.DayOfWeek)) return d;
                }
                return null;
            case "interval":
            {
                var start = Start ?? now;
                if (start > now) return start;
                long step = Every * TimeSpan.TicksPerMinute;
                long k = (now - start).Ticks / step + 1;
                return start.AddTicks(k * step);
            }
            default:
                return null;
        }
    }

    static CultureInfo Culture => CultureInfo.GetCultureInfo(Loc.Language == "ru" ? "ru-RU" : "en-US");

    /// <summary>"Daily at 09:00", "Mon, Wed, Fri at 08:30", "Every 15 min", "At Windows sign-in", "Once: 23.10.2026 09:00".</summary>
    public string Describe()
    {
        var c = Culture;
        switch (Kind)
        {
            case "once":
                return Start is { } st ? Loc.F("schedDescOnce", st.ToString("d", c), Time) : Loc.T("schedKindOnce");
            case "daily":
                return Loc.F("schedDescDaily", Time);
            case "weekly":
                var names = Days.Select(k => c.DateTimeFormat.GetAbbreviatedDayName(Dow(k)));
                return Loc.F("schedDescWeekly", string.Join(", ", names), Time);
            case "logon":
                return Loc.T("schedDescLogon");
            case "interval":
                return Loc.F("schedDescEvery", Every);
            default:
                return Kind;
        }
    }
}

/// <summary>The "schedules" list of the settings.</summary>
public static class ScheduleList
{
    public static List<Schedule> All(Settings s) => s.Schedules.Select(Schedule.Parse).Where(x => x != null).Select(x => x!).ToList();

    public static Schedule? Get(Settings s, string id) => All(s).FirstOrDefault(x => x.Id == id);

    public static List<Schedule> ForScript(Settings s, string rel) =>
        All(s).Where(x => string.Equals(x.Rel, rel, StringComparison.OrdinalIgnoreCase)).ToList();

    public static bool Has(Settings s, string rel) => ForScript(s, rel).Count > 0;

    /// <summary>Adds the schedule or replaces the one with its id (the order is kept).</summary>
    public static void Put(Settings s, Schedule sched)
    {
        var list = All(s);
        int i = list.FindIndex(x => x.Id == sched.Id);
        if (i >= 0) list[i] = sched; else list.Add(sched);
        s.Schedules = list.Select(x => x.ToLine()).ToList();
    }

    public static bool Remove(Settings s, string id)
    {
        var list = All(s);
        int n = list.RemoveAll(x => x.Id == id);
        s.Schedules = list.Select(x => x.ToLine()).ToList();
        return n > 0;
    }

    /// <summary>A script was renamed or moved: its schedules follow (the tasks refer to the schedule id and stay as they are).
    /// newRel null (deleted) keeps them: the tasks must be removed from Task Scheduler too, which the app does.</summary>
    public static void MoveScript(Settings s, string oldRel, string? newRel)
    {
        if (newRel == null) return;
        var list = All(s);
        foreach (var x in list) if (string.Equals(x.Rel, oldRel, StringComparison.OrdinalIgnoreCase)) x.Rel = newRel;
        s.Schedules = list.Select(x => x.ToLine()).ToList();
    }

    public static void MoveFolder(Settings s, Func<string, string> fix)
    {
        var list = All(s);
        foreach (var x in list) x.Rel = fix(x.Rel);
        s.Schedules = list.Select(x => x.ToLine()).ToList();
    }

    public static List<Schedule> UsingPreset(Settings s, string rel, string name) =>
        ForScript(s, rel).Where(x => string.Equals(x.Preset, name, StringComparison.OrdinalIgnoreCase)).ToList();
}

/// <summary>The Task Scheduler definition of a schedule (Task Scheduler 1.2 XML, registered with schtasks /Create /XML).
/// XML instead of schtasks' own /SC /SD /ST switches: /SD takes the date in the system's short date format, which differs
/// per locale; StartBoundary is ISO 8601. The task runs as the current user, only when signed in (InteractiveToken, so a
/// window or a notification can be shown), with normal rights (LeastPrivilege; no administrator rights, no UAC).</summary>
public static class TaskXml
{
    public static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    static string Iso(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    static readonly Dictionary<string, string> DayNames = new()
    {
        ["mon"] = "Monday", ["tue"] = "Tuesday", ["wed"] = "Wednesday", ["thu"] = "Thursday", ["fri"] = "Friday", ["sat"] = "Saturday", ["sun"] = "Sunday"
    };

    /// <summary>userId: the user's SID (or DOMAIN\name). now: the start of daily / weekly triggers (today) and of an interval
    /// schedule without a start.</summary>
    /// <summary>The folder of a Windows path (also when the tests run elsewhere).</summary>
    static string WindowsDir(string path) { int i = path.LastIndexOfAny(new[] { '\\', '/' }); return i > 0 ? path.Substring(0, i) : ""; }

    public static string Build(Schedule s, string exePath, string userId, string taskName, string description, DateTime now)
    {
        XElement E(string name, params object?[] content) => new(Ns + name, content);
        Schedule.TryTime(s.Time, out var t);
        XElement trigger = s.Kind switch
        {
            "once" => E("TimeTrigger", E("StartBoundary", Iso(s.Start ?? now)), E("Enabled", "true")),
            "daily" => E("CalendarTrigger", E("StartBoundary", Iso(now.Date + t)), E("Enabled", "true"),
                E("ScheduleByDay", E("DaysInterval", "1"))),
            "weekly" => E("CalendarTrigger", E("StartBoundary", Iso(now.Date + t)), E("Enabled", "true"),
                E("ScheduleByWeek", E("DaysOfWeek", s.Days.Where(DayNames.ContainsKey).Select(d => E(DayNames[d]))), E("WeeksInterval", "1"))),
            "logon" => E("LogonTrigger", E("Enabled", "true"), E("UserId", userId), E("Delay", "PT30S")),
            "interval" => E("TimeTrigger",
                E("Repetition", E("Interval", "PT" + Math.Clamp(s.Every, Schedule.MinEvery, Schedule.MaxEvery) + "M"), E("StopAtDurationEnd", "false")),
                E("StartBoundary", Iso(s.Start ?? new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1))), E("Enabled", "true")),
            _ => throw new ArgumentException("kind")
        };
        var doc = new XDocument(
            E("Task", new XAttribute("version", "1.2"),
                E("RegistrationInfo", E("Author", "Shelly"), E("Description", description), E("URI", taskName)),
                E("Triggers", trigger),
                E("Principals", E("Principal", new XAttribute("id", "Author"),
                    E("UserId", userId), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
                E("Settings",
                    E("MultipleInstancesPolicy", "IgnoreNew"),
                    E("DisallowStartIfOnBatteries", "false"),
                    E("StopIfGoingOnBatteries", "false"),
                    E("AllowHardTerminate", "true"),
                    E("StartWhenAvailable", "true"),
                    E("RunOnlyIfNetworkAvailable", "false"),
                    E("IdleSettings", E("StopOnIdleEnd", "false"), E("RestartOnIdle", "false")),
                    E("AllowStartOnDemand", "true"),
                    E("Enabled", s.Enabled ? "true" : "false"),
                    E("Hidden", "false"),
                    E("RunOnlyIfIdle", "false"),
                    E("WakeToRun", "false"),
                    E("ExecutionTimeLimit", "PT0S"),
                    E("Priority", "7")),
                E("Actions", new XAttribute("Context", "Author"),
                    E("Exec", E("Command", exePath), E("Arguments", SchTasks.TaskArguments(s.Id)),
                        E("WorkingDirectory", WindowsDir(exePath))))));
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(new StringWriter(sb), new XmlWriterSettings { Indent = true, Encoding = Encoding.Unicode }))
            doc.Save(w);
        return sb.ToString();
    }
}

/// <summary>Task Scheduler through schtasks.exe (part of Windows, no extra packages, no administrator rights for the user's
/// own tasks). Every Shelly copy (folder) and user gets its own prefix, so tasks of a portable copy elsewhere are never
/// touched: \Shelly\&lt;copy&gt;-&lt;schedule id&gt;.</summary>
public static class SchTasks
{
    public const string Folder = "\\Shelly\\";

    /// <summary>8 hex characters for this Shelly folder and user.</summary>
    public static string InstanceId(string root, string user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.TrimEnd('\\', '/').ToLowerInvariant() + "|" + user.ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();

    public static string TaskName(string inst, string id) => Folder + inst + "-" + id;

    /// <summary>The schedule id in a task name of this copy, or null (another copy's task, or not Shelly's).</summary>
    public static string? IdOf(string taskName, string inst)
    {
        string pre = Folder + inst + "-";
        if (!taskName.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return null;
        string id = taskName.Substring(pre.Length).ToLowerInvariant();
        return Regex.IsMatch(id, "^[0-9a-f]{8}$") ? id : null;
    }

    public static string TaskArguments(string id) => "--scheduled " + id;

    public static string[] CreateArgs(string taskName, string xmlPath) => new[] { "/Create", "/TN", taskName, "/XML", xmlPath, "/F" };
    public static string[] DeleteArgs(string taskName) => new[] { "/Delete", "/TN", taskName, "/F" };
    internal static string[] RunArgs(string taskName) => new[] { "/Run", "/TN", taskName };
    /// <summary>All tasks, verbose CSV without headers: column 1 is the task name, column 8 "Task To Run" (the same order in
    /// every Windows language; only the headers are translated).</summary>
    public static string[] QueryArgs() => new[] { "/Query", "/FO", "CSV", "/V", "/NH" };

    /// <summary>One CSV line (quoted fields, "" inside quotes).</summary>
    public static List<string> CsvFields(string line)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') q = false;
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res;
    }

    /// <summary>Task names and their command lines from the query output (a task with several triggers has several lines).</summary>
    public static List<(string Name, string Run)> ParseQuery(string output)
    {
        var res = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (output ?? "").Split('\n'))
        {
            string line = raw.Trim('\r', ' ');
            if (!line.StartsWith("\"")) continue;
            var f = CsvFields(line);
            if (f.Count < 9 || !f[1].StartsWith("\\")) continue;
            if (seen.Add(f[1])) res.Add((f[1], f[8]));
        }
        return res;
    }

    /// <summary>Settings are the source of truth: a schedule without its task is "missing"; one whose task starts another
    /// Shelly.exe (the folder was moved) is "outdated"; a task of this copy without a schedule is an orphan.</summary>
    public static Reconciliation Reconcile(IEnumerable<Schedule> schedules, IEnumerable<(string Name, string Run)> tasks, string inst, string exePath)
    {
        var r = new Reconciliation();
        var mine = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, run) in tasks)
        {
            var id = IdOf(name, inst);
            if (id != null) mine[id] = run;
        }
        // A path with characters outside ASCII may come back in another code page: it is not compared
        bool comparable = exePath.All(ch => ch < 128);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in schedules)
        {
            ids.Add(s.Id);
            if (!mine.TryGetValue(s.Id, out var run)) r.State[s.Id] = "missing";
            else if (comparable && run.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) < 0) r.State[s.Id] = "outdated";
            else r.State[s.Id] = "ok";
        }
        foreach (var id in mine.Keys) if (!ids.Contains(id)) r.Orphans.Add(TaskName(inst, id));
        r.Orphans.Sort(StringComparer.OrdinalIgnoreCase);
        return r;
    }
}

public sealed class Reconciliation
{
    /// <summary>Schedule id → ok | missing | outdated.</summary>
    public Dictionary<string, string> State { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tasks in \Shelly\ of this copy that no schedule refers to.</summary>
    public List<string> Orphans { get; } = new();
    public int Missing => State.Values.Count(v => v != "ok");
}
