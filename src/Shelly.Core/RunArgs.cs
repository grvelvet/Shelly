namespace Shelly.Core;

public static class RunArgs
{
    /// <summary>Names of mandatory text parameters without a value (switch / bool always have one).</summary>
    public static List<string> MissingMandatory(IEnumerable<ParamInfo> ps, IDictionary<string, object>? vals)
    {
        var missing = new List<string>();
        foreach (var p in ps)
        {
            if (!p.Mandatory || p.Kind == "switch" || p.Kind == "bool") continue;
            object? v = null;
            if (vals != null) vals.TryGetValue(p.Name, out v);
            if (v == null || (v is string s && s.Trim().Length == 0)) missing.Add(p.Name);
        }
        return missing;
    }

    /// <summary>Dialog values to PowerShell arguments: -Name 'value' -Switch -Flag:$false -List 'a','b'.</summary>
    public static string Format(IEnumerable<ParamInfo> ps, IDictionary<string, object>? vals)
    {
        if (vals == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var p in ps)
        {
            if (!vals.TryGetValue(p.Name, out var v) || v == null) continue;
            switch (p.Kind)
            {
                case "switch":
                    if (IsTrue(v)) sb.Append(" -").Append(p.Name);
                    else if (p.DefaultBool) sb.Append(" -").Append(p.Name).Append(":$false");
                    break;
                case "bool":
                    sb.Append(" -").Append(p.Name).Append(':').Append(IsTrue(v) ? "$true" : "$false");
                    break;
                case "array":
                    var parts = (Convert.ToString(v) ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(Quoting.PsString).ToList();
                    if (parts.Count > 0) sb.Append(" -").Append(p.Name).Append(' ').Append(string.Join(",", parts));
                    break;
                default:
                    string t = Convert.ToString(v) ?? "";
                    if (t.Length > 0) sb.Append(" -").Append(p.Name).Append(' ').Append(Quoting.PsString(t));
                    break;
            }
        }
        return sb.ToString();
    }

    // One quoted value as Quoting.PsString writes it: a quote character inside is doubled (the same character twice)
    const string Quoted = @"'(?:[^'\u2018\u2019\u201A\u201B]|''|\u2018\u2018|\u2019\u2019|\u201A\u201A|\u201B\u201B)*'";
    static readonly System.Text.RegularExpressions.Regex Shape = new(
        @"^(?: +-[\p{L}_][\p{L}\p{N}_]*(?::\$(?:true|false)| +" + Quoted + "(?:," + Quoted + ")*)?)* *$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>true when the text has the shape <see cref="Format"/> produces (-Name, -Name:$true, -Name 'value', -Name 'a','b') and nothing else.
    /// Arguments kept in the history file are plain text that anyone can edit: only this shape goes into a command line.</summary>
    public static bool IsValid(string? argText)
    {
        if (string.IsNullOrEmpty(argText)) return true;
        if (argText.Length > 32768) return false;
        try { return Shape.IsMatch(argText); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }

    static bool IsTrue(object v) => v is bool b ? b : string.Equals(Convert.ToString(v), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Command for inline runs: plain text output (no ANSI in PowerShell 7), the same exit code rules in both
    /// engines (the script's exit N or last native exit code, 1 on a terminating error, else 0).</summary>
    public static string InlineCommand(string path, string argText) =>
        "try{[Console]::OutputEncoding=[Text.Encoding]::UTF8}catch{}; $ProgressPreference='SilentlyContinue'; " +
        "if($PSStyle){try{$PSStyle.OutputRendering='PlainText'}catch{}}; " +
        "$global:LASTEXITCODE=0; & " + Quoting.PsString(path) + argText + "; if($LASTEXITCODE){exit $LASTEXITCODE}; exit 0";

    public static string InlineArgs(string path, string argText) =>
        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + Quoting.NativeArg(InlineCommand(path, argText));

    /// <summary>Arguments for a separate window (and administrator runs, where the working folder would be System32).
    /// expectedHash (SHA-256, hex; administrator runs): the script runs only if the file still has it when PowerShell gets to
    /// it, after the UAC prompt (<see cref="WindowHost.HashGuard"/>). The window stays open (-NoExit), and doneFile gets the
    /// script's exit code when it ends (1.2.0), with the same rules as inline runs. 1.3.1: the older forms without an end
    /// marker (-File, -Command without it) and the admin flag are gone; every window run has had a marker since 1.2.0.</summary>
    public static string WindowArgs(string path, string argText, string? expectedHash, string doneFile, string? logFile = null)
    {
        string dir = System.IO.Path.GetDirectoryName(path) ?? "";
        string run = "$global:LASTEXITCODE=0; & " + Quoting.PsString(path) + argText + "; if($LASTEXITCODE){$sc=$LASTEXITCODE}";
        run = WindowHost.HashGuard(path, expectedHash, run, true);
        string startLog = !string.IsNullOrEmpty(logFile)
            ? "try { Start-Transcript -LiteralPath " + Quoting.PsString(logFile) + " -Force | Out-Null } catch {}; "
            : "";
        string body = "Set-Location -LiteralPath " + Quoting.PsString(dir) + "; $sc=0; " + startLog +
                      "try { " + run + " } catch { $Host.UI.WriteErrorLine(($_ | Out-String).Trim()); $sc=1 } " +
                      DoneFinally(doneFile, "$sc", logFile);
        return WindowHost.Prefix + Quoting.NativeArg(body);
    }

    /// <summary>The end marker of a window run: stops the transcript (when logFile is set) and writes the exit code to doneFile.</summary>
    public static string DoneFinally(string doneFile, string codeExpr, string? logFile = null) =>
        "finally { " +
        (!string.IsNullOrEmpty(logFile) ? "try { Stop-Transcript | Out-Null } catch {}; " : "") +
        "try { [IO.File]::WriteAllText(" + Quoting.PsString(doneFile) + ", [string](" + codeExpr + ")) } catch {} }";

    /// <summary>The exit code in an end marker file; null when the file is not there or not a number yet.</summary>
    public static int? ReadDone(string doneFile)
    {
        try
        {
            if (!File.Exists(doneFile)) return null;
            string t = File.ReadAllText(doneFile).Trim();
            return int.TryParse(t, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int c) ? c : null;
        }
        catch { return null; }
    }
}
