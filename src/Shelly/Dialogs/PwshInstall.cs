using System.Diagnostics;
using System.Windows.Threading;
using Shelly.Core;
using Shelly.Services;

namespace Shelly.Dialogs;

/// <summary>Install PowerShell 7: winget (in a visible window), the MSI from GitHub, or the download page;
/// then Shelly watches for pwsh.exe and switches the scripts over when it appears.</summary>
public static class PwshInstall
{
    static DispatcherTimer? watch;
    static DateTime watchUntil;
    public static bool Watching => watch != null;
    public static event Action? WatchChanged;

    public static void Show()
    {
        var st = App.S;
        if (st.RecheckEngines() && st.PwshOk) { App.Status(Loc.F("p7Found", st.Pwsh!.Version, st.Pwsh.Path)); return; }
        string? winget = EngineLocator.FindWinget();
        string choice;
        if (winget != null)
            choice = Dlg.Choose(Loc.T("p7InstallTitle"), Loc.T("p7InstallTitle"), Loc.F("p7WingetText", "winget " + EngineLocator.WingetArgs), Glyphs.Download, "AccentTextFillColorPrimaryBrush",
                new("winget", Loc.T("p7BtnWinget"), true), new("msi", Loc.T("p7BtnMsi")), new("page", Loc.T("p7BtnPage")), new("cancel", Loc.T("cancel")));
        else
            choice = Dlg.Choose(Loc.T("p7InstallTitle"), Loc.T("p7InstallTitle"), Loc.T("p7NoWingetText"), Glyphs.Download, "AccentTextFillColorPrimaryBrush",
                new("msi", Loc.T("p7BtnMsi"), true), new("page", Loc.T("p7BtnPage")), new("cancel", Loc.T("cancel")));
        switch (choice)
        {
            case "winget":
            case "msi":
                try
                {
                    string exe = st.Ps51 ?? st.Pwsh?.Path ?? "powershell.exe";
                    string script = EngineLocator.InstallScript(choice, winget);
                    Process.Start(new ProcessStartInfo(exe, "-NoProfile -ExecutionPolicy Bypass -NoExit -Command " + Quoting.NativeArg(script)) { UseShellExecute = true });
                    App.Status(Loc.T("p7Started"));
                    StartWatch();
                }
                catch (Exception ex) { App.Status(Loc.F("startFail", ex.Message), true); }
                break;
            case "page":
                if (ShellOps.Open(EngineLocator.PwshReleasePage)) { App.Status(Loc.T("p7PageOpened")); StartWatch(); }
                break;
        }
    }

    public static void StartWatch()
    {
        watchUntil = DateTime.Now.AddMinutes(20);
        if (watch != null) return;
        watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        watch.Tick += (_, _) =>
        {
            var st = App.S;
            st.RecheckEngines();
            if (st.PwshOk)
            {
                App.Status(Loc.F("p7Found", st.Pwsh!.Version, st.Pwsh.Path));
                StopWatch();
            }
            else if (DateTime.Now > watchUntil) StopWatch();
        };
        watch.Start();
        WatchChanged?.Invoke();
    }

    static void StopWatch()
    {
        watch?.Stop();
        watch = null;
        WatchChanged?.Invoke();
    }
}
