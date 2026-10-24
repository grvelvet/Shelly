using System.Globalization;

namespace Shelly.Core;

/// <summary>Results for people: Success / Failed (code N) / Stopped / Window closed / Crashed (...), with the raw code in a tip.</summary>
/// <summary>RunEntry.Kind and LastRun.Kind (1.3.1: named; the values are the ones in existing history files).</summary>
public static class RunKinds
{
    public const string Running = "running", Ok = "ok", Fail = "fail", Stopped = "stopped", Closed = "closed", Started = "started";
}

public static class RunResult
{
    public sealed record CodeInfo(long Code, string Hex, string Name, string Kind, string Key);

    public static CodeInfo ExitCodeInfo(long code)
    {
        long signed = code;
        if (signed > int.MaxValue && signed <= uint.MaxValue) signed -= 4294967296L;
        long u = signed < 0 ? signed + 4294967296L : signed;
        string hex = u >= 0 && u <= uint.MaxValue ? u.ToString("X8") : code.ToString("X");
        return signed switch
        {
            -1073741510 => new(signed, hex, "STATUS_CONTROL_C_EXIT", "closed", ""),
            1073807364 => new(signed, hex, "DBG_TERMINATE_PROCESS", "killed", ""),
            -1073741819 => new(signed, hex, "STATUS_ACCESS_VIOLATION", "crash", "crashAccess"),
            -1073741571 => new(signed, hex, "STATUS_STACK_OVERFLOW", "crash", "crashStack"),
            -1073740791 => new(signed, hex, "STATUS_STACK_BUFFER_OVERRUN", "crash", "crashBuffer"),
            -1073741502 => new(signed, hex, "STATUS_DLL_INIT_FAILED", "crash", "crashDll"),
            -1073741795 => new(signed, hex, "STATUS_ILLEGAL_INSTRUCTION", "crash", "crashIllegal"),
            _ => new(signed, hex, "", "", "")
        };
    }

    public static (string Text, string Tip) Format(string kind, long code)
    {
        var info = ExitCodeInfo(code);
        string tip = "";
        if (code != 0) tip = info.Name.Length > 0 ? Loc.F("codeTipName", info.Code, info.Hex, info.Name) : Loc.F("codeTip", info.Code, info.Hex);
        string text = kind switch
        {
            "ok" => Loc.T("resOk"),
            "stopped" => Loc.T("resStopped"),
            "running" => Loc.T("histRunning"),
            "started" => Loc.T("histStarted"),
            "interrupted" => Loc.T("histInterrupted"),
            "closed" => Loc.T("resClosed"),
            _ => info.Kind switch
            {
                "closed" => Loc.T("resClosed"),
                "killed" => Loc.T("resKilled"),
                "crash" => Loc.F("resCrash", Loc.T(info.Key)),
                _ => Loc.F("resFail", info.Code < -32768 || info.Code > 65535 ? "0x" + info.Hex : info.Code.ToString(CultureInfo.InvariantCulture))
            }
        };
        return (text, tip);
    }

    public static string KindFor(int exitCode, bool stoppedByUser) => stoppedByUser ? RunKinds.Stopped : exitCode == 0 ? RunKinds.Ok : RunKinds.Fail;

    public static string FormatDuration(double ms)
    {
        if (ms < 0) ms = 0;
        if (ms < 60000) return Loc.F("durS", ms / 1000.0);
        int sec = (int)Math.Floor(ms / 1000);
        if (sec < 3600) return Loc.F("durM", sec / 60, sec % 60);
        return Loc.F("durH", sec / 3600, (sec % 3600) / 60);
    }

    public static string FormatElapsed(double seconds)
    {
        int s = (int)Math.Floor(Math.Max(0, seconds));
        if (s >= 3600) return $"{s / 3600}:{(s % 3600) / 60:00}:{s % 60:00}";
        return $"{s / 60}:{s % 60:00}";
    }

    public static string FormatRunTime(DateTime t) => t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("dd.MM HH:mm");

    public static string ModeText(string mode) => mode switch { "window" => Loc.T("modeWindow"), "admin" => Loc.T("modeAdmin"), _ => Loc.T("modeInline") };
    public static string OriginText(string origin) => origin switch
    {
        "tray" => Loc.T("originTray"), "editor" => Loc.T("originEditor"), "rerun" => Loc.T("originRerun"), "jumplist" => Loc.T("originTray"), "schedule" => Loc.T("originSchedule"), _ => Loc.T("originList")
    };

    /// <summary>The row mark of a last result: glyph, colour kind (ok | fail | stopped | window) and tooltip line.</summary>
    public static (string Mark, string Kind, string Line, string Tip) LastRunView(LastRun r)
    {
        string when = FormatRunTime(r.Time);
        var res = Format(r.Kind, r.Code);
        string line = Loc.F("lastLine", when, FormatDuration(r.Ms), res.Text);
        string mark = "\uE81C", kind = "window";
        switch (r.Kind)
        {
            case "ok": mark = "\uE73E"; kind = "ok"; break;
            case "fail": mark = "\uE711"; kind = "fail"; break;
            case "stopped": mark = "\uE71A"; kind = "stopped"; break;
            case "closed": break;
            default: line = Loc.F("lastLineShort", when, res.Text); break;
        }
        string tip = res.Tip.Length > 0 ? line + "\n" + res.Tip : line;
        return (mark, kind, line, tip);
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return Loc.F("sizeB", bytes);
        if (bytes < 1024 * 1024) return Loc.F("sizeKB", Math.Round(bytes / 1024.0, 1));
        return Loc.F("sizeMB", Math.Round(bytes / 1048576.0, 1));
    }
}
