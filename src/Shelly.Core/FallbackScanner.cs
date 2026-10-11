using System.Text;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>The built-in safety scan used only until PowerShell's own parser (Analyzer.ps1) has reported, or when no
/// PowerShell can run it. It follows the analyzer's rules on a light tokenizer (comments dropped, strings kept apart,
/// aliases from the built-in table and Set-Alias/New-Alias, constant variables and splatting tables substituted),
/// so it is approximate: the UI shows its result as "checking…" or "not verified", never as safe.</summary>
public static class FallbackScanner
{
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["?"] = "where-object", ["%"] = "foreach-object", ["ac"] = "add-content", ["cat"] = "get-content", ["cd"] = "set-location",
        ["chdir"] = "set-location", ["clc"] = "clear-content", ["cli"] = "clear-item", ["clp"] = "clear-itemproperty", ["copy"] = "copy-item",
        ["cp"] = "copy-item", ["cpi"] = "copy-item", ["curl"] = "invoke-webrequest", ["del"] = "remove-item", ["dir"] = "get-childitem",
        ["echo"] = "write-output", ["erase"] = "remove-item", ["foreach"] = "foreach-object", ["gc"] = "get-content", ["gci"] = "get-childitem",
        ["gcm"] = "get-command", ["gi"] = "get-item", ["gwmi"] = "get-wmiobject", ["icm"] = "invoke-command", ["iex"] = "invoke-expression",
        ["ii"] = "invoke-item", ["irm"] = "invoke-restmethod", ["iwmi"] = "invoke-wmimethod", ["iwr"] = "invoke-webrequest", ["kill"] = "stop-process",
        ["ls"] = "get-childitem", ["mi"] = "move-item", ["move"] = "move-item", ["mv"] = "move-item", ["nal"] = "new-alias", ["ni"] = "new-item",
        ["rd"] = "remove-item", ["ri"] = "remove-item", ["rm"] = "remove-item", ["rmdir"] = "remove-item", ["rp"] = "remove-itemproperty",
        ["rwmi"] = "remove-wmiobject", ["sajb"] = "start-job", ["sal"] = "set-alias", ["saps"] = "start-process", ["select"] = "select-object",
        ["si"] = "set-item", ["sp"] = "set-itemproperty", ["spps"] = "stop-process", ["spsv"] = "stop-service", ["start"] = "start-process",
        ["type"] = "get-content", ["wget"] = "invoke-webrequest", ["where"] = "where-object", ["write"] = "write-output",
    };

    static readonly string[] DlCmds = { "invoke-webrequest", "invoke-restmethod", "start-bitstransfer", "curl", "wget", "bitsadmin" };
    static readonly Regex DlMember = new(@"(?i)\.(DownloadString|DownloadData|DownloadFile|DownloadStringTaskAsync|OpenRead|GetStringAsync|GetByteArrayAsync)\s*\(");
    static readonly Regex SystemPath = new(@"(?i)^[a-z]:[\\/]?(\*(\.\*)?)?$|^[\\/](\*(\.\*)?)?$|^(\$env:systemdrive|\$\{env:systemdrive\})[\\/]?(\*(\.\*)?)?$" +
        @"|^[a-z]:[\\/]windows([\\/]|$)|system32|syswow64|\$\{?env:(windir|systemroot|programfiles|programdata|commonprogramfiles)" +
        @"|program files|^[a-z]:[\\/]programdata|^[a-z]:[\\/]users[\\/]?(\*(\.\*)?)?$|^(\$env:userprofile|\$\{env:userprofile\}|\$home|~)[\\/]?(\*(\.\*)?)?$" +
        @"|^hklm:|hkey_local_machine|^[a-z]:[\\/]boot([\\/]|$)");
    static readonly Regex TempPath = new(@"(?i)^(\$env:(temp|tmp)|\$\{env:(temp|tmp)\})[\\/]+[^\\/*\s]");
    static readonly Regex ExecExt = new(@"(?i)\.(ps1|psm1|exe|com|bat|cmd|msi|vbs|js|wsf|hta|scr)$");

    sealed class Seg
    {
        public int Line;
        public string Text = "";          // code without comments, strings kept
        public string Bare = "";          // strings blanked (for keyword matching)
        public List<string> Words = new(); // tokens: strings unquoted, variables substituted
        public string Pre = "";           // earlier pipeline elements
    }

    public static List<DangerFinding> Scan(string text)
    {
        var found = new List<DangerFinding>();
        string code = StripComments(text);
        var lines = code.Split('\n');
        var srcLines = text.Split('\n');
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sbVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashVars = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var userAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dlVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dlFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scriptParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sbParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasSink = Regex.IsMatch(code, @"(?i)\b(iex|invoke-expression|icm|invoke-command)\b|scriptblock\]::create|newscriptblock|invokescript|assembly\]::load|\badd-type\b|&\s*\$");
        bool hklmNet = Regex.IsMatch(code, @"(?i)registry\]::localmachine|registryhive\]::localmachine");
        bool shadow = Regex.IsMatch(code, @"(?i)shadowcopy");
        var pm = Regex.Match(code, @"(?is)^\s*(\[[^\]]*\]\s*)*param\s*\((.*?)\)\s*$", RegexOptions.Multiline);
        if (pm.Success) foreach (Match v in Regex.Matches(pm.Groups[2].Value, @"\$(\w+)")) scriptParams.Add(v.Groups[1].Value);
        // [scriptblock] parameters of the script's own functions get script blocks from the script's own calls
        foreach (Match v in Regex.Matches(code, @"(?i)\[scriptblock\]\s*\$(\w+)"))
            if (!scriptParams.Contains(v.Groups[1].Value)) sbParams.Add(v.Groups[1].Value);

        void Add(string rule, string sev, int line, string detail)
        {
            var ex = found.FirstOrDefault(f => f.Rule == rule && f.Line == line);
            if (ex != null) { if (Rank(sev) < Rank(ex.Sev)) { ex.Sev = sev; ex.Detail = detail; } return; }
            string c = line >= 1 && line <= srcLines.Length ? srcLines[line - 1].Trim() : "";
            if (c.Length > 160) c = c.Substring(0, 157) + "...";
            if (detail.Length > 120) detail = detail.Substring(0, 117) + "...";
            found.Add(new DangerFinding { Rule = rule, Sev = sev, Line = line, Code = c, Detail = detail });
        }

        foreach (var sp in sbParams) sbVars.Add(sp);
        // ---------- segments ----------
        var segs = new List<Seg>();
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (var stmt in SplitTop(lines[i], ';'))
            {
                string pre = "";
                foreach (var part in SplitPipeline(stmt))
                {
                    string t = part.Trim();
                    if (t.Length == 0) continue;
                    segs.Add(new Seg { Line = i + 1, Text = t, Bare = BlankStrings(t), Pre = pre });
                    pre += " | " + t;
                }
            }
        }
        // ---------- context pass: variables, aliases, downloads ----------
        foreach (var s in segs)
        {
            var am = Regex.Match(s.Text, @"^\$(\w+)\s*=\s*(.+)$", RegexOptions.Singleline);
            if (am.Success)
            {
                string n = am.Groups[1].Value, rhs = am.Groups[2].Value.Trim();
                vars.Remove(n); if (!sbParams.Contains(n)) sbVars.Remove(n); hashVars.Remove(n);
                if (rhs.StartsWith("{")) sbVars.Add(n);
                else if (rhs.StartsWith("@{")) hashVars[n] = ParseHash(rhs, vars);
                else { var v = ConstValue(rhs, vars); if (v != null) vars[n] = v; }
                string? u = DownloadUrl(rhs, vars, dlVars);
                if (u != null) dlVars[n] = u;
            }
            var w = Words(s.Text, vars);
            if (w.Count >= 3 && Resolve(w[0], userAliases) is "set-alias" or "new-alias")
            {
                string? an = Param(w, "name"), av = Param(w, "value");
                var pos = w.Skip(1).Where(x => !x.StartsWith("-")).ToList();
                an ??= pos.ElementAtOrDefault(0); av ??= pos.ElementAtOrDefault(an == pos.ElementAtOrDefault(0) ? 1 : 0);
                if (an != null && av != null) userAliases[an] = av;
            }
            // files a download writes
            if (w.Count > 0 && DlCmds.Contains(Resolve(w[0], userAliases)))
            {
                string url = UrlOf(w);
                string? file = Param(w, "outfile") ?? Param(w, "destination") ?? Param(w, "o");
                if (file == null && Resolve(w[0], userAliases) == "start-bitstransfer") file = w.Skip(1).Where(x => !x.StartsWith("-")).ElementAtOrDefault(1);
                if (file != null && ExecExt.IsMatch(Leaf(file))) dlFiles[Leaf(file)] = url;
            }
            var dfm = Regex.Match(s.Text, @"(?i)\.DownloadFile\s*\(\s*(.+?)\s*,\s*(.+?)\s*\)");
            if (dfm.Success)
            {
                string f = Unq(Expand(dfm.Groups[2].Value, vars));
                if (ExecExt.IsMatch(Leaf(f))) dlFiles[Leaf(f)] = Unq(Expand(dfm.Groups[1].Value, vars));
            }
        }
        // ---------- rules ----------
        foreach (var s in segs)
        {
            int ln = s.Line;
            string bare = s.Bare;
            var w = Words(Regex.Replace(s.Text, @"^([&.]\s*)?([\w.-]+)\(", "$1$2 ("), vars);
            if (w.Count > 0 && (w[0] == "&" || w[0] == ".")) w.RemoveAt(0);
            s.Words = w;
            // .NET and other member calls (anywhere in the segment)
            ScanMembers(s, vars, dlVars, sbVars, hasSink, hklmNet, shadow, dlFiles, Add);
            string first = s.Text;
            bool amp = Regex.IsMatch(first, @"^[&.]\s");
            string cmdText = amp ? first.Substring(1).TrimStart() : first;
            if (amp)
            {
                if (cmdText.StartsWith("{")) continue;
                var vm = Regex.Match(cmdText, @"^\$(\w+)\b");
                if (vm.Success)
                {
                    string vn = vm.Groups[1].Value;
                    if (sbVars.Contains(vn)) continue;
                    if (dlVars.TryGetValue(vn, out var du)) { Add("DlExec", "high", ln, du); continue; }
                    if (vars.TryGetValue(vn, out var val) && Regex.IsMatch(val, @"[\\/]|\.\w+$")) { w[0] = val; }
                    else
                    {
                        Add("Dynamic", "high", ln, "$" + vn + (vars.TryGetValue(vn, out var v2) ? " -> " + v2 : ""));
                        if (vars.TryGetValue(vn, out var v3)) w[0] = v3; else continue;
                    }
                }
                else if (cmdText.StartsWith("("))
                {
                    string? du = DownloadUrl(cmdText, vars, dlVars);
                    if (du != null) { Add("DlExec", "high", ln, du); continue; }
                    Add("Dynamic", "high", ln, Regex.Match(cmdText, @"^\([^)]*\)?").Value);
                    continue;
                }
            }
            if (w.Count == 0 || w[0].StartsWith("$") && !amp) continue;
            string n = Resolve(w[0], userAliases);
            string disp = w[0];
            Command(n, disp, w, s, ln, vars, hashVars, dlVars, dlFiles, scriptParams, sbVars, Add, 0);
        }
        // A line that downloads and runs code says so once
        var dl = found.Where(f => f.Rule == "DlExec").Select(f => f.Line).ToHashSet();
        return Safety.Sorted(found.Where(f => !((f.Rule is "Dynamic" or "Download") && dl.Contains(f.Line))));
    }

    static int Rank(string sev) => sev == "high" ? 0 : sev == "medium" ? 1 : 2;

    delegate void AddFn(string rule, string sev, int line, string detail);

    static void Command(string n, string disp, List<string> w, Seg s, int ln, Dictionary<string, string> vars,
        Dictionary<string, Dictionary<string, string>> hashVars, Dictionary<string, string> dlVars, Dictionary<string, string> dlFiles,
        HashSet<string> scriptParams, HashSet<string> sbVars, AddFn Add, int depth)
    {
        if (depth > 3) return;
        string joined = " " + string.Join(" ", w.Skip(1).Select(x => x.ToLowerInvariant())) + " ";
        var args = w.Skip(1).ToList();
        // splatting
        var prm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool unknownSplat = false;
        for (int i = 1; i < w.Count; i++)
        {
            if (w[i].StartsWith("@") && w[i].Length > 1 && char.IsLetter(w[i][1]))
            {
                if (hashVars.TryGetValue(w[i].Substring(1), out var h)) foreach (var kv in h) prm[kv.Key] = kv.Value; else unknownSplat = true;
            }
            else if (w[i].StartsWith("-") && w[i].Length > 1 && !Regex.IsMatch(w[i], @"^-\d"))
            {
                string k = w[i].Substring(1).TrimEnd(':').ToLowerInvariant();
                prm[k] = i + 1 < w.Count && !w[i + 1].StartsWith("-") ? w[i + 1] : "$true";
            }
        }
        string? P(string full, int min = 1) { foreach (var kv in prm) if (kv.Key.Length >= min && full.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value; return null; }
        bool On(string? v) => v != null && !Regex.IsMatch(v, @"^\$?(false|0)$", RegexOptions.IgnoreCase);

        if (Runners.Contains(n) || ExecExt.IsMatch(disp))
            foreach (var x in w.Concat(prm.Values)) if (dlFiles.TryGetValue(Leaf(x), out var u)) { Add("DlExec", "high", ln, u); break; }
        if (Regex.IsMatch(disp, @"(?i)\.psm?1$")) { Add("ScriptCall", "low", ln, disp); return; }

        switch (n)
        {
            case "remove-item":
            {
                var paths = args.Where(a => !a.StartsWith("-") && !a.StartsWith("@") && !a.StartsWith("/")).ToList();
                foreach (var k in new[] { "path", "literalpath" }) { var v = P(k, k == "path" ? 1 : 2); if (v != null && v != "$true") paths.Add(v); }
                // values of -Param are also in args; drop flag values
                var sys = paths.FirstOrDefault(p => SystemPath.IsMatch(Unq(p)));
                if (sys != null) { Add("DelSys", "high", ln, sys); return; }
                bool rec = On(P("recurse")) || joined.Contains(" /s ");
                var flags = new List<string>();
                if (rec) flags.Add("-Recurse");
                if (On(P("force", 2)) || joined.Contains(" /q ") || joined.Contains(" /f ")) flags.Add("-Force");
                var wild = paths.FirstOrDefault(p => p.Contains('*'));
                if (wild != null) flags.Add(wild);
                if (unknownSplat) { Add("Del", "medium", ln, "parameters hidden in a splatted variable"); return; }
                if (flags.Count == 0) return;
                bool allTemp = paths.Count > 0 && paths.All(p => TempPath.IsMatch(Unq(p)));
                Add("Del", (!rec && wild == null) || (allTemp && !rec) ? "low" : "medium", ln, string.Join(", ", flags));
                return;
            }
            case "format-volume" or "clear-disk" or "initialize-disk" or "remove-partition" or "diskpart" or "format":
                Add("Disk", "high", ln, disp); return;
            case "bcdedit":
                if (Regex.IsMatch(joined, @" /(set|delete|deletevalue|import|create|copy|default|bootsequence|displayorder|timeout|debug|bootdebug|clean|store)\b")) Add("Boot", "high", ln, "bcdedit");
                return;
            case "stop-computer" or "restart-computer": Add("Power", "high", ln, disp); return;
            case "shutdown": if (Regex.IsMatch(joined, @" /(s|r|p|g|sg|h|fw)\b")) Add("Power", "high", ln, "shutdown"); return;
            case "set-itemproperty" or "new-itemproperty" or "remove-itemproperty" or "rename-itemproperty" or "clear-itemproperty" or "new-item" or "set-item":
                if (Regex.IsMatch(s.Text + " " + string.Join(" ", w), @"(?i)HKLM:|HKEY_LOCAL_MACHINE")) Add("RegWrite", "medium", ln, disp);
                return;
            case "reg":
            {
                string sub = args.FirstOrDefault()?.ToLowerInvariant() ?? "";
                if (sub == "delete" && Regex.IsMatch(joined, @" (hklm|hkey_local_machine)")) Add("RegDel", "high", ln, "reg delete");
                else if (sub is "add" or "delete" or "import" or "restore" or "load" or "unload" or "copy") Add("RegWrite", "medium", ln, "reg " + sub);
                return;
            }
            case "set-executionpolicy":
            {
                var pol = (P("executionpolicy") is { } ep ? new[] { ep } : Array.Empty<string>()).Concat(args)
                    .FirstOrDefault(a => Regex.IsMatch(a, "^(?i)(unrestricted|bypass|remotesigned|allsigned|restricted|default|undefined)$")) ?? "";
                if (Regex.IsMatch(pol, "^(?i)(restricted|allsigned|default|undefined)$")) return;
                string scope = P("scope") ?? "";
                Add("ExecPolicy", Regex.IsMatch(scope, "^(?i)process") || pol.Equals("remotesigned", StringComparison.OrdinalIgnoreCase) ? "medium" : "high", ln, pol + (scope.Length > 0 ? ", -Scope " + scope : ""));
                return;
            }
            case "set-mppreference" or "add-mppreference":
            {
                var bad = prm.Where(kv => kv.Key.StartsWith("exclusion") || (kv.Key.StartsWith("disable") && On(kv.Value))).Select(kv => "-" + kv.Key).ToList();
                if (bad.Count > 0) Add("Defender", "high", ln, string.Join(", ", bad));
                else if (unknownSplat) Add("Defender", "medium", ln, "parameters hidden in a splatted variable");
                return;
            }
            case "netsh":
                if (Regex.IsMatch(joined, @"advfirewall .*\bstate +off") || Regex.IsMatch(joined, @" firewall .*opmode +(mode=)?disable")) Add("Firewall", "high", ln, "netsh");
                else if (Regex.IsMatch(joined, @" interface .*\bdisable")) Add("NetAdapter", "medium", ln, "netsh interface");
                return;
            case "set-netfirewallprofile": if (Regex.IsMatch(P("enabled", 2) ?? "", @"^\$?(false|0)$", RegexOptions.IgnoreCase)) Add("Firewall", "high", ln, "Set-NetFirewallProfile -Enabled False"); return;
            case "disable-netadapter": Add("NetAdapter", "medium", ln, disp); return;
            case "remove-service": Add("SvcDelete", "high", ln, "Remove-Service"); return;
            case "stop-service" or "set-service":
            {
                if (n == "set-service" && !Regex.IsMatch(joined, " (disabled|manual|stopped) ")) return;
                string sev = ServiceSev(string.Join(" ", args));
                if (sev.Length > 0) Add("Service", sev, ln, disp + " " + string.Join(" ", args));
                return;
            }
            case "sc" or "net" or "net1":
            {
                string sub = args.FirstOrDefault()?.ToLowerInvariant() ?? "";
                if (n == "sc" && !disp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && sub is not ("delete" or "stop" or "config" or "create" or "pause")) return;   // Set-Content
                string rest = string.Join(" ", args.Skip(1));
                if (n == "sc" && sub == "delete") { Add("SvcDelete", "high", ln, ("sc delete " + rest).Trim()); return; }
                if (sub is "stop" or "config" or "pause") { string sev = ServiceSev(rest); if (sev.Length > 0) Add("Service", sev, ln, n + " " + sub + " " + rest); return; }
                if (n != "sc" && sub == "user" && (Regex.IsMatch(joined, " /(add|delete|del|active)") || args.Count >= 3)) Add("Account", "high", ln, "net user");
                if (n != "sc" && sub == "localgroup" && Regex.IsMatch(joined, " /(add|delete|del)") && Regex.IsMatch(joined, "admin|администратор")) Add("Account", "high", ln, "net localgroup");
                return;
            }
            case "takeown" or "icacls":
                if (n == "icacls" && !Regex.IsMatch(joined, " /(grant|deny|reset|setowner|remove|inheritance|setintegritylevel|restore|substitute)")) return;
                Add("Acl", args.Any(a => SystemPath.IsMatch(Unq(a))) ? "high" : "medium", ln, disp); return;
            case "set-acl": if (args.Concat(prm.Values).Any(a => SystemPath.IsMatch(Unq(a)))) Add("Acl", "high", ln, "Set-Acl"); return;
            case "cipher": if (joined.Contains(" /w")) Add("Wipe", "high", ln, "cipher /w"); return;
            case "vssadmin": if (Regex.IsMatch(joined, " (delete|resize) ") && joined.Contains(" shadow")) Add("Shadows", "high", ln, disp); return;
            case "wmic": if (joined.Contains("shadowcopy") && joined.Contains(" delete")) Add("Shadows", "high", ln, disp); return;
            case "wbadmin": if (joined.Contains(" delete ")) Add("Shadows", "high", ln, disp); return;
            case "remove-ciminstance" or "remove-wmiobject": if (Regex.IsMatch(s.Pre + s.Text, "(?i)shadowcopy")) Add("Shadows", "high", ln, disp); return;
            case "invoke-cimmethod" or "invoke-wmimethod":
            {
                string mn = P("methodname") ?? P("name") ?? "";
                if (Regex.IsMatch(mn, "^(?i)(win32shutdown|win32shutdowntracker|shutdown|reboot)$")) Add("Power", "high", ln, disp + " " + mn);
                else if (Regex.IsMatch(s.Pre + s.Text, "(?i)shadowcopy") && mn.Equals("delete", StringComparison.OrdinalIgnoreCase)) Add("Shadows", "high", ln, disp);
                return;
            }
            case "clear-eventlog" or "remove-eventlog": Add("Logs", "high", ln, disp); return;
            case "wevtutil": if (Regex.IsMatch(args.FirstOrDefault() ?? "", "^(?i)(cl|clear-log|um|uninstall-manifest|sl|set-log)$")) Add("Logs", "high", ln, "wevtutil " + args[0]); return;
            case "schtasks":
                if (Regex.IsMatch(joined, " /(create|change) ")) Add("Task", Regex.IsMatch(joined, @" /ru +(system|""?nt authority\\system""?)\b| /rl +highest") ? "high" : "medium", ln, "schtasks");
                else if (joined.Contains(" /delete ")) Add("Task", "medium", ln, "schtasks /delete");
                return;
            case "register-scheduledtask": Add("Task", Regex.IsMatch(s.Text, "(?i)highest|system") ? "high" : "medium", ln, disp); return;
            case "invoke-expression":
            {
                string? u = DownloadUrl(s.Text, vars, dlVars) ?? DownloadUrl(s.Pre, vars, dlVars);
                if (u != null) { Add("DlExec", "high", ln, u); return; }
                Add("Dynamic", "high", ln, "Invoke-Expression");
                var arg = args.FirstOrDefault();
                if (arg != null && !arg.StartsWith("$") && Regex.IsMatch(s.Text, @"(?i)^\S+\s+(-command\s+)?['""]")) Rescan(arg, ln, vars, hashVars, dlVars, dlFiles, scriptParams, sbVars, Add, depth);
                return;
            }
            case "invoke-command" or "start-job" or "start-threadjob":
            {
                var m = Regex.Match(s.Text, @"(?i)\s-s(c(r(i(p(t(b(l(o(c(k)?)?)?)?)?)?)?)?)?)?\s*:?\s*(\S+)");
                string? sb = m.Success ? m.Groups[11].Value : (args.Count > 0 && !args[0].StartsWith("-") ? Regex.Match(s.Text, @"^\S+\s+(\S+)").Groups[1].Value : null);
                if (sb != null && !sb.StartsWith("{") && !(sb.StartsWith("$") && sbVars.Contains(sb.Substring(1))))
                {
                    string? u = DownloadUrl(s.Text, vars, dlVars);
                    if (u != null) Add("DlExec", "high", ln, u); else Add("Dynamic", "high", ln, disp + " " + sb);
                }
                return;
            }
            case "add-type":
            {
                bool hasCode = prm.Keys.Any(k => k.Length >= 2 && new[] { "typedefinition", "memberdefinition", "path", "literalpath" }.Any(f => f.StartsWith(k)));
                bool positional = Regex.IsMatch(s.Text, @"(?i)^\S+\s+(@?['""]|\$)");
                if (!hasCode && !positional) return;
                bool fromVar = Regex.IsMatch(s.Text, @"(?i)-(typedefinition|memberdefinition|path|literalpath)\s+\$|^\S+\s+\$|-(path|literalpath)\s");
                Add(fromVar ? "Dynamic" : "AddType", fromVar ? "high" : "medium", ln, "Add-Type");
                return;
            }
            case "remove-localuser" or "new-localuser" or "set-localuser" or "disable-localuser" or "rename-localuser": Add("Account", "high", ln, disp); return;
            case "add-localgroupmember" or "remove-localgroupmember": if (Regex.IsMatch(s.Text, "(?i)admin|администратор|S-1-5-32-544")) Add("Account", "high", ln, disp); return;
            case "powershell" or "pwsh":
            {
                foreach (var k in prm.Keys) if (k == "ec" || "encodedcommand".StartsWith(k)) Add("Encoded", "high", ln, "-" + k);
                string? code = P("command");
                if (code != null && code != "$true") Rescan(code, ln, vars, hashVars, dlVars, dlFiles, scriptParams, sbVars, Add, depth);
                return;
            }
            case "start-process":
            {
                if (Regex.IsMatch(P("verb") ?? "", "^(?i)runas$")) Add("Elevate", "medium", ln, "Start-Process -Verb RunAs");
                string? file = P("filepath", 2);
                if (file == null || file == "$true") file = args.FirstOrDefault(a => !a.StartsWith("-"));
                string al = P("argumentlist", 2) ?? args.Where(a => !a.StartsWith("-")).Skip(1).FirstOrDefault() ?? "";
                if (file != null)
                {
                    var tk = new List<string> { file };
                    tk.AddRange(SplitText(al));
                    Command(ResolveNative(tk[0]), tk[0], tk, s, ln, vars, hashVars, dlVars, dlFiles, scriptParams, sbVars, Add, depth + 1);
                }
                return;
            }
            case "cmd":
            {
                int idx = args.FindIndex(a => Regex.IsMatch(a, "^(?i)/[ck]$"));
                if (idx < 0 || idx + 1 >= args.Count) return;
                string rest = string.Join(" ", args.Skip(idx + 1));
                foreach (var seg in Regex.Split(rest, @"&&|\|\||&|\|"))
                {
                    var tk = SplitText(seg);
                    if (tk.Count > 0) Command(ResolveNative(tk[0]), tk[0], tk, s, ln, vars, hashVars, dlVars, dlFiles, scriptParams, sbVars, Add, depth + 1);
                }
                return;
            }
        }
    }

    static readonly string[] Runners = { "start-process", "invoke-item", "msiexec", "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32" };

    static void Rescan(string code, int ln, Dictionary<string, string> vars, Dictionary<string, Dictionary<string, string>> hashVars, Dictionary<string, string> dlVars,
        Dictionary<string, string> dlFiles, HashSet<string> scriptParams, HashSet<string> sbVars, AddFn Add, int depth)
    {
        foreach (var f in Scan(code)) Add(f.Rule, f.Sev, ln, f.Detail);
    }

    static void ScanMembers(Seg s, Dictionary<string, string> vars, Dictionary<string, string> dlVars, HashSet<string> sbVars, bool hasSink, bool hklmNet,
        bool shadow, Dictionary<string, string> dlFiles, AddFn Add)
    {
        string t = s.Text, b = s.Bare;
        int ln = s.Line;
        if (Regex.IsMatch(b, @"(?i)\[(system\.management\.automation\.)?scriptblock\]::create\s*\("))
        {
            string? u = DownloadUrl(t, vars, dlVars);
            if (u != null) Add("DlExec", "high", ln, u); else Add("Dynamic", "high", ln, "[scriptblock]::Create");
        }
        if (Regex.IsMatch(b, @"(?i)\$executioncontext\.invokecommand\.(invokescript|newscriptblock|expandstring)"))
        {
            string? u = DownloadUrl(t, vars, dlVars);
            if (u != null) Add("DlExec", "high", ln, u); else Add("Dynamic", "high", ln, "$ExecutionContext.InvokeCommand");
        }
        foreach (Match m in Regex.Matches(b, @"(?i)(\$\w+|\))\.(invokereturnasis|invokewithcontext|dynamicinvoke|invoke)\s*\("))
        {
            string target = m.Groups[1].Value, mn = m.Groups[2].Value.ToLowerInvariant();
            if (target == ")")
            {
                if (Regex.IsMatch(b.Substring(0, m.Index + 1), @"(?i)\.(getmethod|getmethods|getmember|getproperty|getdelegateforfunctionpointer|createdelegate)\s*\([^()]*\)$")) Add("Dynamic", "high", ln, "reflection Invoke");
            }
            else if (!sbVars.Contains(target.Substring(1)) && mn != "invoke") Add("Dynamic", "high", ln, target + "." + m.Groups[2].Value + "()");
        }
        // & $x / & (...) inside a script block or later in the line
        foreach (Match m in Regex.Matches(b, @"(?<=[{(;|]\s*)[&.]\s*(\$(\w+)|\()"))
        {
            if (m.Index == 0) continue;
            if (m.Groups[2].Success)
            {
                string vn = m.Groups[2].Value;
                if (sbVars.Contains(vn) || (vars.TryGetValue(vn, out var pv) && Regex.IsMatch(pv, @"[\\/]|\.\w+$"))) continue;
                if (dlVars.TryGetValue(vn, out var du)) Add("DlExec", "high", ln, du); else Add("Dynamic", "high", ln, "$" + vn);
            }
            else
            {
                string? du = DownloadUrl(t.Substring(m.Index), vars, dlVars);
                if (du != null) Add("DlExec", "high", ln, du); else Add("Dynamic", "high", ln, t.Substring(m.Index, Math.Min(40, t.Length - m.Index)));
            }
        }
        if (Regex.IsMatch(b, @"(?i)\.invokemember\s*\(")) Add("Dynamic", "high", ln, "reflection: InvokeMember");
        foreach (Match m in Regex.Matches(t, @"(?i)\[(system\.)?reflection\.assembly\]::(load|loadfrom|loadfile|unsafeloadfrom)\s*\(\s*(.)"))
            if (!(m.Groups[2].Value.Equals("load", StringComparison.OrdinalIgnoreCase) && m.Groups[3].Value is "'" or "\"")) Add("Dynamic", "high", ln, "[Reflection.Assembly]::" + m.Groups[2].Value);
        if (Regex.IsMatch(b, @"(?i)interopservices\.marshal\]::getdelegateforfunctionpointer")) Add("Dynamic", "high", ln, "Marshal.GetDelegateForFunctionPointer");
        if (Regex.IsMatch(b, @"(?i)::frombase64string\s*\(")) Add("Encoded", hasSink ? "high" : "low", ln, "FromBase64String");
        var dm = DlMember.Match(b);
        if (dm.Success) Add("Download", "low", ln, DownloadUrl(t, vars, dlVars) ?? "?");
        foreach (Match m in Regex.Matches(t, @"(?i)\[(system\.)?io\.(file|directory)\]::delete\s*\(\s*([^,)]*)\s*(,\s*([^)]*))?\)"))
        {
            string p = Unq(Expand(m.Groups[3].Value, vars));
            bool rec = m.Groups[2].Value.Equals("directory", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(m.Groups[5].Value, @"(?i)\$true|1");
            string label = "[IO." + m.Groups[2].Value + "]::Delete " + p;
            if (SystemPath.IsMatch(p)) Add("NetDelete", "high", ln, label);
            else if (TempPath.IsMatch(p)) { }
            else Add("NetDelete", rec ? "medium" : "low", ln, label);
        }
        foreach (Match m in Regex.Matches(t, @"(?i)visualbasic\.fileio\.filesystem\]::(deletedirectory|deletefile)\s*\(\s*([^,)]*)"))
        {
            string p = Unq(Expand(m.Groups[2].Value, vars));
            Add("NetDelete", SystemPath.IsMatch(p) ? "high" : m.Groups[1].Value.Equals("deletedirectory", StringComparison.OrdinalIgnoreCase) ? "medium" : "low", ln, "FileSystem." + m.Groups[1].Value + " " + p);
        }
        if (Regex.IsMatch(b, @"(?i)\)\.delete\s*\(\s*\$true") || Regex.IsMatch(b, @"(?i)\$\w+\.delete\s*\(\s*\$true")) Add("NetDelete", "medium", ln, "Delete($true)");
        if (shadow && Regex.IsMatch(b, @"(?i)\.delete\s*\(\s*\)")) Add("Shadows", "high", ln, "Win32_ShadowCopy.Delete()");
        var rd = Regex.Match(b, @"(?i)\.(deletesubkeytree|deletesubkey|deletevalue)\s*\(");
        if (rd.Success) { if (Regex.IsMatch(b, @"(?i)localmachine|classesroot|\]::users") || (hklmNet && !Regex.IsMatch(b, "(?i)currentuser"))) Add("RegDel", "high", ln, rd.Groups[1].Value); else Add("RegWrite", "medium", ln, rd.Groups[1].Value); }
        if (hklmNet && Regex.IsMatch(b, @"(?i)\.(setvalue|createsubkey)\s*\(") && !Regex.IsMatch(b, "(?i)currentuser")) Add("RegWrite", "medium", ln, "HKLM SetValue");
        var ps = Regex.Match(t, @"(?i)\[(system\.)?diagnostics\.process\]::start\s*\(\s*([^,)]+)\s*(,\s*([^)]*))?\)");
        if (ps.Success)
        {
            var tk = new List<string> { Unq(Expand(ps.Groups[2].Value, vars)) };
            tk.AddRange(SplitText(Unq(Expand(ps.Groups[4].Value, vars))));
            foreach (var x in tk) if (dlFiles.TryGetValue(Leaf(x), out var u)) Add("DlExec", "high", ln, u);
            Command(ResolveNative(tk[0]), tk[0], tk, s, ln, vars, new(), dlVars, dlFiles, new(), sbVars, Add, 1);
        }
        if (Regex.IsMatch(t, @"(?i)(^|[\s'""])[-/](e|ec|en|enc|enco|encod|encode|encoded|encodedc[a-z]*)\s+[A-Za-z0-9+/=]{16,}") && Regex.IsMatch(t, @"(?i)powershell|pwsh|['""]")) Add("Encoded", "high", ln, "-EncodedCommand");
    }

    // ---------- downloads ----------
    static string? DownloadUrl(string text, Dictionary<string, string> vars, Dictionary<string, string> dlVars)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string bare = BlankStrings(text);
        var m = DlMember.Match(bare);
        if (m.Success)
        {
            var am = Regex.Match(text.Substring(m.Index + m.Length), @"^\s*([^)]*)\)");
            return am.Success ? Unq(Expand(am.Groups[1].Value.Split(',')[0], vars)) : "?";
        }
        var cm = Regex.Match(bare, @"(?i)(^|[\s(|;{]|\$\()(invoke-webrequest|invoke-restmethod|iwr|irm|curl(\.exe)?|wget(\.exe)?|start-bitstransfer)(?=\s|$)");
        if (cm.Success)
        {
            string after = text.Substring(cm.Index + cm.Length);
            string afterBare = bare.Substring(cm.Index + cm.Length);
            int stop = afterBare.IndexOfAny(new[] { ')', '|', ';' });
            var rest = Words(stop >= 0 ? after.Substring(0, stop) : after, vars);
            var tmp = new List<string> { "x" }; tmp.AddRange(rest.TakeWhile(x => x != "|" && x != ")"));
            return UrlOf(tmp);
        }
        foreach (Match v in Regex.Matches(bare, @"\$(\w+)"))
            if (dlVars.TryGetValue(v.Groups[1].Value, out var u)) return u;
        return null;
    }

    static string UrlOf(List<string> w)
    {
        string? u = Param(w, "uri") ?? Param(w, "source");
        u ??= w.Skip(1).FirstOrDefault(x => Regex.IsMatch(x, "^(?i)(https?|ftp)://"));
        u ??= w.Skip(1).FirstOrDefault(x => !x.StartsWith("-") && !x.StartsWith("/") && x != ")" && x != "|");
        return u == null ? "?" : Unq(u).TrimEnd(')');
    }

    static string? Param(List<string> w, string full)
    {
        for (int i = 1; i < w.Count - 1; i++)
            if (w[i].StartsWith("-") && w[i].Length > 1 && full.StartsWith(w[i].Substring(1).TrimEnd(':'), StringComparison.OrdinalIgnoreCase) && !w[i + 1].StartsWith("-"))
                return w[i + 1];
        return null;
    }

    // ---------- names ----------
    static string Resolve(string name, Dictionary<string, string> userAliases)
    {
        string leaf = Leaf(name);
        bool exe = Regex.IsMatch(leaf, @"\.(exe|com)$");
        leaf = Regex.Replace(leaf, @"\.(exe|com|cmd|bat)$", "");
        if (exe) return leaf;
        for (int k = 0; k < 10 && userAliases.TryGetValue(leaf, out var v); k++) leaf = Regex.Replace(Leaf(v), @"\.(exe|com|cmd|bat)$", "");
        if (leaf == "sc") return "sc";
        return Aliases.TryGetValue(leaf, out var a) ? a : leaf;
    }

    static string ResolveNative(string name)
    {
        string leaf = Regex.Replace(Leaf(name), @"\.(exe|com|cmd|bat)$", "");
        return leaf is "del" or "erase" or "rd" or "rmdir" ? "remove-item" : leaf;
    }

    static string Leaf(string name)
    {
        string n = Unq(name).Trim().ToLowerInvariant();
        int i = n.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 ? n.Substring(i + 1) : n;
    }

    static string ServiceSev(string names)
    {
        string[] sec = { "windefend", "wdnissvc", "wdfilter", "sense", "securityhealthservice", "wscsvc", "mpssvc", "bfe", "eventlog", "wuauserv", "usosvc", "waasmedicsvc", "appidsvc", "vss", "srservice", "swprv" };
        string[] core = { "rpcss", "dcomlaunch", "winmgmt", "cryptsvc", "dhcp", "dnscache", "nsi", "lanmanworkstation", "lanmanserver", "schedule", "samss", "lsm", "gpsvc", "trustedinstaller", "bits", "profsvc", "power", "plugplay", "eventsystem", "netlogon", "w32time", "termservice", "wlansvc" };
        string sev = "";
        foreach (Match m in Regex.Matches(names.ToLowerInvariant(), @"[a-z0-9_.-]+"))
        {
            if (sec.Contains(m.Value)) return "high";
            if (core.Contains(m.Value)) sev = "medium";
        }
        return sev;
    }

    // ---------- text helpers ----------
    static string Unq(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length >= 2 && (s[0] == '\'' || s[0] == '"') && s[^1] == s[0]) return s.Substring(1, s.Length - 2);
        return s.Trim('"', '\'');
    }

    static string Expand(string s, Dictionary<string, string> vars)
    {
        s = s.Trim();
        if (s.StartsWith("'")) return s;
        return Regex.Replace(s, @"\$\{?(\w+)\}?", m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    /// <summary>A constant value: 'x', "x$known", 'a'+'b', '{0}{1}' -f 'a','b', -join ('a','b'), $known.</summary>
    static string? ConstValue(string rhs, Dictionary<string, string> vars)
    {
        rhs = rhs.Trim();
        while (rhs.StartsWith("(") && rhs.EndsWith(")") && FallbackParser.MatchBracket(rhs, 0) == rhs.Length - 1) rhs = rhs.Substring(1, rhs.Length - 2).Trim();
        var cast = Regex.Match(rhs, @"^\[string\]\s*(.+)$", RegexOptions.IgnoreCase);
        if (cast.Success) rhs = cast.Groups[1].Value;
        if (Regex.IsMatch(rhs, @"^'([^']|'')*'$")) return rhs.Substring(1, rhs.Length - 2).Replace("''", "'");
        if (Regex.IsMatch(rhs, @"^""([^""`]|`.)*""$") && !rhs.Contains("$(")) return Expand(rhs, vars).Trim('"');
        var vm = Regex.Match(rhs, @"^\$(\w+)$");
        if (vm.Success) return vars.TryGetValue(vm.Groups[1].Value, out var v) ? v : null;
        var parts = SplitTop(rhs, '+');
        if (parts.Count > 1)
        {
            var vals = parts.Select(p => ConstValue(p, vars)).ToList();
            return vals.All(x => x != null) ? string.Concat(vals) : null;
        }
        var fm = Regex.Match(rhs, @"^(.+?)\s+-f\s+(.+)$", RegexOptions.IgnoreCase);
        if (fm.Success)
        {
            var f = ConstValue(fm.Groups[1].Value, vars);
            var items = SplitTop(fm.Groups[2].Value.Trim().TrimStart('(').TrimEnd(')'), ',').Select(p => ConstValue(p, vars)).ToList();
            if (f != null && items.All(x => x != null)) try { return string.Format(f, items.Cast<object>().ToArray()); } catch { }
            return null;
        }
        var jm = Regex.Match(rhs, @"^-join\s*\((.+)\)$", RegexOptions.IgnoreCase);
        if (jm.Success)
        {
            var items = SplitTop(jm.Groups[1].Value, ',').Select(p => ConstValue(p, vars)).ToList();
            return items.All(x => x != null) ? string.Concat(items) : null;
        }
        return null;
    }

    static Dictionary<string, string> ParseHash(string rhs, Dictionary<string, string> vars)
    {
        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int close = FallbackParser.MatchBracket(rhs, 1);
        string body = close > 1 ? rhs.Substring(2, close - 2) : rhs.Substring(2);
        foreach (var kv in SplitTop(body, ';').SelectMany(x => x.Split('\n')))
        {
            var m = Regex.Match(kv.Trim(), @"^['""]?(\w+)['""]?\s*=\s*(.+)$");
            if (m.Success) h[m.Groups[1].Value.ToLowerInvariant()] = ConstValue(m.Groups[2].Value, vars) ?? m.Groups[2].Value.Trim();
        }
        return h;
    }

    /// <summary>Tokens of a command: quoted strings unquoted (expandable ones with known variables), others as written.</summary>
    static List<string> Words(string s, Dictionary<string, string> vars)
    {
        var res = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (c == '@' && i + 1 < s.Length && (s[i + 1] == '\'' || s[i + 1] == '"'))
            {
                string end = s[i + 1] + "@";
                int e = s.IndexOf(end, i + 2, StringComparison.Ordinal);
                if (e < 0) e = s.Length - 2;
                res.Add(s.Substring(i + 2, Math.Max(0, e - i - 2)).Trim());
                i = e + 2; continue;
            }
            if (c == '\'' || c == '"')
            {
                int e = SkipQuoted(s, i, c);
                string raw = s.Substring(i, Math.Min(s.Length, e + 1) - i);
                res.Add(c == '"' ? Unq(Expand(raw, vars)) : Unq(raw));
                i = e + 1; continue;
            }
            int j = i;
            while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] != ',')
            {
                if (s[j] == '(' || s[j] == '{' || s[j] == '[') { int e = FallbackParser.MatchBracket(s, j); if (e < 0) { j = s.Length; break; } j = e + 1; continue; }
                if (s[j] == '\'' || s[j] == '"') { j = SkipQuoted(s, j, s[j]) + 1; continue; }
                j++;
            }
            string tok = s.Substring(i, Math.Min(j, s.Length) - i);
            var vm = Regex.Match(tok, @"^\$(\w+)$");
            if (vm.Success && vars.TryGetValue(vm.Groups[1].Value, out var v)) tok = v;
            else if (tok.Contains('$') && !tok.StartsWith("$") && !tok.StartsWith("@")) tok = Expand(tok, vars);
            res.Add(tok);
            i = j;
        }
        return res;
    }

    static List<string> SplitText(string text) =>
        Regex.Matches(text ?? "", @"""[^""]*""|'[^']*'|[^\s""',]+").Select(m => m.Value.Trim('"', '\'')).Where(x => x.Length > 0).ToList();

    static int SkipQuoted(string t, int i, char q)
    {
        for (int j = i + 1; j < t.Length; j++)
        {
            if (q == '"' && t[j] == '`') { j++; continue; }
            if (t[j] == q) { if (j + 1 < t.Length && t[j + 1] == q) { j++; continue; } return j; }
        }
        return t.Length - 1;
    }

    static string StripComments(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '<' && i + 1 < s.Length && s[i + 1] == '#')
            {
                int e = s.IndexOf("#>", i + 2, StringComparison.Ordinal);
                if (e < 0) e = s.Length - 2;
                foreach (char x in s.AsSpan(i, e + 2 - i)) if (x == '\n') sb.Append('\n');
                i = e + 1; continue;
            }
            if (c == '#' && (i == 0 || char.IsWhiteSpace(s[i - 1]) || s[i - 1] == ';'))
            {
                int e = s.IndexOf('\n', i);
                if (e < 0) break;
                i = e - 1; continue;
            }
            if (c == '@' && i + 1 < s.Length && (s[i + 1] == '"' || s[i + 1] == '\''))
            {
                // here-string: kept on one line so its content stays with its command
                string end = "\n" + s[i + 1] + "@";
                int e = s.IndexOf(end, i + 2, StringComparison.Ordinal);
                if (e >= 0)
                {
                    string body = s.Substring(i + 2, e - i - 2).Replace("\r", "").Replace("\n", " ");
                    char q = s[i + 1];
                    sb.Append(q).Append(body.Replace(q.ToString(), "")).Append(q);
                    int nl = s.Substring(i, e + end.Length - i).Count(x => x == '\n');
                    sb.Append('\n', nl);
                    i = e + end.Length - 1; continue;
                }
            }
            if (c == '\'' || c == '"') { int e = SkipQuoted(s, i, c); sb.Append(s, i, Math.Min(s.Length, e + 1) - i); i = e; continue; }
            if (c == '\r') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    static string BlankStrings(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\'' || c == '"')
            {
                int e = Math.Min(s.Length - 1, SkipQuoted(s, i, c));
                sb.Append(c).Append(' ', Math.Max(0, e - i - 1)).Append(e > i ? s[e] : ' ');
                i = e; continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    static List<string> SplitTop(string s, char sep)
    {
        var parts = new List<string>();
        int depth = 0, last = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\'' || c == '"') { i = SkipQuoted(s, i, c); continue; }
            if (c == '`') { i++; continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == sep && depth == 0) { parts.Add(s.Substring(last, i - last)); last = i + 1; }
        }
        parts.Add(s.Substring(Math.Min(last, s.Length)));
        return parts;
    }

    /// <summary>Pipeline elements; '||' (PowerShell 7) is not a pipe.</summary>
    static List<string> SplitPipeline(string s)
    {
        var parts = new List<string>();
        int depth = 0, last = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\'' || c == '"') { i = SkipQuoted(s, i, c); continue; }
            if (c == '`') { i++; continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == '|' && depth == 0)
            {
                if (i + 1 < s.Length && s[i + 1] == '|') { i++; continue; }
                parts.Add(s.Substring(last, i - last)); last = i + 1;
            }
        }
        parts.Add(s.Substring(Math.Min(last, s.Length)));
        return parts;
    }
}
