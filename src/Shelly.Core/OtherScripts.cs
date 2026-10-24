using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>The script languages (1.3.0): PowerShell, Python, Batch (.bat / .cmd, run by cmd.exe) and the user's own
/// interpreters (Settings > Environment > Custom interpreters).</summary>
public static class ScriptLangs
{
    public const string PowerShell = "ps", Python = "py", Batch = "bat", Custom = "custom";

    /// <summary>"By type" order: PowerShell, Python, Batch, then the custom interpreters.</summary>
    public static int Order(string lang) => lang switch { PowerShell => 0, Python => 1, Batch => 2, Custom => 3, _ => 4 };

    /// <summary>The engine value kept in the history for a run of this language.</summary>
    internal static string EngineOf(string lang) => lang switch
    {
        Python => EngineInfo.Python, Batch => EngineInfo.Batch, Custom => EngineInfo.Custom, _ => "5.1"
    };

    /// <summary>The free arguments line of a non-PowerShell script: null when it can be used, else the Loc key of the
    /// problem (its {0} is the maximum length). Batch arguments go through cmd.exe, so a few characters can't be passed.</summary>
    public static string? ArgsError(string lang, string? text, CustomInterpreter? custom = null)
    {
        if (!PythonRun.IsValidArgs(text)) return "rsPyArgsBad";
        if (lang == Batch || (lang == Custom && custom != null && BatchRun.IsBatchFile(custom.Program))) return BatchRun.ArgsError(text);
        return null;
    }
}

/// <summary>A program the user added for a file extension (Settings > Environment > Custom interpreters, 1.3.0), kept in
/// launcher-settings.json "customInterpreters" as one JSON line each. Nothing is detected or checked for safety: the user
/// says which program runs which files and how.</summary>
public sealed class CustomInterpreter
{
    public const string DefaultTemplate = "{script} {args}";
    public const int MaxName = 40;
    public string Id = "";
    public string Name = "";
    /// <summary>Lower case, with the dot: ".js", ".rb".</summary>
    public List<string> Extensions = new();
    /// <summary>A full path, or a program name looked up on PATH ("node").</summary>
    public string Program = "";
    /// <summary>The arguments after the program: {script} (the full path), {args} (the Arguments box, split like a
    /// command line), {dir} (the script's folder).</summary>
    public string Template = DefaultTemplate;
    /// <summary>The working folder is the script's folder (else the program's).</summary>
    public bool ScriptDir = true;

    public string ExtText => string.Join(" ", Extensions);
    /// <summary>The mark on the script rows: the name, short.</summary>
    public string Mark => Name.Length <= 12 ? Name : Name.Substring(0, 11).TrimEnd() + "…";

    public CustomInterpreter Clone() => new() { Id = Id, Name = Name, Extensions = new(Extensions), Program = Program, Template = Template, ScriptDir = ScriptDir };

    public string ToLine() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["id"] = Id, ["name"] = Name, ["ext"] = Extensions, ["program"] = Program, ["args"] = Template, ["scriptDir"] = ScriptDir
    }, JsonOpts.Relaxed);

    public static CustomInterpreter? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var c = new CustomInterpreter { Id = S("id"), Name = S("name").Trim(), Program = S("program").Trim(), Template = S("args") };
            if (r.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.String) c.Template = DefaultTemplate;
            if (r.TryGetProperty("scriptDir", out var sd) && sd.ValueKind == JsonValueKind.False) c.ScriptDir = false;
            if (r.TryGetProperty("ext", out var e))
            {
                var items = e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "")
                          : e.ValueKind == JsonValueKind.String ? new[] { e.GetString() ?? "" } : Array.Empty<string>();
                c.Extensions = CustomInterpreters.ParseExtensions(string.Join(" ", items), out _) ?? new();
            }
            if (!Regex.IsMatch(c.Id, "^[0-9a-f]{8}$")) return null;
            return c;
        }
        catch { return null; }
    }
}

public static class CustomInterpreters
{
    public const int MaxEntries = 20;
    /// <summary>Extensions Shelly or Windows already runs in their own way: never a custom interpreter's.</summary>
    public static readonly string[] Reserved =
    {
        ".ps1", ".psm1", ".psd1", ".ps1xml", ".py", ".bat", ".cmd", ".exe", ".com", ".lnk", ".scr", ".pif", ".msi", ".dll", ".sys", ".cpl", ".url"
    };

    public static List<CustomInterpreter> Load(Settings s) => s.CustomInterpreters.Select(CustomInterpreter.Parse).Where(c => c != null).Select(c => c!).ToList();

    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>Adds or replaces (by id).</summary>
    public static void Put(Settings s, CustomInterpreter c)
    {
        var list = Load(s);
        int i = list.FindIndex(x => x.Id == c.Id);
        if (i >= 0) list[i] = c; else list.Add(c);
        s.CustomInterpreters = list.Select(x => x.ToLine()).ToList();
    }

    public static void Remove(Settings s, string id) =>
        s.CustomInterpreters = Load(s).Where(x => x.Id != id).Select(x => x.ToLine()).ToList();

    /// <summary>".js .mjs", "js, mjs", "*.rb" -> [".js", ".mjs"]; null when one of them is not a usable extension
    /// (<paramref name="bad"/> names it). Duplicates are dropped; the order is kept.</summary>
    public static List<string>? ParseExtensions(string? text, out string bad)
    {
        bad = "";
        var res = new List<string>();
        foreach (var raw in Regex.Split(text ?? "", @"[\s,;]+"))
        {
            string e = raw.Trim().TrimStart('*').ToLowerInvariant();
            if (e.Length == 0) continue;
            if (!e.StartsWith(".")) e = "." + e;
            if (!Regex.IsMatch(e, @"^\.[a-z0-9][a-z0-9_+\-]{0,14}$")) { bad = raw.Trim(); return null; }
            if (!res.Contains(e)) res.Add(e);
        }
        return res;
    }

    /// <summary>The program to start: a full path that exists, or a bare name found on PATH (with PATHEXT-like
    /// extensions when it has none). Null when there is none.</summary>
    public static string? ResolveProgram(string? program, Func<string, bool> exists, IEnumerable<string> pathDirs)
    {
        string p = (program ?? "").Trim().Trim('"').Trim();
        if (p.Length == 0) return null;
        bool hasDir = p.IndexOfAny(new[] { '\\', '/' }) >= 0 || Regex.IsMatch(p, "^[A-Za-z]:");
        if (hasDir) return exists(p) ? p : null;
        bool hasExt = Regex.IsMatch(p, @"\.[A-Za-z0-9]{1,4}$");
        foreach (var d in pathDirs)
        {
            string dir = (d ?? "").Trim().Trim('"');
            if (dir.Length == 0) continue;
            foreach (var ext in hasExt ? new[] { "" } : new[] { ".exe", ".cmd", ".bat", ".com" })
            {
                string full = dir.TrimEnd('\\', '/') + "\\" + p + ext;
                if (exists(full)) return full;
            }
        }
        return null;
    }

    /// <summary>The template's problem, or null: {script} is needed, only {script}, {args} and {dir} are known.</summary>
    public static string? TemplateError(string? template)
    {
        string t = string.IsNullOrWhiteSpace(template) ? CustomInterpreter.DefaultTemplate : template;
        if (t.Length > 1000 || t.Any(ch => char.IsControl(ch) && ch != '\t')) return "ciErrTemplate";
        foreach (Match m in Regex.Matches(t, @"\{(\w*)\}"))
            if (m.Groups[1].Value is not ("script" or "args" or "dir")) return "ciErrToken";
        if (!t.Contains("{script}")) return "ciErrNoScript";
        return null;
    }

    /// <summary>Checks an entry before it is saved; the Loc key of the first problem (its {0} is the detail), or null.
    /// <paramref name="others"/>: the other entries (their extensions are taken).</summary>
    public static (string Key, string Detail)? Validate(CustomInterpreter c, IEnumerable<CustomInterpreter> others, Func<string, bool> exists, IEnumerable<string> pathDirs)
    {
        if (c.Name.Trim().Length == 0) return ("ciErrName", "");
        if (c.Name.Length > CustomInterpreter.MaxName || c.Name.Any(char.IsControl)) return ("ciErrName", "");
        if (c.Extensions.Count == 0) return ("ciErrExt", "");
        foreach (var e in c.Extensions)
        {
            if (Reserved.Contains(e)) return ("ciErrExtBuiltIn", e);
            var taken = others.FirstOrDefault(o => o.Id != c.Id && o.Extensions.Contains(e));
            if (taken != null) return ("ciErrExtTaken", e + " · " + taken.Name);
        }
        if (c.Program.Trim().Length == 0) return ("ciErrProgram", "");
        if (ResolveProgram(c.Program, exists, pathDirs) == null) return ("ciErrProgramMissing", c.Program.Trim());
        if (TemplateError(c.Template) is { } t) return (t, "");
        return null;
    }
}

/// <summary>How a script of a custom interpreter is started (1.3.0): the template is split into arguments like a
/// Windows command line, then {script} / {dir} are put into their argument as one value (spaces in paths need no
/// quotes) and a lone {args} becomes the Arguments box split into separate arguments. Every argument is quoted again
/// for the program (CommandLineToArgvW rules), so nothing typed in the box can start another command. A .bat / .cmd
/// program goes through cmd.exe with the batch rules.</summary>
public static class CustomRun
{
    public static List<string> ArgList(string? template, string scriptPath, string? userArgs)
    {
        string t = string.IsNullOrWhiteSpace(template) ? CustomInterpreter.DefaultTemplate : template;
        string dir = DirOf(scriptPath);
        string args = (userArgs ?? "").Trim();
        var res = new List<string>();
        foreach (var tok in PythonRun.Split(t))
        {
            if (tok == "{args}") { res.AddRange(PythonRun.Split(args)); continue; }
            res.Add(Regex.Replace(tok, @"\{(script|dir|args)\}", m => m.Groups[1].Value switch { "script" => scriptPath, "dir" => dir, _ => args }));
        }
        return res;
    }

    /// <summary>The command line after the program for an inline run.</summary>
    public static string NativeArgs(IEnumerable<string> args) => string.Join(" ", args.Select(Quoting.NativeArg));

    /// <summary>What to start: the program and its arguments, or cmd.exe for a .bat / .cmd program. Error: a Loc key when
    /// the arguments can't be passed (batch rules).</summary>
    public static (string Exe, string Args, string? Error) Inline(CustomInterpreter c, string program, string scriptPath, string? userArgs, string comspec)
    {
        var list = ArgList(c.Template, scriptPath, userArgs);
        if (BatchRun.IsBatchFile(program))
        {
            string? err = BatchRun.PathError(program) ?? list.Select(BatchRun.ArgError).FirstOrDefault(e => e != null);
            return (comspec, BatchRun.InlineArgs(program, list, false), err);
        }
        return (program, NativeArgs(list), null);
    }

    /// <summary>The PowerShell host command line of a window run (see <see cref="WindowHost"/>).</summary>
    public static string WindowArgs(CustomInterpreter c, string program, string scriptPath, string? userArgs, string comspec, string workDir, string? expectedHash, string? doneFile, string? logFile = null)
    {
        var list = ArgList(c.Template, scriptPath, userArgs);
        string call = BatchRun.IsBatchFile(program)
            ? BatchRun.PsCall(comspec, BatchRun.Line(program, list, false))
            : "& " + Quoting.PsString(program) + string.Concat(list.Select(a => " " + Quoting.PsString(a)));
        return WindowHost.Args(workDir, c.Name, call, scriptPath, expectedHash, doneFile, "", logFile);
    }

    public static string WorkDir(CustomInterpreter c, string program, string scriptPath) =>
        c.ScriptDir ? DirOf(scriptPath) : DirOf(program) is { Length: > 0 } d ? d : DirOf(scriptPath);

    internal static string DirOf(string path) => path.LastIndexOfAny(new[] { '\\', '/' }) is var i and > 0 ? path.Substring(0, i) : "";
}

/// <summary>Batch files (.bat / .cmd, 1.3.0), run by cmd.exe: <c>cmd /d /s /c "chcp 65001 &gt;nul &amp; call "script" args"</c>.
/// /d skips the AutoRun commands of the registry, call returns the script's exit code (exit /b N, or the last
/// %ERRORLEVEL%). A script saved as UTF-8 (or plain ASCII) runs with the UTF-8 code page, so Cyrillic text it prints comes
/// out right; one saved in the old OEM code page runs as before (no chcp). Arguments go through cmd.exe: each one is quoted
/// when it has spaces or cmd characters (&amp; | &lt; &gt; ( ) = , ;); " % ^ can't be passed safely and are refused.</summary>
public static class BatchRun
{
    public static bool IsBatchFile(string? path) =>
        path != null && (path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));

    /// <summary>%ComSpec%, else %SystemRoot%\System32\cmd.exe.</summary>
    public static string ComSpec(Func<string, string?> getVar)
    {
        string c = (getVar("ComSpec") ?? "").Trim().Trim('"');
        if (c.Length > 0 && c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return c;
        string root = (getVar("SystemRoot") ?? getVar("windir") ?? "").Trim();
        return (root.Length > 0 ? root.TrimEnd('\\') : @"C:\Windows") + @"\System32\cmd.exe";
    }

    public static string ComSpec() => ComSpec(Environment.GetEnvironmentVariable);

    /// <summary>One argument cmd.exe can pass on unchanged: no " (it can't be escaped), no % (variables are expanded),
    /// no ^ (call doubles it), no control characters. null = fine, else "batArgsBad".</summary>
    public static string? ArgError(string a) =>
        a.Any(ch => ch is '"' or '%' or '^' || char.IsControl(ch)) ? "batArgsBad" : null;

    /// <summary>The Arguments box of a batch script: null when it can be passed, else the Loc key.</summary>
    public static string? ArgsError(string? text)
    {
        if (!PythonRun.IsValidArgs(text)) return "rsPyArgsBad";
        foreach (var a in PythonRun.Split(text)) if (ArgError(a) != null) return "batArgsBad";
        // A quote typed as \" or "" inside quotes ends up in an argument: the split result says so above
        return null;
    }

    /// <summary>A script path cmd.exe can't call safely (% or ^ or ").</summary>
    public static string? PathError(string path) => path.IndexOfAny(new[] { '%', '^', '"' }) >= 0 ? "batPathBad" : null;

    /// <summary>An argument as cmd.exe reads it: quoted when it is empty or has spaces or cmd characters.</summary>
    public static string Quote(string a) => a.Length == 0 || Regex.IsMatch(a, @"[\s&|<>()=,;!]") ? "\"" + a + "\"" : a;

    /// <summary>The command cmd.exe runs: [chcp 65001 &gt;nul &amp; ]call "script" args.</summary>
    public static string Line(string scriptPath, IEnumerable<string> args, bool utf8) =>
        (utf8 ? "chcp 65001 >nul & " : "") + "call \"" + scriptPath + "\"" + string.Concat(args.Select(a => " " + Quote(a)));

    /// <summary>cmd.exe's command line for an inline run (/s: the outer quotes are removed and the rest is taken as it is).</summary>
    public static string InlineArgs(string scriptPath, IEnumerable<string> args, bool utf8) => "/d /s /c \"" + Line(scriptPath, args, utf8) + "\"";

    public static string InlineArgs(string scriptPath, string? userArgs, bool utf8) => InlineArgs(scriptPath, PythonRun.Split(userArgs), utf8);

    /// <summary>PowerShell that starts cmd.exe with this line (the PowerShell host of a window run). Legacy argument passing,
    /// so PowerShell 7.3+ hands the line over as it is, like Windows PowerShell does.</summary>
    public static string PsCall(string comspec, string line) =>
        "$PSNativeCommandArgumentPassing='Legacy'; & " + Quoting.PsString(comspec) + " /d /s /c " + Quoting.PsString(line);

    public static string WindowArgs(string comspec, string scriptPath, string? userArgs, bool utf8, string? expectedHash, string? doneFile, string? logFile = null) =>
        WindowHost.Args(CustomRun.DirOf(scriptPath), "cmd.exe" + (utf8 ? " · UTF-8" : ""), PsCall(comspec, Line(scriptPath, PythonRun.Split(userArgs), utf8)), scriptPath, expectedHash, doneFile, "", logFile);

    /// <summary>Run with the UTF-8 code page: the file is valid UTF-8 (plain ASCII is too). A file in the OEM or ANSI code
    /// page is not, and runs in the console's own code page as it always did.</summary>
    public static bool IsUtf8Source(byte[] bytes)
    {
        try { _ = new UTF8Encoding(false, true).GetString(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    public static bool HasBom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    /// <summary>The text of a batch file as cmd.exe reads it: UTF-8 when it is valid UTF-8, else the OEM code page.</summary>
    public static string Decode(byte[] bytes)
    {
        if (HasBom(bytes)) return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        return IsUtf8Source(bytes) ? Encoding.UTF8.GetString(bytes) : ConsoleText.Oem().GetString(bytes);
    }

    /// <summary>A new batch script: the description comment and one echo line (UTF-8, so it runs with chcp 65001).</summary>
    public static string Template(string name) =>
        "@echo off\r\nrem " + Loc.T("templateDesc").TrimStart('#', ' ') + "\r\n\r\necho " + Loc.F("templateHello", name).Replace("%", "%%").Replace("^", "^^").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|") + "\r\n";
}

/// <summary>The PowerShell console a batch or custom script runs in for a window run (like Python's): the working folder,
/// a grey first line naming what runs it, the exit code at the end, the window stays open. With
/// <paramref name="expectedHash"/> (administrator runs) the script starts only if the file still has that SHA-256 after
/// the UAC prompt; <paramref name="doneFile"/> gets the exit code when the script ends (1.2.0 end marker).</summary>
public static class WindowHost
{
    /// <summary>The start of every window run's command line (PowerShell scripts, Python, batch, custom interpreters).</summary>
    public const string Prefix = "-NoProfile -NoExit -ExecutionPolicy Bypass -Command ";

    /// <summary>prelude: PowerShell set after the working folder, before the label line (Python: its environment variables).</summary>
    public static string Args(string dir, string label, string call, string scriptPath, string? expectedHash, string? doneFile, string prelude = "", string? logFile = null)
    {
        string stopLog = !string.IsNullOrEmpty(logFile) ? "; try { Stop-Transcript | Out-Null } catch {}" : "";
        call += (doneFile != null ? "; $sc=$LASTEXITCODE" : "") + stopLog + "; Write-Host ''; Write-Host (" + Quoting.PsString(Loc.T("pyWinExit")) + " -f $LASTEXITCODE)";
        call = HashGuard(scriptPath, expectedHash, call, doneFile != null);
        string startLog = !string.IsNullOrEmpty(logFile)
            ? "try { Start-Transcript -LiteralPath " + Quoting.PsString(logFile) + " -Force | Out-Null } catch {}; "
            : "";
        if (doneFile != null) call = "$sc=-1; " + startLog + "try { " + call + " } " + RunArgs.DoneFinally(doneFile, "$sc", logFile);
        else if (startLog.Length > 0) call = startLog + call;
        string cmd = "Set-Location -LiteralPath " + Quoting.PsString(dir) + "; " + prelude + "Write-Host " + Quoting.PsString(label) + " -ForegroundColor DarkGray; " + call;
        return Prefix + Quoting.NativeArg(cmd);
    }

    /// <summary>The one copy of the check after the UAC prompt (1.3.1; it was written out three times): with
    /// <paramref name="expectedHash"/> (SHA-256, hex) <paramref name="call"/> runs only if the file still has it, else a red
    /// line says why (and $sc=1 when <paramref name="setCode"/>: the end marker gets a failure). No valid hash: the call as it is.</summary>
    public static string HashGuard(string scriptPath, string? expectedHash, string call, bool setCode)
    {
        if (expectedHash == null || !Regex.IsMatch(expectedHash, "^[0-9A-Fa-f]{64}$")) return call;
        return "if((Get-FileHash -LiteralPath " + Quoting.PsString(scriptPath) + " -Algorithm SHA256).Hash -ne '" + expectedHash + "'){ Write-Host " +
               Quoting.PsString(Loc.T("runChangedAdmin")) + " -ForegroundColor Red" + (setCode ? "; $sc=1" : "") + " } else { " + call + " }";
    }
}

/// <summary>Output of programs that write in different code pages (batch files, custom interpreters): each line is UTF-8
/// when it is valid UTF-8 (chcp 65001, most modern tools), else the console's OEM code page (866 on Russian Windows: cmd's
/// built-in commands and older tools without chcp).</summary>
public static class ConsoleText
{
    static readonly UTF8Encoding Strict = new(false, true);
    static Encoding? oem;

    public static string Decode(byte[] line, Encoding fallback)
    {
        int n = line.Length;
        if (n > 0 && line[n - 1] == (byte)'\r') n--;
        int start = n >= 3 && line[0] == 0xEF && line[1] == 0xBB && line[2] == 0xBF ? 3 : 0;
        try { return Strict.GetString(line, start, n - start); }
        catch (DecoderFallbackException) { return fallback.GetString(line, 0, n); }
    }

    [DllImport("kernel32.dll")] static extern uint GetOEMCP();

    /// <summary>The OEM code page of this Windows (866 for Russian, 437 for US English).</summary>
    public static int OemCodePage()
    {
        try { if (OperatingSystem.IsWindows()) return (int)GetOEMCP(); } catch { }
        return CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
    }

    public static Encoding Encoding(int codePage)
    {
        try
        {
            System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(codePage);
        }
        catch { return System.Text.Encoding.Latin1; }
    }

    public static Encoding Oem() => oem ??= Encoding(OemCodePage());
}

/// <summary>Bytes from a pipe into lines (\n, \r\n or a lone \r end a line); a very long line is cut at
/// <see cref="MaxLine"/> bytes.</summary>
public sealed class LineSplitter
{
    public const int MaxLine = 64 * 1024;
    readonly List<byte> buf = new();
    bool afterCr;

    public List<byte[]> Push(byte[] data, int count)
    {
        var res = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            byte b = data[i];
            if (b == (byte)'\n')
            {
                if (afterCr) { afterCr = false; continue; }
                res.Add(Take());
            }
            else if (b == (byte)'\r') { res.Add(Take()); afterCr = true; }
            else
            {
                afterCr = false;
                buf.Add(b);
                if (buf.Count >= MaxLine) res.Add(Take());
            }
        }
        return res;
    }

    /// <summary>The last line when it had no line end; null when there is nothing left.</summary>
    public byte[]? Flush() => buf.Count > 0 ? Take() : null;

    byte[] Take() { var a = buf.ToArray(); buf.Clear(); return a; }
}

/// <summary>The leading comment lines of a script in any of Shelly's languages (1.3.0): "#" for PowerShell and Python,
/// "rem" / "::" for batch files (after @echo off, setlocal or chcp), and #, //, --, ;, ', rem or :: for custom
/// interpreters (after a #! line). Each comment comes back as "# text" so the directive and description rules are the same.</summary>
public static class CommentHeader
{
    /// <summary>"# text" for a comment, "" for a line to skip, null where the header ends.</summary>
    public static string? Normalize(string line, string lang)
    {
        string t = line.Trim().TrimStart('\uFEFF');
        if (t.Length == 0) return "";
        if (lang == ScriptLangs.Batch)
        {
            if (Regex.IsMatch(t, @"^@?(echo\s+off|setlocal\b.*|chcp\s+\d+(\s*>\s*nul)?|title\b.*)$", RegexOptions.IgnoreCase)) return "";
            var m = Regex.Match(t, @"^(@?rem(\s|$)|::)(.*)$", RegexOptions.IgnoreCase);
            return m.Success ? "#" + m.Groups[3].Value : null;
        }
        if (lang == ScriptLangs.Custom)
        {
            if (t.StartsWith("#!")) return "";
            var m = Regex.Match(t, @"^(#|//|--|;|'|@?rem(\s|$)|::)(.*)$", RegexOptions.IgnoreCase);
            return m.Success ? "#" + m.Groups[3].Value : null;
        }
        return t.StartsWith("#") ? t : null;
    }

    /// <summary>The first comment that is not a Shelly directive, as the description.</summary>
    public static string Description(string text, string lang)
    {
        var lines = (text ?? "").Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length && i < FallbackParser.MaxHeaderLines; i++)
        {
            string? c = Normalize(lines[i], lang);
            if (c == null) break;
            if (c.Length == 0 || ScriptHeader.IsDirective(c)) continue;
            string d = FallbackParser.StripDescriptionPrefix(c.TrimStart('#').Trim());
            if (d.Length > 0) return d;
        }
        return "";
    }
}

/// <summary>A light safety check of batch files (1.3.0), with the rules and severities of the PowerShell check: each
/// command of a line (split at &amp;, &amp;&amp;, ||, | and brackets, after if / for … do / call / start) is looked up like a
/// native command in a PowerShell script: del / rd with /s /q (system folders are high), format, diskpart, reg delete,
/// bcdedit, vssadmin delete shadows, cipher /w, takeown / icacls on system folders, shutdown, sc delete, net user,
/// schtasks, wevtutil cl, netsh firewall off, powershell -EncodedCommand or code that downloads and runs. %VAR% becomes
/// $env:VAR for the path rules. Approximate: what it does not know is not reported, so it is a guardrail, not a sandbox.</summary>
public static class BatchScanner
{
    public static List<DangerFinding> Scan(string text)
    {
        var src = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cmds = new List<(int Line, List<string> Words)>();
        for (int i = 0; i < src.Length; i++)
        {
            int ln = i + 1;
            string line = src[i];
            // ^ at the end of a line continues it on the next one
            while (line.TrimEnd().EndsWith("^") && !line.TrimEnd().EndsWith("^^") && i + 1 < src.Length) line = line.TrimEnd()[..^1] + src[++i];
            string t = line.Trim().TrimStart('@').Trim();
            if (t.Length == 0 || t.StartsWith(":") || Regex.IsMatch(t, @"^rem(\s|$)", RegexOptions.IgnoreCase)) continue;
            foreach (var seg in Segments(t))
            {
                // start "title" …: the quoted title is not the program
                var w = Commands(Words(Regex.Replace(seg, @"^(\s*@?start)\s+""[^""]*""", "$1", RegexOptions.IgnoreCase)));
                if (w.Count > 0) cmds.Add((ln, w));
            }
        }
        return FallbackScanner.ScanCommands(cmds, src);
    }

    /// <summary>The parts of a line that are commands of their own: split at &amp;, &amp;&amp;, |, || and ( ) outside quotes.</summary>
    static List<string> Segments(string t)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        bool q = false;
        for (int i = 0; i < t.Length; i++)
        {
            char ch = t[i];
            if (ch == '"') q = !q;
            if (!q && ch == '^' && i + 1 < t.Length) { cur.Append(t[++i]); continue; }
            if (!q && (ch is '&' or '|' or '(' or ')'))
            {
                if (cur.ToString().Trim().Length > 0) res.Add(cur.ToString());
                cur.Clear();
                continue;
            }
            cur.Append(ch);
        }
        if (cur.ToString().Trim().Length > 0) res.Add(cur.ToString());
        return res;
    }

    static List<string> Words(string seg)
    {
        var res = new List<string>();
        foreach (Match m in Regex.Matches(seg, @"(""[^""]*""|[^\s""]+)+"))
        {
            string w = m.Value.Replace("\"", "");
            // Environment variables as the PowerShell rules know them; %~dp0 (the script's folder) is not a system folder
            w = Regex.Replace(w, @"%~[a-z]*\d%?|%~[a-z]*\d", ".\\", RegexOptions.IgnoreCase);
            w = Regex.Replace(w, @"%([A-Za-z_][\w()]*)%", mm => "$env:" + mm.Groups[1].Value.ToLowerInvariant());
            if (w.Length > 0) res.Add(w);
        }
        return res;
    }

    /// <summary>The command itself: without if conditions, for … do, else, call and start (with its title and options).</summary>
    static List<string> Commands(List<string> w)
    {
        for (int guard = 0; guard < 8 && w.Count > 0; guard++)
        {
            string a = w[0].TrimStart('@').ToLowerInvariant();
            if (a.Length == 0) { w = w.Skip(1).ToList(); continue; }
            if (a == "do") { w = w.Skip(1).ToList(); continue; }   // "for … in (set) do": the brackets split the line before "do"
            if (a == "else" || a == "call") { w = w.Skip(1).ToList(); if (w.Count > 0 && w[0].StartsWith(":")) return new(); continue; }
            if (a == "for")
            {
                int d = w.FindIndex(x => x.Equals("do", StringComparison.OrdinalIgnoreCase));
                if (d < 0) return new();
                w = w.Skip(d + 1).ToList();
                continue;
            }
            if (a == "if")
            {
                int k = 1;
                if (k < w.Count && w[k].Equals("/i", StringComparison.OrdinalIgnoreCase)) k++;
                if (k < w.Count && w[k].Equals("not", StringComparison.OrdinalIgnoreCase)) k++;
                if (k >= w.Count) return new();
                string c = w[k].ToLowerInvariant();
                if (c is "exist" or "defined" or "errorlevel" or "cmdextversion") k += 2;
                else if (w[k].Contains("==")) k += w[k].EndsWith("==") ? 2 : 1;
                else if (k + 1 < w.Count && w[k + 1] == "==") k += 3;
                else if (k + 1 < w.Count && Regex.IsMatch(w[k + 1], "^(?i)(equ|neq|lss|leq|gtr|geq)$")) k += 3;
                else k += 1;
                w = w.Skip(k).ToList();
                continue;
            }
            if (a == "start")
            {
                int k = 1;
                // start "title" /wait /b /min … program
                while (k < w.Count && (w[k].StartsWith("/") || (k == 1 && w[k].Length == 0))) k++;
                w = w.Skip(k).ToList();
                continue;
            }
            break;
        }
        return w;
    }
}

/// <summary>How a batch file or a custom interpreter's script is started (1.3.0), for interactive and scheduled runs alike.
/// Window(expectedHash, doneFile) gives the PowerShell host's command line of a window run.</summary>
public sealed record OtherRunPlan(
    string Engine, string Exe, string InlineArgs, string WorkDir, string Label,
    Func<string?, string?, string> Window,
    Func<string?, string?, string?, string>? WindowWithLog = null);

public static class OtherRun
{
    /// <summary>The plan, or null with <paramref name="error"/> (a Loc key; its {0} is the script name, {1} the detail).</summary>
    public static OtherRunPlan? Plan(ScriptFile f, string? userArgs, out string? error, out string detail,
        string? comspec = null, Func<string, bool>? exists = null, IEnumerable<string>? pathDirs = null, byte[]? content = null)
    {
        error = null; detail = "";
        comspec ??= BatchRun.ComSpec();
        string args = (userArgs ?? "").Trim();
        if (!PythonRun.IsValidArgs(args)) { error = "rsPyArgsBad"; detail = PythonRun.MaxArgsLength.ToString(); return null; }
        string dir = CustomRun.DirOf(f.Path);
        if (f.IsBatch)
        {
            if (BatchRun.PathError(f.Path) is { } pe) { error = pe; return null; }
            if (BatchRun.ArgsError(args) is { } ae) { error = ae; return null; }
            bool utf8;
            try { utf8 = BatchRun.IsUtf8Source(content ?? File.ReadAllBytes(f.Path)); } catch { utf8 = true; }
            string cs = comspec;
            return new OtherRunPlan(EngineInfo.Batch, cs, BatchRun.InlineArgs(f.Path, args, utf8), dir, "cmd.exe",
                (hash, done) => BatchRun.WindowArgs(cs, f.Path, args, utf8, hash, done),
                (hash, done, log) => BatchRun.WindowArgs(cs, f.Path, args, utf8, hash, done, log));
        }
        var ci = ScriptKinds.CustomFor(f.Path);
        if (ci == null) { error = "customGone"; return null; }
        string? prog = CustomInterpreters.ResolveProgram(ci.Program, exists ?? File.Exists, pathDirs ?? AnalysisService.PathDirs());
        if (prog == null) { error = "customNoProgram"; detail = ci.Program; return null; }
        var (exe, inline, err) = CustomRun.Inline(ci, prog, f.Path, args, comspec);
        if (err != null) { error = err; return null; }
        string wd = CustomRun.WorkDir(ci, prog, f.Path);
        string cs2 = comspec;
        return new OtherRunPlan(EngineInfo.Custom, exe, inline, wd, ci.Name,
            (hash, done) => CustomRun.WindowArgs(ci, prog, f.Path, args, cs2, wd, hash, done),
            (hash, done, log) => CustomRun.WindowArgs(ci, prog, f.Path, args, cs2, wd, hash, done, log));
    }
}
