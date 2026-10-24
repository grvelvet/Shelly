using System.Text;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>Built-in reader for the parts of a script Shelly needs before (or without) PowerShell's parser:
/// the description comment, #Requires and the param() block. It understands comments, quoted strings, here-strings,
/// nested brackets, attributes ([Parameter(Mandatory)], [ValidateSet(...)]), type constraints and defaults.
/// Results are marked Fallback; the PowerShell AST (Analyzer.ps1) replaces them as soon as it is available.</summary>
public static class FallbackParser
{
    public static AnalysisRecord Parse(string path, string ticks, string text)
    {
        var r = new AnalysisRecord { Path = path, Ticks = ticks, Engine = "", Fallback = true };
        r.Desc = Description(text) ?? "";
        r.Req = Requires(text);
        try { r.Params = Params(text); } catch { r.Params = new(); }
        try { r.Danger = FallbackScanner.Scan(text); } catch { r.Danger = new(); }
        return r;
    }

    public static AnalysisRecord ParseFile(string path)
    {
        string ticks = File.GetLastWriteTimeUtc(path).Ticks.ToString();
        string text = "";
        try { text = TextFile.ReadAllText(path); } catch { }
        return Parse(path, ticks, text);
    }

    /// <summary>The script's description, from its leading comments (before any code):
    /// a one-line <c># comment</c> among the first 8 lines (not <c>#Requires</c>, <c>#!</c> or a <c># Shelly-…:</c>
    /// directive; a "Description:" / "Описание:" prefix is dropped), or the <c>.SYNOPSIS</c> of a <c>&lt;# … #&gt;</c>
    /// help block, whichever comes first. The same rules as Get-Description in Analyzer.ps1.</summary>
    public static string? Description(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length && i < MaxHeaderLines; i++)
        {
            string t = lines[i].Trim().TrimStart('\uFEFF');
            if (t.Length == 0) continue;
            if (t.StartsWith("<#"))
            {
                var block = new List<string>();
                string first = t.Substring(2);
                int j = i;
                bool closed = false;
                for (string cur = first; ; cur = lines[j].Trim())
                {
                    int end = cur.IndexOf("#>", StringComparison.Ordinal);
                    if (end >= 0) { block.Add(cur.Substring(0, end)); closed = true; break; }
                    block.Add(cur);
                    if (++j >= lines.Length || j >= MaxHeaderLines) break;
                }
                string? syn = Synopsis(block);
                if (syn != null) return syn;
                if (!closed) return null;
                i = j;
                continue;
            }
            if (i >= 8) break;
            if (t.StartsWith("#") && !t.StartsWith("#Requires", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("#!") && !ScriptHeader.IsDirective(t))
            {
                string d = t.TrimStart('#').Trim();
                if (d.StartsWith("<")) d = d.TrimStart('<').Trim();
                d = StripDescriptionPrefix(d);
                if (d.Length > 0) return d;
            }
            if (!t.StartsWith("#")) break;
        }
        return null;
    }

    /// <summary>How far the leading comments are read (a help block can be long).</summary>
    public const int MaxHeaderLines = 60;

    static readonly Regex DescPrefix = new(@"^(description|описание)\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string StripDescriptionPrefix(string d) => DescPrefix.Replace(d, "").Trim();

    /// <summary>The first paragraph after <c>.SYNOPSIS</c> in the lines of a help block, joined into one line.</summary>
    public static string? Synopsis(IEnumerable<string> blockLines)
    {
        bool inSyn = false;
        var parts = new List<string>();
        foreach (var raw in blockLines)
        {
            string t = raw.Trim();
            if (!inSyn)
            {
                if (Regex.IsMatch(t, @"^\.SYNOPSIS\s*$", RegexOptions.IgnoreCase)) inSyn = true;
                continue;
            }
            if (Regex.IsMatch(t, @"^\.[A-Za-z]+(\s|$)")) break;   // the next help keyword
            if (t.Length == 0) { if (parts.Count > 0) break; continue; }
            parts.Add(t);
        }
        string s = string.Join(" ", parts).Trim();
        return s.Length > 0 ? s : null;
    }

    public static RequiresInfo Requires(string text)
    {
        var req = new RequiresInfo();
        foreach (Match m in Regex.Matches(text, @"(?im)^\s*#requires\s+(.+)$"))
        {
            string s = m.Groups[1].Value;
            var v = Regex.Match(s, @"(?i)-version\s+([\d.]+)");
            if (v.Success) req.Version = v.Groups[1].Value;
            foreach (Match e in Regex.Matches(s, @"(?i)-psedition\s+([\w, ]+)"))
                foreach (var ed in e.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (ed.Equals("Core", StringComparison.OrdinalIgnoreCase) || ed.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                        req.Editions.Add(char.ToUpperInvariant(ed[0]) + ed.Substring(1).ToLowerInvariant());
            if (Regex.IsMatch(s, @"(?i)-runasadministrator")) req.Elevation = true;
        }
        return req;
    }

    // ---------- param() block ----------
    public static List<ParamInfo> Params(string text)
    {
        var res = new List<ParamInfo>();
        int start = FindParamBlock(text);
        if (start < 0) return res;
        int close = MatchBracket(text, start);
        if (close < 0) return res;
        string body = text.Substring(start + 1, close - start - 1);
        foreach (var part in SplitTop(body, ','))
        {
            var p = ParseOne(part);
            if (p != null) res.Add(p);
        }
        return res;
    }

    /// <summary>Index of '(' of the script's param block: the first statement after comments, #Requires,
    /// using statements and attributes such as [CmdletBinding()].</summary>
    static int FindParamBlock(string t)
    {
        int i = 0;
        while (i < t.Length)
        {
            char c = t[i];
            if (char.IsWhiteSpace(c) || c == '\uFEFF') { i++; continue; }
            if (c == '#') { i = SkipLineComment(t, i); continue; }
            if (c == '<' && i + 1 < t.Length && t[i + 1] == '#') { i = SkipBlockComment(t, i); continue; }
            if (c == '[')
            {
                int e = MatchBracket(t, i);
                if (e < 0) return -1;
                i = e + 1;
                continue;
            }
            var m = Regex.Match(t.Substring(i, Math.Min(64, t.Length - i)), @"^using\s+[^\r\n]*", RegexOptions.IgnoreCase);
            if (m.Success) { i += m.Length; continue; }
            m = Regex.Match(t.Substring(i, Math.Min(16, t.Length - i)), @"^param\s*\(", RegexOptions.IgnoreCase);
            if (m.Success) return i + m.Length - 1;
            return -1;
        }
        return -1;
    }

    static int SkipLineComment(string t, int i) { int e = t.IndexOf('\n', i); return e < 0 ? t.Length : e + 1; }
    static int SkipBlockComment(string t, int i) { int e = t.IndexOf("#>", i + 2, StringComparison.Ordinal); return e < 0 ? t.Length : e + 2; }

    /// <summary>Index of the bracket that closes the one at <paramref name="open"/>, skipping strings and comments.</summary>
    public static int MatchBracket(string t, int open)
    {
        var stack = new Stack<char>();
        for (int i = open; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '#' ) { i = SkipLineComment(t, i) - 1; continue; }
            if (c == '<' && i + 1 < t.Length && t[i + 1] == '#') { i = SkipBlockComment(t, i) - 1; continue; }
            if (c == '@' && i + 1 < t.Length && (t[i + 1] == '"' || t[i + 1] == '\''))
            {
                // here-string: @" ... newline "@
                string end = "\n" + t[i + 1] + "@";
                int e = t.IndexOf(end, i + 2, StringComparison.Ordinal);
                if (e < 0) { int e2 = t.IndexOf("\r" + end.Substring(1), i + 2, StringComparison.Ordinal); e = e2; }
                if (e >= 0) { i = e + end.Length - 1; continue; }
            }
            if (c == '\'' ) { i = SkipQuoted(t, i, '\''); continue; }
            if (c == '"') { i = SkipQuoted(t, i, '"'); continue; }
            if (c == '`') { i++; continue; }
            if (c == '(' || c == '[' || c == '{') stack.Push(c);
            else if (c == ')' || c == ']' || c == '}')
            {
                if (stack.Count == 0) return -1;
                stack.Pop();
                if (stack.Count == 0) return i;
            }
        }
        return -1;
    }

    static int SkipQuoted(string t, int i, char q)
    {
        for (int j = i + 1; j < t.Length; j++)
        {
            if (q == '"' && t[j] == '`') { j++; continue; }
            if (t[j] == q)
            {
                if (j + 1 < t.Length && t[j + 1] == q) { j++; continue; }
                return j;
            }
        }
        return t.Length - 1;
    }

    /// <summary>Splits on a separator outside brackets, strings and comments.</summary>
    static List<string> SplitTop(string s, char sep)
    {
        var parts = new List<string>();
        int depth = 0, last = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '#') { i = SkipLineComment(s, i) - 1; continue; }
            if (c == '<' && i + 1 < s.Length && s[i + 1] == '#') { i = SkipBlockComment(s, i) - 1; continue; }
            if (c == '\'' || c == '"') { i = SkipQuoted(s, i, c); continue; }
            if (c == '`') { i++; continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == sep && depth == 0) { parts.Add(s.Substring(last, i - last)); last = i + 1; }
        }
        parts.Add(s.Substring(last));
        return parts;
    }

    static string StripComments(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '#') { int e = SkipLineComment(s, i); sb.Append('\n'); i = e - 1; continue; }
            if (c == '<' && i + 1 < s.Length && s[i + 1] == '#') { i = SkipBlockComment(s, i) - 1; sb.Append(' '); continue; }
            if (c == '\'' || c == '"') { int e = SkipQuoted(s, i, c); sb.Append(s, i, e - i + 1); i = e; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    static ParamInfo? ParseOne(string raw)
    {
        string s = StripComments(raw).Trim();
        if (s.Length == 0) return null;
        var p = new ParamInfo();
        int i = 0;
        // attributes and type constraints before the variable
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length || s[i] != '[') break;
            int e = MatchBracket(s, i);
            if (e < 0) return null;
            string inner = s.Substring(i + 1, e - i - 1).Trim();
            var am = Regex.Match(inner, @"^([\w.]+)\s*\((.*)\)\s*$", RegexOptions.Singleline);
            if (am.Success)
            {
                string an = am.Groups[1].Value;
                string args = am.Groups[2].Value;
                if (Regex.IsMatch(an, @"^(System\.Management\.Automation\.)?Parameter(Attribute)?$", RegexOptions.IgnoreCase))
                {
                    foreach (var a in SplitTop(args, ','))
                    {
                        var kv = Regex.Match(a.Trim(), @"^(\w+)\s*(=\s*(.+))?$", RegexOptions.Singleline);
                        if (!kv.Success) continue;
                        string k = kv.Groups[1].Value;
                        string v = kv.Groups[3].Value.Trim();
                        if (k.Equals("Mandatory", StringComparison.OrdinalIgnoreCase))
                            p.Mandatory = !kv.Groups[2].Success || Regex.IsMatch(v, @"^\$true$|^1$", RegexOptions.IgnoreCase);
                        else if (k.Equals("HelpMessage", StringComparison.OrdinalIgnoreCase)) p.Help = Unquote(v);
                    }
                }
                else if (Regex.IsMatch(an, @"^(System\.Management\.Automation\.)?ValidateSet(Attribute)?$", RegexOptions.IgnoreCase))
                {
                    p.SetValues = SplitTop(args, ',').Select(x => Unquote(x.Trim())).Where(x => x.Length > 0 && !x.Contains('=')).ToList();
                    p.Set = string.Join(", ", p.SetValues);
                }
            }
            else if (p.Type.Length == 0 && Regex.IsMatch(inner, @"^[\w.]+(\[\])?$"))
            {
                p.Type = inner;
            }
            i = e + 1;
        }
        var vm = Regex.Match(s.Substring(i), @"^\s*\$(?:\{([^}]+)\}|([\w:]+))\s*(=\s*(.*))?$", RegexOptions.Singleline);
        if (!vm.Success) return null;
        p.Name = vm.Groups[1].Success && vm.Groups[1].Value.Length > 0 ? vm.Groups[1].Value : vm.Groups[2].Value;
        if (p.Name.Contains(':')) p.Name = p.Name.Substring(p.Name.LastIndexOf(':') + 1);
        string t = p.Type.ToLowerInvariant();
        if (t == "switch" || t == "system.management.automation.switchparameter") p.Kind = "switch";
        else if (t == "bool" || t == "system.boolean") p.Kind = "bool";
        else if (t.EndsWith("[]")) p.Kind = "array";
        if (vm.Groups[3].Success)
        {
            string d = vm.Groups[4].Value.Trim();
            p.DefaultText = d;
            if (p.Kind == "switch" || p.Kind == "bool") p.DefaultBool = Regex.IsMatch(d, @"^\$true$|^1$", RegexOptions.IgnoreCase);
            else
            {
                string? v = ConstantValue(d);
                if (v != null) p.Default = v;
            }
        }
        return p;
    }

    // Only constants (like SafeGetValue): quoted strings, numbers, $true/$false, @(...) / comma lists of those
    static string? ConstantValue(string d)
    {
        d = d.Trim();
        if (d.StartsWith("@(") && d.EndsWith(")")) d = d.Substring(2, d.Length - 3);
        var parts = SplitTop(d, ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (parts.Count == 0) return null;
        var vals = new List<string>();
        foreach (var x in parts)
        {
            if ((x.StartsWith("'") && x.EndsWith("'") && x.Length >= 2) || (x.StartsWith("\"") && x.EndsWith("\"") && x.Length >= 2 && !x.Contains('$')))
                vals.Add(Unquote(x));
            else if (Regex.IsMatch(x, @"^-?\d+(\.\d+)?$")) vals.Add(x);
            else if (x.Equals("$true", StringComparison.OrdinalIgnoreCase)) vals.Add("True");
            else if (x.Equals("$false", StringComparison.OrdinalIgnoreCase)) vals.Add("False");
            else return null;
        }
        return string.Join(", ", vals);
    }

    static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'') return v.Substring(1, v.Length - 2).Replace("''", "'");
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') return v.Substring(1, v.Length - 2).Replace("`\"", "\"").Replace("\"\"", "\"");
        return v;
    }
}

/// <summary>Reading script files like PowerShell 5.1 does: BOM-aware, UTF-8 when it decodes cleanly, else the ANSI code page.</summary>
public static class TextFile
{
    public static string ReadAllText(string path) => Decode(File.ReadAllBytes(path), out _);

    /// <summary>Encoding names: utf8bom | utf8 | ascii | utf16le | utf16be | ansi</summary>
    public static string Decode(byte[] b, out string encoding)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { encoding = "utf8bom"; return new UTF8Encoding(false).GetString(b, 3, b.Length - 3); }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { encoding = "utf16le"; return Encoding.Unicode.GetString(b, 2, b.Length - 2); }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { encoding = "utf16be"; return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); }
        if (b.All(x => x < 0x80)) { encoding = "ascii"; return Encoding.ASCII.GetString(b); }
        try { encoding = "utf8"; return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException)
        {
            encoding = "ansi";
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                int cp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                return Encoding.GetEncoding(cp).GetString(b);
            }
            catch { return Encoding.Latin1.GetString(b); }
        }
    }
}
