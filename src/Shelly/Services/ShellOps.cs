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

    /// <summary>Open a script in an external editor: PowerShell ISE when present, else Notepad.</summary>
    public static void ExternalEditor(string path)
    {
        string ise = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell_ise.exe");
        try
        {
            if (File.Exists(ise)) Process.Start(new ProcessStartInfo(ise, "-File " + Quoting.NativeArg(path)) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("notepad.exe", Quoting.NativeArg(path)) { UseShellExecute = true });
        }
        catch (Exception ex) { App.Status(Loc.F("openFail", ex.Message), true); }
    }
}
