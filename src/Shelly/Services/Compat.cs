using Shelly.Core;

namespace Shelly.Services;

/// <summary>Compatibility check: Windows, .NET, both PowerShell engines, execution policy, C# compiler, hotkey, tray, analysis.</summary>
public static class Compat
{
    public sealed record Item(string Key, string Title, string Level, string Text);

    public static List<Item> Check()
    {
        var st = App.S;
        var list = new List<Item>();
        int build = SystemInfo.WindowsBuild;
        list.Add(build >= 22000 ? new("win", Loc.T("cWin"), "ok", Loc.F("cWin11", build))
               : build >= 17763 ? new("win", Loc.T("cWin"), "info", Loc.F("cWin10", build))
               : new("win", Loc.T("cWin"), "warn", Loc.F("cWinOld", build)));
        list.Add(new("host", Loc.T("cHost"), "ok", Loc.F("cHostOk", Environment.Version.ToString(3), System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant())));
        list.Add(st.Ps51Ok ? new("ps51", Loc.T("cPs51"), "ok", Loc.F("cPs51Ok", st.Ps51)) : new("ps51", Loc.T("cPs51"), "warn", Loc.T("cPs51No")));
        int need7 = st.Scripts.Count(s => st.Meta(s).Engine.Missing);
        if (st.PwshOk) list.Add(new("pwsh", Loc.T("cPwsh"), "ok", Loc.F("cPwshOk", st.Pwsh!.Label, st.Pwsh.Path)));
        else if (need7 > 0) list.Add(new("pwsh", Loc.T("cPwsh"), "warn", Loc.F("cPwshNeeded", need7)));
        else list.Add(new("pwsh", Loc.T("cPwsh"), "info", Loc.T("cPwshNo")));
        string? pol = SystemInfo.PolicyExecution();
        if (pol == null) list.Add(new("policy", Loc.T("cPolicy"), "ok", Loc.T("cPolicyOk")));
        else if (pol.Equals("AllSigned", StringComparison.OrdinalIgnoreCase) || pol.Equals("Restricted", StringComparison.OrdinalIgnoreCase))
            list.Add(new("policy", Loc.T("cPolicy"), "error", Loc.F("cPolicyBlock", pol)));
        else list.Add(new("policy", Loc.T("cPolicy"), "info", Loc.F("cPolicySet", pol)));
        var eng = st.Analysis.Eng;
        var avail = eng.Available().ToList();
        list.Add(avail.Count > 0 ? new("analyzer", Loc.T("cAnalyzer"), "ok", Loc.F("cAnalyzerOk", string.Join(", ", avail.Select(EngineInfo.Name))))
                                 : new("analyzer", Loc.T("cAnalyzer"), "warn", Loc.T("cAnalyzerNo")));
        string? csc = ExeBuilder.FindCsc();
        list.Add(csc != null ? new("csc", Loc.T("cCsc"), "ok", Loc.F("cCscOk", csc)) : new("csc", Loc.T("cCsc"), "info", Loc.T("cCscNo")));
        string? net = SystemInfo.NetFrameworkVersion();
        list.Add(net == null ? new("net", Loc.T("cNet"), "info", Loc.T("cNetNone"))
               : string.CompareOrdinal(net, "4.5") < 0 ? new("net", Loc.T("cNet"), "info", Loc.F("cNetOld", net))
               : new("net", Loc.T("cNet"), "ok", Loc.F("cNetOk", net)));
        var hk = App.Hotkey;
        if (hk != null) list.Add(new("hotkey", Loc.T("cHotkey"), hk.Ok ? "ok" : "warn", hk.StateText));
        list.Add(App.Tray?.Available == true ? new("tray", Loc.T("cTray"), "ok", Loc.T("cWorks")) : new("tray", Loc.T("cTray"), "warn", Loc.T("trayNA")));
        return list;
    }

    /// <summary>Warnings worth the info bar, and a key that changes when they change (for "dismissed").</summary>
    public static (List<Item> Items, string Key) Warnings()
    {
        var w = Check().Where(i => i.Level is "warn" or "error").Where(i => i.Key != "pwsh").ToList();
        return (w, string.Join(",", w.Select(i => i.Key + ":" + i.Level)));
    }
}
