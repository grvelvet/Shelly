using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>A single lint issue reported by <see cref="ScriptLinter"/> (1.4.0).</summary>
public sealed record LintIssue(
    int Line,
    int Column,
    int Length,
    string Severity,
    string Code,
    string Message)
{
    public int SeverityRank => Severity switch { "error" => 0, "warning" => 1, _ => 2 };
}

/// <summary>Fast static linter for PowerShell (.ps1), Python (.py), Batch (.bat/.cmd), and custom scripts (1.4.0).</summary>
public static class ScriptLinter
{
    public static List<LintIssue> Lint(string path, string text, ScriptMeta? meta = null, bool hasBom = false)
    {
        string lang = ScriptKinds.Lang(path);
        var issues = new List<LintIssue>();
        text ??= "";

        if (lang == ScriptLangs.PowerShell)
        {
            if (meta?.Engine?.Errors is { Count: > 0 } parseErrs)
            {
                foreach (var err in parseErrs)
                    issues.Add(new LintIssue(Math.Max(1, err.Line), 1, 0, "error", "PS-SYNTAX", err.Message));
            }
            var dangers = FallbackScanner.Scan(text);
            foreach (var d in dangers)
            {
                string sev = d.Sev == "high" ? "error" : d.Sev == "medium" ? "warning" : "info";
                issues.Add(new LintIssue(Math.Max(1, d.Line), 1, Math.Max(0, d.Code.Length), sev, "PS-SAFETY-" + d.Rule.ToUpperInvariant(), d.Text));
            }
            if (meta?.Issues is { Count: > 0 } compatIssues)
            {
                foreach (var ci in compatIssues.Where(i => i.Kind != "parse"))
                {
                    string sev = ci.Level == "warn" ? "warning" : "info";
                    issues.Add(new LintIssue(1, 1, 0, sev, "PS-COMPAT", ci.Text()));
                }
            }
            issues.AddRange(LintPowerShell(text));
        }
        else if (lang == ScriptLangs.Python)
        {
            issues.AddRange(LintPython(text));
        }
        else if (lang == ScriptLangs.Batch)
        {
            issues.AddRange(LintBatch(text, hasBom));
        }
        else
        {
            issues.AddRange(LintCustom(text, hasBom));
        }

        return SortAndDedupe(issues);
    }

    public static List<LintIssue> SortAndDedupe(IEnumerable<LintIssue> issues) =>
        issues
            .GroupBy(i => (i.Line, i.Column, i.Code, i.Message))
            .Select(g => g.First())
            .OrderBy(i => i.SeverityRank)
            .ThenBy(i => i.Line)
            .ThenBy(i => i.Column)
            .ToList();

    // ---------- PowerShell ----------

    static readonly Dictionary<string, string> PsAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["curl"] = "Invoke-WebRequest",
        ["wget"] = "Invoke-WebRequest",
        ["ls"] = "Get-ChildItem",
        ["dir"] = "Get-ChildItem",
        ["cat"] = "Get-Content",
        ["rm"] = "Remove-Item",
        ["cp"] = "Copy-Item",
        ["mv"] = "Move-Item",
        ["%"] = "ForEach-Object",
        ["?"] = "Where-Object"
    };

    static readonly Regex PsEmptyCatchRx = new(@"\bcatch\b\s*(?:\[[A-Za-z0-9_.,\[\]\s]+\]\s*)?\{\s*(?:#[^\r\n]*\s*)*\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PsSecretRx = new(@"(\$\w*(?:password|passwd|pwd|secret|api[_-]?key|token|client[_-]?secret|access[_-]?key)\w*)\s*=\s*(['""])(?![\s$])([^'""]{4,})\2", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PsErrPrefRx = new(@"\$ErrorActionPreference\s*=\s*['""]SilentlyContinue['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PsAliasCmdRx = new(@"(?:^|[|;{=(])\s*(curl|wget|ls|dir|cat|rm|cp|mv|%|\?)(?=\s+[^\s=]|\s*$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<LintIssue> LintPowerShell(string text)
    {
        var list = new List<LintIssue>();
        if (string.IsNullOrEmpty(text)) return list;
        string clean = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = clean.Split('\n');

        // 1. Empty catch {} blocks (can span multiple lines)
        foreach (Match m in PsEmptyCatchRx.Matches(clean))
        {
            var (ln, col) = OffsetToLineCol(clean, m.Index);
            list.Add(new LintIssue(ln, col, Math.Min(m.Length, 20), "warning", "PS-EMPTY-CATCH", Loc.T("lintPsEmptyCatch")));
        }

        // 2. Per-line checks: secrets, aliases, $ErrorActionPreference
        bool inBlockComment = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (inBlockComment)
            {
                if (trimmed.Contains("#>", StringComparison.Ordinal)) inBlockComment = false;
                continue;
            }
            if (trimmed.StartsWith("<#", StringComparison.Ordinal))
            {
                if (!trimmed.Contains("#>", StringComparison.Ordinal)) inBlockComment = true;
                continue;
            }
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

            string codeOnly = StripPsInlineComment(line);

            var sec = PsSecretRx.Match(codeOnly);
            if (sec.Success)
            {
                list.Add(new LintIssue(i + 1, sec.Index + 1, sec.Groups[1].Length, "warning", "PS-SECRET", Loc.T("lintPsSecret")));
            }

            var ep = PsErrPrefRx.Match(codeOnly);
            if (ep.Success)
            {
                list.Add(new LintIssue(i + 1, ep.Index + 1, ep.Length, "warning", "PS-ERRPREF", Loc.T("lintPsErrPref")));
            }

            foreach (Match am in PsAliasCmdRx.Matches(codeOnly))
            {
                string alias = am.Groups[1].Value;
                int col = am.Groups[1].Index + 1;
                if (IsInsideQuotes(codeOnly, am.Groups[1].Index)) continue;
                if (PsAliases.TryGetValue(alias, out var fullCmd))
                    list.Add(new LintIssue(i + 1, col, alias.Length, "info", "PS-ALIAS", Loc.F("lintPsAlias", alias, fullCmd)));
            }
        }

        // 3. Unused parameters in param(...) block
        CheckPsUnusedParams(clean, list);

        return list;
    }

    static void CheckPsUnusedParams(string clean, List<LintIssue> list)
    {
        var pm = Regex.Match(clean, @"(?im)^\s*param\s*\(");
        if (!pm.Success) return;
        int openIdx = clean.IndexOf('(', pm.Index);
        if (openIdx < 0) return;
        int depth = 0, closeIdx = -1;
        for (int i = openIdx; i < clean.Length; i++)
        {
            if (clean[i] == '(') depth++;
            else if (clean[i] == ')')
            {
                depth--;
                if (depth == 0) { closeIdx = i; break; }
            }
        }
        if (closeIdx <= openIdx) return;
        string paramBlock = clean.Substring(openIdx + 1, closeIdx - openIdx - 1);
        string rest = clean.Substring(closeIdx + 1);

        foreach (Match vm in Regex.Matches(paramBlock, @"\$([A-Za-z_][A-Za-z0-9_]*)\b"))
        {
            string name = vm.Groups[1].Value;
            if (name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("null", StringComparison.OrdinalIgnoreCase))
                continue;
            // Check if $name or $PSBoundParameters['name'] is used in rest
            if (!Regex.IsMatch(rest, @"\$(?:script:|local:|private:)?" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(rest, @"PSBoundParameters", RegexOptions.IgnoreCase))
            {
                var (ln, col) = OffsetToLineCol(clean, openIdx + 1 + vm.Index);
                list.Add(new LintIssue(ln, col, vm.Length, "warning", "PS-UNUSED-PARAM", Loc.F("lintPsUnusedParam", name)));
            }
        }
    }

    static string StripPsInlineComment(string line)
    {
        bool inS = false, inD = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '`' && inD && i + 1 < line.Length) { i++; continue; }
            if (c == '\'' && !inD) inS = !inS;
            else if (c == '"' && !inS) inD = !inD;
            else if (c == '#' && !inS && !inD) return line.Substring(0, i);
        }
        return line;
    }

    // ---------- Python ----------

    static readonly Regex PyBareExceptRx = new(@"^\s*(except\s*:)\s*(?:#.*)?$", RegexOptions.Compiled);
    static readonly Regex PyMutableDefRx = new(@"^\s*(?:async\s+)?def\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^)]*?([A-Za-z_][A-Za-z0-9_]*\s*(?::[^=,)]+)?=\s*(?:\[\s*\]|\{\s*\}|set\s*\(\s*\)))", RegexOptions.Compiled);
    static readonly Regex PyPrint2Rx = new(@"^\s*(print\s+(?:""[^""]*""|'[^']*'))(?:\s*#.*)?$", RegexOptions.Compiled);
    static readonly Regex PyEqNoneRx = new(@"(?<![=!<>])(==|!=)\s*None\b", RegexOptions.Compiled);

    public static List<LintIssue> LintPython(string text)
    {
        var list = new List<LintIssue>();
        if (string.IsNullOrEmpty(text)) return list;
        string clean = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = clean.Split('\n');

        int tabOnlyLines = 0, spaceOnlyLines = 0;
        int firstTabLine = 0, firstSpaceLine = 0;
        bool reportedMixedIndent = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

            // Check leading indentation
            int indentLen = line.Length - line.TrimStart(' ', '\t').Length;
            if (indentLen > 0)
            {
                string indent = line.Substring(0, indentLen);
                bool hasTab = indent.Contains('\t');
                bool hasSpace = indent.Contains(' ');
                if (hasTab && hasSpace)
                {
                    list.Add(new LintIssue(i + 1, 1, indentLen, "warning", "PY-TAB-SPACE", Loc.T("lintPyTabSpace")));
                    reportedMixedIndent = true;
                }
                else if (hasTab) { tabOnlyLines++; if (firstTabLine == 0) firstTabLine = i + 1; }
                else if (hasSpace) { spaceOnlyLines++; if (firstSpaceLine == 0) firstSpaceLine = i + 1; }
            }

            string codeOnly = StripPyInlineComment(line);

            var be = PyBareExceptRx.Match(line);
            if (be.Success)
                list.Add(new LintIssue(i + 1, be.Groups[1].Index + 1, be.Groups[1].Length, "warning", "PY-BARE-EXCEPT", Loc.T("lintPyBareExcept")));

            var md = PyMutableDefRx.Match(codeOnly);
            if (md.Success)
                list.Add(new LintIssue(i + 1, md.Groups[1].Index + 1, md.Groups[1].Length, "warning", "PY-MUTABLE-DEFAULT", Loc.F("lintPyMutableDefault", md.Groups[1].Value.Trim())));

            var p2 = PyPrint2Rx.Match(codeOnly);
            if (p2.Success)
                list.Add(new LintIssue(i + 1, p2.Groups[1].Index + 1, 5, "error", "PY-PRINT2", Loc.T("lintPyPrint2")));

            foreach (Match eq in PyEqNoneRx.Matches(codeOnly))
            {
                if (!IsInsideQuotes(codeOnly, eq.Index))
                    list.Add(new LintIssue(i + 1, eq.Index + 1, eq.Length, "info", "PY-EQ-NONE", Loc.F("lintPyEqNone", eq.Value)));
            }
        }

        if (!reportedMixedIndent && tabOnlyLines > 0 && spaceOnlyLines > 0)
        {
            int ln = Math.Max(firstTabLine, firstSpaceLine);
            list.Add(new LintIssue(ln, 1, 1, "warning", "PY-TAB-SPACE", Loc.T("lintPyTabSpace")));
        }

        return list;
    }

    static string StripPyInlineComment(string line)
    {
        bool inS = false, inD = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && (inS || inD) && i + 1 < line.Length) { i++; continue; }
            if (c == '\'' && !inD) inS = !inS;
            else if (c == '"' && !inS) inD = !inD;
            else if (c == '#' && !inS && !inD) return line.Substring(0, i);
        }
        return line;
    }

    public static LintIssue? ParsePythonSyntaxError(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return null;
        string clean = stderr.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        // Match: File "<stdin>" (or "<string>"), line N
        int line = 1, col = 1;
        var lm = Regex.Match(clean, @"line\s+(\d+)", RegexOptions.IgnoreCase);
        if (lm.Success && int.TryParse(lm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLine))
            line = Math.Max(1, parsedLine);

        var lines = clean.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        string msg = lines.Count > 0 ? lines[^1] : "SyntaxError";
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (Regex.IsMatch(lines[i], @"^(SyntaxError|IndentationError|TabError)\s*:", RegexOptions.IgnoreCase))
            {
                msg = lines[i];
                if (i >= 1 && lines[i - 1].Trim() == "^")
                    col = Math.Max(1, lines[i - 1].IndexOf('^') + 1);
                break;
            }
        }
        return new LintIssue(line, col, 1, "error", "PY-SYNTAX", msg);
    }

    public static async Task<LintIssue?> CheckPythonSyntaxAsync(string pythonExe, IReadOnlyList<string>? prefix, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pythonExe) || !File.Exists(pythonExe)) return null;
        try
        {
            var psi = new ProcessStartInfo(pythonExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (prefix != null)
                foreach (var a in prefix) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("-I");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import ast,sys; ast.parse(sys.stdin.read())");

            using var proc = Process.Start(psi);
            if (proc == null) return null;
            await proc.StandardInput.WriteAsync(text.AsMemory(), ct);
            proc.StandardInput.Close();

            var errTask = proc.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                return null;
            }
            if (proc.ExitCode == 0) return null;
            string stderr = await errTask;
            return ParsePythonSyntaxError(stderr);
        }
        catch { return null; }
    }

    // ---------- Batch (.bat / .cmd) ----------

    static readonly Regex BatLabelDefRx = new(@"^\s*:([A-Za-z0-9_.\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BatGotoRx = new(@"\b(?:goto\s+:?|call\s+:)([A-Za-z0-9_.\-]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BatForSinglePctRx = new(@"\bfor\b(?:\s+/[A-Za-z]\b(?:\s+""[^""]*"")?)*\s+(?<!%)%([A-Za-z])\s+in\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BatDelayedVarRx = new(@"!([A-Za-z_][A-Za-z0-9_]*)!", RegexOptions.Compiled);
    static readonly Regex BatEnableDelayedRx = new(@"\bsetlocal\b[^\r\n]*\bEnableDelayedExpansion\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<LintIssue> LintBatch(string text, bool hasBom = false)
    {
        var list = new List<LintIssue>();
        text ??= "";
        if (hasBom || text.StartsWith('\uFEFF'))
            list.Add(new LintIssue(1, 1, 1, "warning", "BAT-BOM", Loc.T("lintBatBom")));

        string clean = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = clean.Split('\n');

        // Safety scanner findings
        foreach (var d in BatchScanner.Scan(clean))
        {
            string sev = d.Sev == "high" ? "error" : d.Sev == "medium" ? "warning" : "info";
            list.Add(new LintIssue(Math.Max(1, d.Line), 1, Math.Max(0, d.Code.Length), sev, "BAT-SAFETY-" + d.Rule.ToUpperInvariant(), d.Text));
        }

        // Collect all defined labels
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "eof" };
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.StartsWith("::", StringComparison.Ordinal)) continue;
            var lm = BatLabelDefRx.Match(lines[i]);
            if (lm.Success) labels.Add(lm.Groups[1].Value);
        }

        bool hasDelayedExpansion = BatEnableDelayedRx.IsMatch(clean);
        bool reportedDelayed = false;
        var parenStack = new Stack<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("::", StringComparison.Ordinal) ||
                Regex.IsMatch(trimmed, @"^@?rem\b", RegexOptions.IgnoreCase))
                continue;
            if (trimmed.StartsWith(":", StringComparison.Ordinal))
                continue;

            // 1. Single %i in FOR loop
            var fm = BatForSinglePctRx.Match(line);
            if (fm.Success)
            {
                list.Add(new LintIssue(i + 1, fm.Groups[1].Index, 2, "error", "BAT-FOR-PERCENT", Loc.F("lintBatForPercent", fm.Groups[1].Value)));
            }

            // 2. Missing label in goto / call :label
            foreach (Match gm in BatGotoRx.Matches(line))
            {
                if (IsInsideQuotes(line, gm.Index)) continue;
                string target = gm.Groups[1].Value;
                if (target.Contains('%') || target.Contains('!')) continue;
                if (!labels.Contains(target))
                {
                    list.Add(new LintIssue(i + 1, gm.Groups[1].Index + 1, target.Length, "error", "BAT-MISSING-LABEL", Loc.F("lintBatMissingLabel", target)));
                }
            }

            // 3. Delayed expansion !VAR! without setlocal EnableDelayedExpansion
            if (!hasDelayedExpansion && !reportedDelayed)
            {
                var dm = BatDelayedVarRx.Match(line);
                if (dm.Success)
                {
                    list.Add(new LintIssue(i + 1, dm.Index + 1, dm.Length, "warning", "BAT-DELAYED-EXP", Loc.F("lintBatDelayedExp", dm.Groups[1].Value)));
                    reportedDelayed = true;
                }
            }

            // 4. Track parentheses outside quotes and ^ escapes
            bool inQ = false;
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (!inQ && ch == '^' && c + 1 < line.Length) { c++; continue; }
                if (ch == '"') { inQ = !inQ; continue; }
                if (inQ) continue;
                if (ch == '(') parenStack.Push(i + 1);
                else if (ch == ')')
                {
                    if (parenStack.Count == 0)
                        list.Add(new LintIssue(i + 1, c + 1, 1, "error", "BAT-PARENS", Loc.T("lintBatParens")));
                    else
                        parenStack.Pop();
                }
            }
        }

        if (parenStack.Count > 0)
        {
            int openLine = parenStack.Pop();
            list.Add(new LintIssue(openLine, 1, 1, "error", "BAT-PARENS", Loc.T("lintBatParens")));
        }

        return list;
    }

    // ---------- Custom scripts ----------

    public static List<LintIssue> LintCustom(string text, bool hasBom = false)
    {
        var list = new List<LintIssue>();
        text ??= "";
        if (hasBom || text.StartsWith('\uFEFF'))
            list.Add(new LintIssue(1, 1, 1, "warning", "CUSTOM-BOM", Loc.T("lintCustomBom")));

        string clean = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = clean.Split('\n');
        var stack = new Stack<(char Ch, int Line, int Col)>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            bool inS = false, inD = false;
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (ch == '\\' && (inS || inD) && c + 1 < line.Length) { c++; continue; }
                if (ch == '\'' && !inD) { inS = !inS; continue; }
                if (ch == '"' && !inS) { inD = !inD; continue; }
                if (inS || inD) continue;
                if (ch == '#' || (ch == '/' && c + 1 < line.Length && line[c + 1] == '/')) break;

                if (ch is '(' or '[' or '{')
                    stack.Push((ch, i + 1, c + 1));
                else if (ch is ')' or ']' or '}')
                {
                    char expected = ch == ')' ? '(' : ch == ']' ? '[' : '{';
                    if (stack.Count == 0 || stack.Peek().Ch != expected)
                    {
                        list.Add(new LintIssue(i + 1, c + 1, 1, "warning", "CUSTOM-BRACKETS", Loc.F("lintCustomBrackets", ch)));
                        return list;
                    }
                    stack.Pop();
                }
            }
        }

        if (stack.Count > 0)
        {
            var top = stack.Pop();
            list.Add(new LintIssue(top.Line, top.Col, 1, "warning", "CUSTOM-BRACKETS", Loc.F("lintCustomBrackets", top.Ch)));
        }

        return list;
    }

    // ---------- Helpers ----------

    static (int Line, int Col) OffsetToLineCol(string text, int offset)
    {
        int line = 1, col = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; col = 1; }
            else if (text[i] != '\r') col++;
        }
        return (line, col);
    }

    static bool IsInsideQuotes(string line, int index)
    {
        bool inS = false, inD = false;
        for (int i = 0; i < index && i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\'' && !inD) inS = !inS;
            else if (c == '"' && !inS) inD = !inD;
        }
        return inS || inD;
    }
}
