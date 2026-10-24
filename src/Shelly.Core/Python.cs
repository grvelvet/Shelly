using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>The script languages Shelly knows, by file extension: PowerShell (.ps1), Python (.py), Batch (.bat, .cmd) and
/// the extensions of the user's custom interpreters (1.3.0).</summary>
public static class ScriptKinds
{
    public const string Ps1 = ".ps1";
    public const string Py = ".py";
    public const string Bat = ".bat";
    public const string Cmd = ".cmd";
    /// <summary>The built-in extensions.</summary>
    public static readonly string[] Extensions = { Ps1, Py, Bat, Cmd };

    static volatile IReadOnlyList<CustomInterpreter> custom = Array.Empty<CustomInterpreter>();

    /// <summary>The custom interpreters in use (Settings > Environment). Only usable entries count; an extension taken twice
    /// belongs to the first entry.</summary>
    internal static IReadOnlyList<CustomInterpreter> Custom => custom;

    public static void SetCustom(IEnumerable<CustomInterpreter>? list)
    {
        var res = new List<CustomInterpreter>();
        var seen = new HashSet<string>();
        foreach (var c in list ?? Array.Empty<CustomInterpreter>())
        {
            var ext = c.Extensions.Where(e => !CustomInterpreters.Reserved.Contains(e) && seen.Add(e)).ToList();
            if (ext.Count == 0) continue;
            var copy = c.Clone(); copy.Extensions = ext;
            res.Add(copy);
        }
        custom = res;
    }

    /// <summary>Every extension Shelly lists now: the built-in ones and those of the custom interpreters.</summary>
    public static IEnumerable<string> AllExtensions => Extensions.Concat(custom.SelectMany(c => c.Extensions));

    /// <summary>The script extension of a path or name (".ps1", ".py", ".bat", ".cmd" or a custom interpreter's, lower
    /// case), or null for any other file.</summary>
    public static string? Ext(string? pathOrName)
    {
        if (string.IsNullOrEmpty(pathOrName)) return null;
        foreach (var e in Extensions)
            if (pathOrName.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return e;
        string? best = null;
        foreach (var c in custom)
            foreach (var e in c.Extensions)
                if (pathOrName.EndsWith(e, StringComparison.OrdinalIgnoreCase) && (best == null || e.Length > best.Length)) best = e;
        return best;
    }

    public static bool IsScript(string? pathOrName) => Ext(pathOrName) != null;
    public static bool IsPython(string? pathOrName) => Ext(pathOrName) == Py;
    public static bool IsBatch(string? pathOrName) => Ext(pathOrName) is Bat or Cmd;

    /// <summary>The custom interpreter of a script, or null.</summary>
    public static CustomInterpreter? CustomFor(string? pathOrName)
    {
        string? e = Ext(pathOrName);
        if (e == null || Extensions.Contains(e)) return null;
        return custom.FirstOrDefault(c => c.Extensions.Contains(e));
    }

    /// <summary>The language of a script: <see cref="ScriptLangs"/> ("ps" for anything unknown).</summary>
    public static string Lang(string? pathOrName) => Ext(pathOrName) switch
    {
        Py => ScriptLangs.Python, Bat or Cmd => ScriptLangs.Batch, Ps1 or null => ScriptLangs.PowerShell, _ => ScriptLangs.Custom
    };

    /// <summary>What starts the script: "python" for .py, "cmd" for .bat / .cmd, "custom" for a custom interpreter's,
    /// "powershell" for everything else.</summary>
    internal static string RunnerOf(string? pathOrName) => Lang(pathOrName) switch
    {
        ScriptLangs.Python => "python", ScriptLangs.Batch => "cmd", ScriptLangs.Custom => "custom", _ => "powershell"
    };

    /// <summary>The name without a script extension; <paramref name="ext"/> is that extension (null when there was none).</summary>
    public static string StripExt(string name, out string? ext)
    {
        ext = Ext(name);
        return ext == null ? name : name.Substring(0, name.Length - ext.Length);
    }

    /// <summary>A .py file that is not listed: private modules by the Python convention (a name starting with "_", such as
    /// __init__.py), which are imported by scripts and not run on their own.</summary>
    public static bool IsHiddenPython(string fileName) => IsPython(fileName) && fileName.StartsWith("_");
}

/// <summary>What a Python probe says about an interpreter: its version ("3.12.1") and sys.executable.</summary>
public sealed record PythonProbe(string Version, string Executable);

/// <summary>A Python interpreter that works: the exe and the arguments that come before the script ("-3" for the py launcher
/// when its python.exe cannot be started directly).</summary>
public sealed class PythonInfo
{
    public string Path = "";
    public string[] Prefix = Array.Empty<string>();
    public string Version = "";
    /// <summary>setting | py | path | common | venv</summary>
    public string Source = "";
    /// <summary>The virtual environment this interpreter belongs to (Source "venv"), else null.</summary>
    public PythonVenv? Venv;
    public string Label => "Python " + Version;
    /// <summary>For the run header: "Python 3.12.1", or ".venv · Python 3.12" for a virtual environment.</summary>
    public string RunLabel => Venv == null ? Label
        : Venv.Name + " · Python" + (PythonSummary.ShortVersion(Version) is { Length: > 0 } v ? " " + v : "");

    /// <summary>The interpreter of a (working) virtual environment.</summary>
    public static PythonInfo FromVenv(PythonVenv v) => new() { Path = v.Python, Version = v.Version, Source = "venv", Venv = v };
}

/// <summary>A virtual environment found next to a script: its folder (.venv, venv or env with pyvenv.cfg), the interpreter
/// in it (Scripts\python.exe; bin/python on Linux/macOS) and the version from pyvenv.cfg. <see cref="Broken"/>: pyvenv.cfg
/// is there but the interpreter is not (a venv copied from another PC, a half-deleted one): an error, never a silent switch
/// to another Python.</summary>
public sealed record PythonVenv(string Root, string Python, string Version, bool Broken)
{
    /// <summary>The folder name: ".venv", "venv" or "env".</summary>
    public string Name => Tail(Root.TrimEnd('\\', '/'));
    /// <summary>The folder of the interpreter (Scripts or bin): goes first on PATH, like activate does.</summary>
    public string BinDir => Python.LastIndexOfAny(new[] { '\\', '/' }) is var i and > 0 ? Python[..i] : System.IO.Path.Combine(Root, "Scripts");

    // Both separators on every OS (the paths are Windows ones; the tests also run elsewhere)
    static string Tail(string p) => p.LastIndexOfAny(new[] { '\\', '/' }) is var i and >= 0 ? p[(i + 1)..] : p;
}

/// <summary>What the venv search needs from the file system (a fake one in the tests).</summary>
public interface IVenvFileSystem
{
    bool FileExists(string path);
    /// <summary>The folder is a symbolic link or a junction: never followed.</summary>
    bool IsLink(string dir);
    string? ReadText(string path);
}

public sealed class SystemVenvFileSystem : IVenvFileSystem
{
    public static readonly SystemVenvFileSystem Instance = new();
    public bool FileExists(string path) { try { return File.Exists(path); } catch { return false; } }
    public bool IsLink(string dir)
    {
        try
        {
            var di = new DirectoryInfo(dir);
            return di.Exists && ((di.Attributes & FileAttributes.ReparsePoint) != 0 || di.LinkTarget != null);
        }
        catch { return true; }
    }
    public string? ReadText(string path)
    {
        try { var fi = new FileInfo(path); return fi.Exists && fi.Length < 64 * 1024 ? File.ReadAllText(path) : null; }
        catch { return null; }
    }
}

/// <summary>Finds the virtual environment of a script: .venv, venv or env in the script's folder, then in each parent up to
/// the root of the Shelly scripts folder or of the external folder (never above it). The nearest one wins; inside one
/// folder the order is .venv, venv, env.</summary>
public static class VenvLocator
{
    public static readonly string[] Names = { ".venv", "venv", "env" };

    public static PythonVenv? Find(string scriptPath, string? root, IVenvFileSystem fs)
    {
        string? dir = System.IO.Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrEmpty(dir)) return null;
        string? top = string.IsNullOrEmpty(root) ? null : Trim(root);
        // A script outside the root (should not happen): only its own folder
        bool walk = top != null && IsUnder(dir, top);
        for (int guard = 0; dir != null && guard < 64; guard++)
        {
            foreach (var n in Names)
            {
                var v = Probe(System.IO.Path.Combine(dir, n), fs);
                if (v != null) return v;
            }
            if (!walk || Same(dir, top!)) break;
            dir = System.IO.Path.GetDirectoryName(Trim(dir));
            if (dir == null || !IsUnder(dir, top!)) break;
        }
        return null;
    }

    /// <summary>One candidate folder: a venv when it has pyvenv.cfg and is not a link.</summary>
    public static PythonVenv? Probe(string venvDir, IVenvFileSystem fs)
    {
        string cfg = System.IO.Path.Combine(venvDir, "pyvenv.cfg");
        if (!fs.FileExists(cfg) || fs.IsLink(venvDir)) return null;
        string version = ParseVersion(fs.ReadText(cfg));
        foreach (var exe in new[] { System.IO.Path.Combine(venvDir, "Scripts", "python.exe"), System.IO.Path.Combine(venvDir, "bin", "python"), System.IO.Path.Combine(venvDir, "bin", "python3") })
            if (fs.FileExists(exe)) return new PythonVenv(venvDir, exe, version, false);
        return new PythonVenv(venvDir, System.IO.Path.Combine(venvDir, "Scripts", "python.exe"), version, true);
    }

    /// <summary>"version = 3.12.1" (venv) or "version_info = 3.12.1.final.0" (virtualenv, uv) from pyvenv.cfg; "" when absent.</summary>
    public static string ParseVersion(string? cfg)
    {
        string? info = null;
        foreach (var raw in (cfg ?? "").Replace("\r", "").Split('\n'))
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            string k = raw[..eq].Trim().ToLowerInvariant(), v = raw[(eq + 1)..].Trim();
            if (k == "version" && v.Length > 0) return v;
            if (k == "version_info" && v.Length > 0) info = v;
        }
        if (info == null) return "";
        var m = Regex.Match(info, @"^\d+\.\d+(\.\d+)?");
        return m.Success ? m.Value : info;
    }

    static string Trim(string p) => p.Length > 3 ? p.TrimEnd('\\', '/') : p;
    static bool Same(string a, string b) => string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
    static bool IsUnder(string dir, string root)
    {
        string d = Trim(dir), r = Trim(root);
        return Same(d, r) || (d.Length > r.Length && d.StartsWith(r, StringComparison.OrdinalIgnoreCase) && (d[r.Length] is '\\' or '/' || r.EndsWith('\\') || r.EndsWith('/')));
    }
}

/// <summary>Everything the interpreter search needs from the machine, so the search itself can be tested on any OS.</summary>
public interface IPythonEnvironment
{
    bool FileExists(string path);
    string? GetVariable(string name);
    /// <summary>The PATH folders: this process first, then (on Windows) the machine and user PATH from the registry.</summary>
    IEnumerable<string> PathDirectories();
    IEnumerable<string> GetDirectories(string parent, string pattern);
    /// <summary>Starts <paramref name="exe"/> with <paramref name="prefix"/> and a short -c program; null when it fails, times out or is not Python 3.</summary>
    PythonProbe? Probe(string exe, IReadOnlyList<string> prefix);
}

/// <summary>Finds Python 3: the path set in Settings, else the py launcher (py -3), python.exe on PATH (the Microsoft Store
/// placeholder in WindowsApps is skipped unless it really starts Python) and the usual install folders.</summary>
public static class PythonLocator
{
    public sealed record Candidate(string Exe, string[] Prefix, string Source);

    public const string ProbeCode = "import sys;print(sys.version.split()[0]);print(sys.executable)";

    public static bool IsWindowsApps(string path) => path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The places to try, in order (duplicates removed, the configured path is not part of this).</summary>
    public static List<Candidate> Candidates(IPythonEnvironment env)
    {
        var res = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string exe, string[] prefix, string source)
        {
            if (exe.Length == 0 || !seen.Add(exe + "|" + string.Join(" ", prefix))) return;
            if (env.FileExists(exe)) res.Add(new Candidate(exe, prefix, source));
        }
        var dirs = env.PathDirectories().Select(d => d.Trim().Trim('"')).Where(d => d.Length > 0).ToList();
        // 1. The py launcher: on PATH, in the Windows folder (all-users install) or in the per-user launcher folder
        foreach (var d in dirs) Add(Combine(d, "py.exe"), new[] { "-3" }, "py");
        string win = env.GetVariable("WINDIR") ?? env.GetVariable("SystemRoot") ?? "";
        if (win.Length > 0) Add(Combine(win, "py.exe"), new[] { "-3" }, "py");
        string local = env.GetVariable("LOCALAPPDATA") ?? "";
        if (local.Length > 0) Add(Combine(local, @"Programs\Python\Launcher\py.exe"), new[] { "-3" }, "py");
        // 2. python.exe on PATH; the WindowsApps alias goes last, it is often only the Store placeholder
        foreach (var d in dirs.Where(d => !IsWindowsApps(d + "\\"))) Add(Combine(d, "python.exe"), Array.Empty<string>(), "path");
        foreach (var d in dirs.Where(d => IsWindowsApps(d + "\\"))) Add(Combine(d, "python.exe"), Array.Empty<string>(), "path");
        // 3. The usual install folders, newest version first
        var roots = new List<string>();
        if (local.Length > 0) roots.Add(Combine(local, @"Programs\Python"));
        foreach (var v in new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)" })
        {
            string pf = env.GetVariable(v) ?? "";
            if (pf.Length > 0) roots.Add(pf);
        }
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> found;
            try { found = env.GetDirectories(root, "Python3*").ToList(); } catch { continue; }
            foreach (var d in found.OrderByDescending(MinorOf).ThenBy(d => d, StringComparer.OrdinalIgnoreCase))
                Add(Combine(d, "python.exe"), Array.Empty<string>(), "common");
        }
        return res;
    }

    /// <summary>"...\Python312" -> 12, "...\Python39-32" -> 9 (the folder names of python.org installs).</summary>
    public static int MinorOf(string dir)
    {
        string name = dir.TrimEnd('\\', '/');
        int i = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (i >= 0) name = name.Substring(i + 1);
        var m = Regex.Match(name, @"^Python3(\d+)", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : -1;
    }

    /// <summary>Windows path join that works the same on every OS (the search is about Windows paths).</summary>
    public static string Combine(string dir, string rel) => dir.TrimEnd('\\', '/') + "\\" + rel;

    /// <summary>The interpreter to use. <paramref name="configured"/> (Settings) wins and is the only one tried when it is set;
    /// <paramref name="error"/> is a string key: pyBadPath (the set path does not start Python 3) or pyNotFound.</summary>
    public static PythonInfo? Find(string? configured, IPythonEnvironment env, out string error)
    {
        error = "";
        string set = (configured ?? "").Trim().Trim('"');
        if (set.Length > 0)
        {
            var info = env.FileExists(set) ? Try(new Candidate(set, Array.Empty<string>(), "setting"), env) : null;
            if (info == null) error = "pyBadPath";
            return info;
        }
        foreach (var c in Candidates(env))
        {
            var info = Try(c, env);
            if (info != null) return info;
        }
        error = "pyNotFound";
        return null;
    }

    static PythonInfo? Try(Candidate c, IPythonEnvironment env)
    {
        PythonProbe? p;
        try { p = env.Probe(c.Exe, c.Prefix); } catch { p = null; }
        if (p == null || !IsPython3(p.Version)) return null;
        // Started through the py launcher or an alias: use the real python.exe when it can be started directly
        string exe = c.Exe;
        string[] prefix = c.Prefix;
        if (c.Source != "setting" && p.Executable.Length > 0 && !p.Executable.Contains(@"\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase) && env.FileExists(p.Executable))
        {
            exe = p.Executable;
            prefix = Array.Empty<string>();
        }
        return new PythonInfo { Path = exe, Prefix = prefix, Version = p.Version, Source = c.Source };
    }

    public static bool IsPython3(string version) => Regex.IsMatch(version ?? "", @"^3\.\d+");

    /// <summary>The probe output: the version on the first line, sys.executable on the second. Null when it is not that.</summary>
    public static PythonProbe? ParseProbe(string output)
    {
        var lines = (output ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('\uFEFF')).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0 || !Regex.IsMatch(lines[0], @"^\d+\.\d+(\.\d+)?\S*$")) return null;
        return new PythonProbe(lines[0], lines.Count > 1 ? lines[1] : "");
    }
}

/// <summary>The real machine for <see cref="PythonLocator"/>.</summary>
public sealed class SystemPythonEnvironment : IPythonEnvironment
{
    public static readonly SystemPythonEnvironment Instance = new();
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(6);

    public bool FileExists(string path) => EngineLocator.FileExists(path);
    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);

    public IEnumerable<string> PathDirectories()
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) yield return Environment.ExpandEnvironmentVariables(d);
        if (!OperatingSystem.IsWindows()) yield break;
        // The PATH of this process is not updated after an install: the machine and user PATH from the registry too
        foreach (var scope in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            string path = "";
            try { path = Environment.GetEnvironmentVariable("Path", scope) ?? ""; } catch { }
            foreach (var d in path.Split(';')) yield return Environment.ExpandEnvironmentVariables(d);
        }
    }

    public IEnumerable<string> GetDirectories(string parent, string pattern) =>
        Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : Array.Empty<string>();

    public PythonProbe? Probe(string exe, IReadOnlyList<string> prefix)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in prefix) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(PythonLocator.ProbeCode);
        foreach (var (k, v) in PythonRun.EnvVars) psi.Environment[k] = v;
        using var p = Process.Start(psi);
        if (p == null) return null;
        try { p.StandardInput.Close(); } catch { }
        var outTask = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)ProbeTimeout.TotalMilliseconds)) { try { p.Kill(true); } catch { } return null; }
        if (p.ExitCode != 0) return null;
        return PythonLocator.ParseProbe(outTask.Wait(2000) ? outTask.Result : "");
    }
}

/// <summary>How a .py script is started: unbuffered (-u) in UTF-8, the user's free arguments split like a Windows command line.</summary>
public static class PythonRun
{
    /// <summary>Key of the free argument text in the Run settings values and presets.</summary>
    public const string ArgsKey = "*args";
    public const int MaxArgsLength = 8000;

    /// <summary>Unbuffered UTF-8 output, so Cyrillic text and progress lines arrive as they are printed.</summary>
    public static readonly IReadOnlyList<(string Key, string Value)> EnvVars = new[]
    {
        ("PYTHONUNBUFFERED", "1"), ("PYTHONIOENCODING", "utf-8"), ("PYTHONUTF8", "1")
    };

    /// <summary>The environment of a run: <see cref="EnvVars"/>, and for a virtual environment what activate does:
    /// VIRTUAL_ENV, its Scripts (bin) folder first on PATH, and no PYTHONHOME (a null value removes the variable).</summary>
    public static List<(string Key, string? Value)> EnvFor(PythonInfo py, string? currentPath)
    {
        var list = EnvVars.Select(e => (e.Key, (string?)e.Value)).ToList();
        if (py.Venv is { } v)
        {
            list.Add(("VIRTUAL_ENV", v.Root));
            string sep = v.BinDir.Contains('\\') ? ";" : System.IO.Path.PathSeparator.ToString();
            list.Add(("PATH", string.IsNullOrEmpty(currentPath) ? v.BinDir : v.BinDir + sep + currentPath));
            list.Add(("PYTHONHOME", null));
        }
        return list;
    }

    /// <summary>Free argument text is one line without control characters, at most <see cref="MaxArgsLength"/> characters.
    /// The history file is plain text anyone can edit: nothing else is used.</summary>
    public static bool IsValidArgs(string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (text.Length > MaxArgsLength) return false;
        foreach (char ch in text) if (char.IsControl(ch) && ch != '\t') return false;
        return true;
    }

    /// <summary>The free argument text from the Run settings values ("" when none).</summary>
    public static string ArgsFrom(IDictionary<string, object>? values) =>
        values != null && values.TryGetValue(ArgsKey, out var v) ? (Convert.ToString(v) ?? "").Trim() : "";

    /// <summary>The argument text as kept in the history and shown in the run header: " text" or "".</summary>
    public static string Display(string? text) { string t = (text ?? "").Trim(); return t.Length > 0 ? " " + t : ""; }

    /// <summary>Splits a command line into arguments by the rules of CommandLineToArgvW (and of Python on Windows): spaces
    /// separate, double quotes group, a backslash escapes a quote, 2n backslashes before a quote are n backslashes.</summary>
    public static List<string> Split(string? text)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(text)) return res;
        var cur = new StringBuilder();
        bool inQuotes = false, has = false;
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == '\\')
            {
                int n = 0;
                while (i < text.Length && text[i] == '\\') { n++; i++; }
                if (i < text.Length && text[i] == '"')
                {
                    cur.Append('\\', n / 2);
                    if (n % 2 == 1) { cur.Append('"'); i++; }
                }
                else cur.Append('\\', n);
                has = true;
                continue;
            }
            if (ch == '"')
            {
                // "" inside quotes is a literal quote
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"') { cur.Append('"'); i += 2; has = true; continue; }
                inQuotes = !inQuotes; has = true; i++;
                continue;
            }
            if ((ch == ' ' || ch == '\t') && !inQuotes)
            {
                if (has) { res.Add(cur.ToString()); cur.Clear(); has = false; }
                i++;
                continue;
            }
            cur.Append(ch); has = true; i++;
        }
        if (has) res.Add(cur.ToString());
        return res;
    }

    /// <summary>Everything after the exe for an inline run: [prefix] -u "script.py" args.</summary>
    public static List<string> InlineArgList(PythonInfo py, string scriptPath, string? userArgs)
    {
        var list = new List<string>(py.Prefix) { "-u", scriptPath };
        list.AddRange(Split(userArgs));
        return list;
    }

    /// <summary>The same as one command line string (each argument quoted by the Windows rules).</summary>
    public static string InlineArgs(PythonInfo py, string scriptPath, string? userArgs) =>
        string.Join(" ", InlineArgList(py, scriptPath, userArgs).Select(Quoting.NativeArg));

    /// <summary>Arguments for powershell.exe (or pwsh.exe) that runs the script in its own console window and keeps it open:
    /// the working folder is the script's folder, the output is UTF-8. With <paramref name="expectedHash"/> (administrator runs)
    /// the script starts only if the file still has that SHA-256 after the UAC prompt. doneFile: the end marker (1.2.0) for the
    /// history and the notification; the window stays open. 1.3.1: built by <see cref="WindowHost.Args"/> like batch and
    /// custom scripts (one copy of the hash check), the same command line as before.</summary>
    public static string WindowArgs(PythonInfo py, string scriptPath, string? userArgs, string? expectedHash, string doneFile, string? logFile = null)
    {
        string dir = System.IO.Path.GetDirectoryName(scriptPath) ?? "";
        string call = "& " + Quoting.PsString(py.Path) + " " + string.Join(" ", InlineArgList(py, scriptPath, userArgs).Select(Quoting.PsString));
        string env = string.Concat(EnvVars.Select(e => "$env:" + e.Key + "=" + Quoting.PsString(e.Value) + "; "));
        if (py.Venv is { } v)
            env += "$env:VIRTUAL_ENV=" + Quoting.PsString(v.Root) + "; $env:PATH=" + Quoting.PsString(v.BinDir + ";") + " + $env:PATH; " +
                   "Remove-Item Env:PYTHONHOME -ErrorAction SilentlyContinue; ";
        // Which interpreter runs it, first line of the window ("Python 3.12.1", ".venv · Python 3.12")
        return WindowHost.Args(dir, py.RunLabel, call, scriptPath, expectedHash, doneFile, env, logFile);
    }

    /// <summary>A new script: the description comment and one print line.</summary>
    public static string Template(string name) =>
        Loc.T("templateDesc") + "\r\n\r\nprint(" + PyString(Loc.F("templateHello", name)) + ")\r\n";

    /// <summary>Double-quoted Python string literal.</summary>
    public static string PyString(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    /// <summary>The description of a Python script: the first line of the module docstring, or the first leading # comment
    /// (not the #! line, a coding line or a # Shelly-…: directive), among the first lines.</summary>
    public static string Description(string text)
    {
        var lines = (text ?? "").Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length && i < FallbackParser.MaxHeaderLines; i++)
        {
            string t = lines[i].Trim().TrimStart('\uFEFF');
            if (t.Length == 0) continue;
            if (t.StartsWith("#"))
            {
                if (t.StartsWith("#!") || Regex.IsMatch(t, @"^#.*coding[:=]") || ScriptHeader.IsDirective(t)) continue;
                string d = FallbackParser.StripDescriptionPrefix(t.TrimStart('#').Trim());
                if (d.Length > 0) return d;
                continue;
            }
            var m = Regex.Match(t, "^[rRuU]?(\"\"\"|''')(.*)$");
            if (!m.Success) break;
            string q = m.Groups[1].Value, rest = m.Groups[2].Value;
            for (int j = i; j < lines.Length && j < i + FallbackParser.MaxHeaderLines; j++)
            {
                string cur = j == i ? rest : lines[j].Trim();
                int end = cur.IndexOf(q, StringComparison.Ordinal);
                string part = (end >= 0 ? cur.Substring(0, end) : cur).Trim();
                if (part.Length > 0) return FallbackParser.StripDescriptionPrefix(part);
                if (end >= 0) break;
            }
            break;
        }
        return "";
    }
}

/// <summary>The one-line Python summary of Settings > Engines: "Python 3.12 · custom path", "Python 3.13 · auto-detect",
/// "Python not found", "Custom path unavailable" (or "Looking for Python…" while the search runs). The full path goes to
/// the tooltip; Reset is offered only when a custom path is set.</summary>
public sealed record PythonSummary(string Text, string Tip, string Level, bool CanReset)
{
    /// <summary>"3.12.4" -> "3.12", "3.13.0rc1" -> "3.13"; anything else as it is.</summary>
    public static string ShortVersion(string? version)
    {
        var m = Regex.Match(version ?? "", @"^\s*(\d+)\.(\d+)");
        return m.Success ? m.Groups[1].Value + "." + m.Groups[2].Value : (version ?? "").Trim();
    }

    /// <param name="configured">The pythonPath setting ("" = auto-detect).</param>
    /// <param name="py">The interpreter found, if any.</param>
    /// <param name="error">The search error: "pyBadPath", "pyNotFound" or "".</param>
    /// <param name="searching">The search has not finished yet.</param>
    public static PythonSummary For(string? configured, PythonInfo? py, string? error, bool searching)
    {
        string path = (configured ?? "").Trim();
        bool custom = path.Length > 0;
        if (searching) return new(Loc.T("pyChecking"), custom ? path : Loc.T("pySumAutoTip"), "info", custom);
        if (py != null)
        {
            string ver = ShortVersion(py.Version);
            string head = ver.Length > 0 ? "Python " + ver : "Python";
            string how = py.Source == "setting" ? Loc.T("pySumCustom") : Loc.T("pySumAuto");
            return new(head + " · " + how, py.Path, "ok", custom);
        }
        if (custom || error == "pyBadPath") return new(Loc.T("pySumBad"), path, "warn", custom);
        return new(Loc.T("pySumNone"), Loc.T("pySumAutoTip"), "warn", false);
    }
}
