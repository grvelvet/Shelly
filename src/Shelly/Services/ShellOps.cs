using System.Diagnostics;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using Shelly.Core;

namespace Shelly.Services;

/// <summary>Explorer, browser, clipboard and Recycle Bin.</summary>
public static class ShellOps
{
    public static bool Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return true; }
        catch (Exception ex) { App.Status(Loc.F("openFail", ex.Message), true); return false; }
    }

    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", Quoting.NativeArg(dir)) { UseShellExecute = true });
        }
        catch (Exception ex) { App.Status(Loc.F("folderOpenFail", ex.Message), true); }
    }

    public static void ShowInExplorer(string file)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select," + Quoting.NativeArg(file)) { UseShellExecute = true }); }
        catch (Exception ex) { App.Status(Loc.F("openFail", ex.Message), true); }
    }

    public static bool CopyText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch { Thread.Sleep(60); }
        }
        App.Status(Loc.F("outCopyFail", "clipboard"), true);
        return false;
    }

    public static void RecycleFile(string path) =>
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);

    public static void RecycleFolder(string path) =>
        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);

    /// <summary>Open a script in the configured or auto-detected external editor (1.4.0).</summary>
    public static void ExternalEditor(string path, int line = 0)
    {
        try
        {
            var st = App.S;
            var cmd = ExternalEditors.Resolve(st.Settings, path, line, pythonExe: st.Python?.Path);
            if (cmd.UsedIseFallback)
                App.Status(Loc.F("edIseFallback", cmd.DisplayName));
            Process.Start(new ProcessStartInfo(cmd.ExePath, cmd.Arguments) { UseShellExecute = false });
        }
        catch (Exception ex) { App.Status(Loc.F("openFail", ex.Message), true); }
    }
}
