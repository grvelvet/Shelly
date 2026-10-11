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

    static bool IsTrue(object v) => v is bool b ? b : string.Equals(Convert.ToString(v), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Command for inline runs: plain text output (no ANSI in PowerShell 7), the same exit code rules in both
    /// engines (the script's exit N or last native exit code, 1 on a terminating error, else 0).</summary>
    public static string InlineCommand(string path, string argText) =>
        "try{[Console]::OutputEncoding=[Text.Encoding]::UTF8}catch{}; $ProgressPreference='SilentlyContinue'; " +
        "if($PSStyle){try{$PSStyle.OutputRendering='PlainText'}catch{}}; " +
        "$global:LASTEXITCODE=0; & " + Quoting.PsString(path) + argText + "; if($LASTEXITCODE){exit $LASTEXITCODE}; exit 0";

    public static string InlineArgs(string path, string argText) =>
        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + Quoting.NativeArg(InlineCommand(path, argText));

    /// <summary>Arguments for a separate window (and administrator runs, where the working folder would be System32).</summary>
    public static string WindowArgs(string path, string argText, bool admin)
    {
        string dir = System.IO.Path.GetDirectoryName(path) ?? "";
        if (argText.Length > 0 || admin)
        {
            string cmd = "Set-Location -LiteralPath " + Quoting.PsString(dir) + "; & " + Quoting.PsString(path) + argText;
            return "-NoProfile -NoExit -ExecutionPolicy Bypass -Command " + Quoting.NativeArg(cmd);
        }
        return "-NoProfile -NoExit -ExecutionPolicy Bypass -File " + Quoting.NativeArg(path);
    }
}
