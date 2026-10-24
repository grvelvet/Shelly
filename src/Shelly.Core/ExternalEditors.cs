using System.Globalization;

namespace Shelly.Core;

/// <summary>Detected external editor info.</summary>
public sealed record DetectedEditor(string Id, string Name, string ExePath, bool Installed);

/// <summary>Resolved command line for launching an external editor.</summary>
public sealed record ExternalEditorCommand(
    string EditorId,
    string DisplayName,
    string ExePath,
    string Arguments,
    bool UsedIseFallback = false);

/// <summary>External editor detection and command-line resolution (1.4.0).</summary>
public static class ExternalEditors
{
    public static readonly string[] DefaultChoices = { "builtin", "external" };
    public static readonly string[] EditorChoices = { "auto", "vscode", "notepadpp", "ise", "notepad", "custom" };

    public static List<DetectedEditor> DetectAll(Func<string, bool>? fileExists = null, Func<string, string?>? getEnv = null)
    {
        var exists = fileExists ?? File.Exists;
        var env = getEnv ?? Environment.GetEnvironmentVariable;

        string? vsCode = FindVsCode(exists, env);
        string? npp = FindNotepadPP(exists, env);
        string? ise = FindIse(exists, env);
        string notepad = FindNotepad(exists, env);

        return new List<DetectedEditor>
        {
            new("vscode", Loc.T("edVsCode"), vsCode ?? "", vsCode != null),
            new("notepadpp", Loc.T("edNotepadPP"), npp ?? "", npp != null),
            new("ise", Loc.T("edIse"), ise ?? "", ise != null),
            new("notepad", Loc.T("edNotepad"), notepad, true)
        };
    }

    public static string? FindVsCode(Func<string, bool>? fileExists = null, Func<string, string?>? getEnv = null)
    {
        var exists = fileExists ?? File.Exists;
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        var candidates = new List<string>();

        string? localApp = env("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(localApp))
            candidates.Add(Path.Combine(localApp, "Programs", "Microsoft VS Code", "Code.exe"));
        string? pf = env("ProgramFiles");
        if (!string.IsNullOrEmpty(pf))
            candidates.Add(Path.Combine(pf, "Microsoft VS Code", "Code.exe"));
        string? pf86 = env("ProgramFiles(x86)");
        if (!string.IsNullOrEmpty(pf86))
            candidates.Add(Path.Combine(pf86, "Microsoft VS Code", "Code.exe"));

        foreach (var c in candidates)
            if (exists(c)) return c;

        string? fromPath = FindOnPath("Code.exe", exists, env);
        if (fromPath != null) return fromPath;
        return null;
    }

    public static string? FindNotepadPP(Func<string, bool>? fileExists = null, Func<string, string?>? getEnv = null)
    {
        var exists = fileExists ?? File.Exists;
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        var candidates = new List<string>();

        string? pf = env("ProgramFiles");
        if (!string.IsNullOrEmpty(pf))
            candidates.Add(Path.Combine(pf, "Notepad++", "notepad++.exe"));
        string? pf86 = env("ProgramFiles(x86)");
        if (!string.IsNullOrEmpty(pf86))
            candidates.Add(Path.Combine(pf86, "Notepad++", "notepad++.exe"));
        string? localApp = env("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(localApp))
            candidates.Add(Path.Combine(localApp, "Notepad++", "notepad++.exe"));

        foreach (var c in candidates)
            if (exists(c)) return c;

        string? fromPath = FindOnPath("notepad++.exe", exists, env);
        if (fromPath != null) return fromPath;
        return null;
    }

    public static string? FindIse(Func<string, bool>? fileExists = null, Func<string, string?>? getEnv = null)
    {
        var exists = fileExists ?? File.Exists;
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        string sysRoot = env("SystemRoot") ?? @"C:\Windows";
        string p = Path.Combine(sysRoot, "System32", "WindowsPowerShell", "v1.0", "powershell_ise.exe");
        if (exists(p)) return p;
        return null;
    }

    public static string FindNotepad(Func<string, bool>? fileExists = null, Func<string, string?>? getEnv = null)
    {
        var exists = fileExists ?? File.Exists;
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        string? sysRoot = env("SystemRoot");
        if (!string.IsNullOrEmpty(sysRoot))
        {
            string p = Path.Combine(sysRoot, "System32", "notepad.exe");
            if (exists(p)) return p;
        }
        return "notepad.exe";
    }

    static string? FindOnPath(string exeName, Func<string, bool> exists, Func<string, string?> env)
    {
        string? pathEnv = env("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        foreach (var raw in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            try
            {
                string full = Path.Combine(dir, exeName);
                if (exists(full)) return full;
                // VS Code puts bin/code.cmd on PATH; check ../Code.exe
                if (string.Equals(exeName, "Code.exe", StringComparison.OrdinalIgnoreCase) &&
                    dir.EndsWith("bin", StringComparison.OrdinalIgnoreCase))
                {
                    string? parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        string up = Path.Combine(parent, "Code.exe");
                        if (exists(up)) return up;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    public static string Quote(string path) => "\"" + (path ?? "").Replace("\"", "\\\"") + "\"";

    public static string FormatVsCodeArgs(string scriptPath, int line = 0) =>
        line > 0
            ? "-g " + Quote(scriptPath + ":" + line.ToString(CultureInfo.InvariantCulture))
            : Quote(scriptPath);

    public static string FormatNotepadPPArgs(string scriptPath, int line = 0) =>
        line > 0
            ? "-n" + line.ToString(CultureInfo.InvariantCulture) + " " + Quote(scriptPath)
            : Quote(scriptPath);

    public static string FormatCustomArgs(string template, string scriptPath, int line = 0)
    {
        string t = (template ?? "").Trim();
        int ln = line > 0 ? line : 1;
        string lineStr = ln.ToString(CultureInfo.InvariantCulture);
        if (t.Length == 0) return Quote(scriptPath);

        bool hasFile = t.Contains("{file}", StringComparison.OrdinalIgnoreCase);
        string res = t.Replace("{line}", lineStr, StringComparison.OrdinalIgnoreCase);
        if (hasFile)
        {
            // If template already has "{file}" in quotes, replace raw; else quote it
            res = res.Replace("\"{file}\"", Quote(scriptPath), StringComparison.OrdinalIgnoreCase)
                     .Replace("{file}", Quote(scriptPath), StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            res = res + " " + Quote(scriptPath);
        }
        return res;
    }

    /// <summary>Resolves which external editor executable and arguments to run for <paramref name="scriptPath"/>.</summary>
    public static ExternalEditorCommand Resolve(
        Settings s,
        string scriptPath,
        int line = 0,
        Func<string, bool>? fileExists = null,
        Func<string, string?>? getEnv = null,
        string? pythonExe = null)
    {
        var exists = fileExists ?? File.Exists;
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        string choice = (s.ExternalEditor ?? "auto").ToLowerInvariant();
        string ext = (Path.GetExtension(scriptPath) ?? "").ToLowerInvariant();
        bool isPs1 = ext == ".ps1" || ext == ".psm1" || ext == ".psd1";
        bool isPy = ext == ".py" || ext == ".pyw";

        if (choice == "custom")
        {
            string customExe = (s.ExternalEditorPath ?? "").Trim().Trim('"');
            if (customExe.Length > 0 && exists(customExe))
            {
                string name = Path.GetFileNameWithoutExtension(customExe);
                return new ExternalEditorCommand("custom", name, customExe, FormatCustomArgs(s.ExternalEditorArgs, scriptPath, line));
            }
            choice = "auto";
        }

        if (choice == "vscode")
        {
            string? vs = FindVsCode(exists, env);
            if (vs != null)
                return new ExternalEditorCommand("vscode", Loc.T("edVsCode"), vs, FormatVsCodeArgs(scriptPath, line));
            choice = "auto";
        }

        if (choice == "notepadpp")
        {
            string? npp = FindNotepadPP(exists, env);
            if (npp != null)
                return new ExternalEditorCommand("notepadpp", Loc.T("edNotepadPP"), npp, FormatNotepadPPArgs(scriptPath, line));
            choice = "auto";
        }

        bool iseFallback = false;
        if (choice == "ise")
        {
            if (isPs1)
            {
                string? ise = FindIse(exists, env);
                if (ise != null)
                    return new ExternalEditorCommand("ise", Loc.T("edIse"), ise, "-File " + Quote(scriptPath));
            }
            else
            {
                // Never open .py, .bat, .cmd, or custom scripts in powershell_ise.exe!
                iseFallback = true;
            }
            choice = "auto";
        }

        if (choice == "notepad")
        {
            string np = FindNotepad(exists, env);
            return new ExternalEditorCommand("notepad", Loc.T("edNotepad"), np, Quote(scriptPath), iseFallback);
        }

        // "auto" mode:
        // 1. Prefer VS Code or Notepad++ for all script types when installed
        string? autoVs = FindVsCode(exists, env);
        if (autoVs != null)
            return new ExternalEditorCommand("vscode", Loc.T("edVsCode"), autoVs, FormatVsCodeArgs(scriptPath, line), iseFallback);

        string? autoNpp = FindNotepadPP(exists, env);
        if (autoNpp != null)
            return new ExternalEditorCommand("notepadpp", Loc.T("edNotepadPP"), autoNpp, FormatNotepadPPArgs(scriptPath, line), iseFallback);

        // 2. Otherwise by script extension:
        // .ps1 -> PowerShell ISE (if present) else Notepad
        if (isPs1)
        {
            string? ise = FindIse(exists, env);
            if (ise != null)
                return new ExternalEditorCommand("ise", Loc.T("edIse"), ise, "-File " + Quote(scriptPath), iseFallback);
        }
        // .py -> IDLE (pyw.exe -m idlelib) if Python is available, else Notepad
        else if (isPy && !string.IsNullOrWhiteSpace(pythonExe))
        {
            string py = pythonExe.Trim().Trim('"');
            string? dir = Path.GetDirectoryName(py);
            string pyw = !string.IsNullOrEmpty(dir) ? Path.Combine(dir, "pyw.exe") : "";
            string runner = !string.IsNullOrEmpty(pyw) && exists(pyw) ? pyw : py;
            if (exists(runner))
                return new ExternalEditorCommand("idle", "IDLE", runner, "-m idlelib " + Quote(scriptPath), iseFallback);
        }

        // .bat/.cmd, custom scripts, or fallback -> Notepad
        string notepad = FindNotepad(exists, env);
        return new ExternalEditorCommand("notepad", Loc.T("edNotepad"), notepad, Quote(scriptPath), iseFallback);
    }

    public static string SummaryText(Settings s)
    {
        string defPart = s.DefaultEditor == "external" ? Loc.T("edChoiceExternal") : Loc.T("edChoiceBuiltin");
        string extPart = (s.ExternalEditor ?? "auto") switch
        {
            "vscode" => Loc.T("edVsCode"),
            "notepadpp" => Loc.T("edNotepadPP"),
            "ise" => Loc.T("edIse"),
            "notepad" => Loc.T("edNotepad"),
            "custom" => string.IsNullOrWhiteSpace(s.ExternalEditorPath) ? Loc.T("edCustom") : Path.GetFileName(s.ExternalEditorPath),
            _ => Loc.T("rsEngAutoShort")
        };
        return defPart + " · " + extPart;
    }
}
