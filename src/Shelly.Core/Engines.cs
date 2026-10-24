using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Shelly.Core;

/// <summary>The engine a script runs on and why (the Auto rules).</summary>
public sealed class EngineInfo
{
    public string Engine = "5.1";
    public bool Auto = true;
    /// <summary>manual | erReq | er7 | erParse51 | erWinPs | erParse7 | erDefault | erPrefer7 | erNo7</summary>
    public string Reason = "";
    public string Data = "";
    /// <summary>PowerShell 7 is needed but not installed.</summary>
    public bool Missing;
    /// <summary>The script itself requires PowerShell 7 (not just preferred).</summary>
    public bool Required;
    public List<ParseError>? Errors;
    public List<string> Hints7 = new();
    public List<string> HintsWin = new();
    public bool Pending;

    /// <summary>The engine value of a Python script (history entries and the meta of .py files).</summary>
    public const string Python = "py";
    /// <summary>The engine value of a batch file (cmd.exe, 1.3.0).</summary>
    public const string Batch = "bat";
    /// <summary>The engine value of a custom interpreter's script (1.3.0).</summary>
    public const string Custom = "x";

    public static string Name(string engine) => engine switch
    {
        Python => "Python", Batch => Loc.T("engBatch"), Custom => Loc.T("engCustom"), "7" => Loc.T("eng7"), _ => Loc.T("eng51")
    };
    public static string Short(string engine) => engine switch { Python => "py", Batch => "bat", Custom => "x", "7" => "7", _ => "5.1" };

    /// <summary>The language mark of a Python script row: always shown ("PY"), a problem when no interpreter was found.</summary>
    public static (bool Show, string Text, bool Problem) PythonMark(bool pythonMissing) => (true, "PY", pythonMissing);

    /// <summary>The engine mark of a script row, shown only when it tells something: the script runs on Windows
    /// PowerShell 5.1, the engine was set by hand, or the engine it needs is not installed. Auto → 7 shows nothing.</summary>
    public (bool Show, string Text, bool Problem) RowMark(bool pwshOk, bool ps51Ok)
    {
        bool problem = Missing || (Engine == "7" ? !pwshOk : !ps51Ok);
        bool show = Engine == "5.1" || !Auto || problem;
        return (show, show ? "PS " + Short(Engine) : "", problem);
    }

    public string ReasonText()
    {
        if (Reason.Length == 0 || Reason == "manual") return "";
        string t = Loc.T(Reason);
        return t.Contains("{0}") ? string.Format(t, Data) : t;
    }
}

public static class EngineResolver
{
    /// <summary>choice: auto | 7 | 5.1; def: engineDefault setting; err51 / err7: parse errors of each engine (null = unknown).</summary>
    public static EngineInfo Resolve(string choice, string def, bool pwshOk, RequiresInfo? req, IEnumerable<string> hints7, IEnumerable<string> hintsWin,
        IReadOnlyCollection<ParseError>? err51, IReadOnlyCollection<ParseError>? err7)
    {
        var r = new EngineInfo();
        var h7 = hints7.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var hw = hintsWin.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        r.Hints7 = h7; r.HintsWin = hw;
        if (choice == "7" || choice == "5.1")
        {
            r.Engine = choice; r.Auto = false; r.Reason = "manual";
            r.Missing = choice == "7" && !pwshOk;
            return r;
        }
        var eds = req?.Editions ?? new List<string>();
        var ver = req?.ParsedVersion;
        (string Reason, string Data)? need7 = null;
        if (ver != null && ver.Major >= 6) need7 = ("erReq", "#Requires -Version " + req!.Version);
        else if (eds.Contains("Core") && !eds.Contains("Desktop")) need7 = ("erReq", "#Requires -PSEdition Core");
        else if (eds.Contains("Desktop") && !eds.Contains("Core")) { r.Reason = "erReq"; r.Data = "#Requires -PSEdition Desktop"; return r; }
        else if (ver != null) { r.Reason = "erReq"; r.Data = "#Requires -Version " + req!.Version; return r; }
        else if (h7.Count > 0) need7 = ("er7", string.Join(", ", h7.Take(3)));
        else if (err51 != null && err51.Count > 0 && err7 != null && err7.Count == 0) need7 = ("erParse51", "");
        if (need7 != null)
        {
            r.Engine = "7"; r.Reason = need7.Value.Reason; r.Data = need7.Value.Data; r.Required = true;
            r.Missing = !pwshOk;
            return r;
        }
        if (hw.Count > 0) { r.Reason = "erWinPs"; r.Data = string.Join(", ", hw.Take(3)); return r; }
        if (err7 != null && err7.Count > 0 && err51 != null && err51.Count == 0) { r.Reason = "erParse7"; return r; }
        if (def == "5.1") { r.Reason = "erDefault"; return r; }
        if (pwshOk) { r.Engine = "7"; r.Reason = def == "7" ? "erDefault" : "erPrefer7"; return r; }
        r.Reason = "erNo7";
        return r;
    }
}

/// <summary>A problem shown before a run and in the details: Kind = parse | need7 | nengine | core | syntax7 | winps | desktop | admin | module
/// | batBom | customMissing (1.3.0);
/// Level = error | warn | info.</summary>
public sealed class ScriptIssue
{
    public string Kind = "";
    public string Level = "warn";
    public string Data = "";
    public List<string> Lines = new();
    public string Engine = "";

    public string Text() => Kind switch
    {
        "parse" => Loc.F("issParse", EngineInfo.Name(Engine)) + "\n    " + string.Join("\n    ", Lines),
        "core" => Loc.F("issCore", Data),
        "need7" => Loc.F("issNeed7", Data),
        "nengine" => Loc.F("issEngineMissing", Data),
        "syntax7" => Loc.F("issSyntax7", Data),
        "winps" => Loc.F("issWinPs", Data),
        "desktop" => Loc.F("issDesktop", Data),
        "admin" => Loc.T("issAdmin"),
        "module" => Loc.F("issModule", Data),
        "batBom" => Loc.T("issBatBom"),
        "customMissing" => Loc.F("issCustomMissing", Data),
        _ => ""
    };

    public static List<ScriptIssue> ForEngine(EngineInfo ei, RequiresInfo? req, bool engineInstalled)
    {
        var list = new List<ScriptIssue>();
        if (ei.Missing)
        {
            string why = ei.Required ? ei.ReasonText() : EngineInfo.Name("7");
            list.Add(new ScriptIssue { Kind = "need7", Level = "error", Data = why });
        }
        else if (!engineInstalled)
            list.Add(new ScriptIssue { Kind = "nengine", Level = "error", Data = EngineInfo.Name(ei.Engine) });
        var errs = ei.Errors ?? new List<ParseError>();
        if (errs.Count > 0)
        {
            var lines = errs.Take(4).Select(e => Loc.F("issLineMsg", e.Line, e.Message)).ToList();
            if (errs.Count > 4) lines.Add("...");
            list.Add(new ScriptIssue { Kind = "parse", Level = "error", Lines = lines, Engine = ei.Engine });
        }
        if (!ei.Auto)
        {
            var ver = req?.ParsedVersion;
            var eds = req?.Editions ?? new List<string>();
            if (ei.Engine == "5.1")
            {
                if (ver != null && ver.Major >= 6) list.Add(new ScriptIssue { Kind = "core", Level = "error", Data = "#Requires -Version " + req!.Version });
                else if (eds.Contains("Core") && !eds.Contains("Desktop")) list.Add(new ScriptIssue { Kind = "core", Level = "error", Data = "#Requires -PSEdition Core" });
                else if (ei.Hints7.Count > 0 && errs.Count == 0) list.Add(new ScriptIssue { Kind = "syntax7", Level = "warn", Data = string.Join(", ", ei.Hints7.Take(3)) });
            }
            else if (eds.Contains("Desktop") && !eds.Contains("Core"))
                list.Add(new ScriptIssue { Kind = "desktop", Level = "warn", Data = "#Requires -PSEdition Desktop" });
        }
        if (ei.Engine == "7" && ei.HintsWin.Count > 0)
            list.Add(new ScriptIssue { Kind = "winps", Level = "warn", Data = string.Join(", ", ei.HintsWin.Take(3)) });
        return list;
    }
}

public sealed class PwshInfo
{
    /// <summary>Exe used to start scripts (the app execution alias for Store installs).</summary>
    public string Path = "";
    /// <summary>Where pwsh.exe really is (the package folder for Store installs).</summary>
    public string Location = "";
    public string Version = "";
    public bool Store;

    public string Label => Store ? Loc.F("p7Store", Version.Length > 0 ? Version : "7") : (Version.Length > 0 ? Version : "7");
}

/// <summary>Finds Windows PowerShell 5.1, PowerShell 7 (MSI, winget, zip on PATH, App Paths, preview, Microsoft Store
/// with its app execution alias) and winget.</summary>
public static class EngineLocator
{
    public static string? FindPs51()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string p = System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        return File.Exists(p) ? p : null;
    }

    /// <summary>File check that also accepts app execution aliases (0-byte reparse points in ...\WindowsApps).</summary>
    public static bool FileExists(string? p)
    {
        if (string.IsNullOrEmpty(p)) return false;
        try
        {
            if (File.Exists(p)) return true;
            var fi = new FileInfo(p);
            return fi.Attributes != (FileAttributes)(-1) && (fi.Attributes & FileAttributes.ReparsePoint) != 0 && (fi.Attributes & FileAttributes.Directory) == 0;
        }
        catch { return false; }
    }

    public static IEnumerable<string> PwshCandidates()
    {
        foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432") })
            if (!string.IsNullOrEmpty(pf)) yield return System.IO.Path.Combine(pf, @"PowerShell\7\pwsh.exe");
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            string x = d.Trim().Trim('"');
            if (x.Length > 0) yield return System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(x), OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        }
        if (OperatingSystem.IsWindows())
        {
            // The PATH of this process is not updated after an install: the machine and user PATH from the registry too
            foreach (var scope in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
            {
                string path = "";
                try { path = Environment.GetEnvironmentVariable("Path", scope) ?? ""; } catch { }
                foreach (var d in path.Split(';'))
                {
                    string x = Environment.ExpandEnvironmentVariables(d.Trim().Trim('"'));
                    if (x.Length > 0) yield return System.IO.Path.Combine(x, "pwsh.exe");
                }
            }
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                string? v = null;
                try
                {
                    using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\pwsh.exe");
                    v = (k?.GetValue("") as string)?.Trim('"');
                }
                catch { }
                if (!string.IsNullOrEmpty(v)) yield return Environment.ExpandEnvironmentVariables(v);
            }
            foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432") })
                if (!string.IsNullOrEmpty(pf)) yield return System.IO.Path.Combine(pf, @"PowerShell\7-preview\pwsh.exe");
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (la.Length > 0)
            {
                yield return System.IO.Path.Combine(la, @"Microsoft\WindowsApps\pwsh.exe");
                yield return System.IO.Path.Combine(la, @"Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe");
            }
        }
    }

    static bool IsPwshLeaf(string p) => Regex.IsMatch(p, @"(^|[\\/])pwsh(\.exe)?$", RegexOptions.IgnoreCase);
    public static bool IsStorePath(string p) => p.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    public static PwshInfo? FindPwsh()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in PwshCandidates())
        {
            if (!seen.Add(p) || !IsPwshLeaf(p)) continue;
            if (FileExists(p)) return NewInfo(p);
        }
        return null;
    }

    static PwshInfo NewInfo(string path)
    {
        var info = new PwshInfo { Path = path, Location = path, Store = IsStorePath(path) };
        if (info.Store)
        {
            string? alias = AliasPath();
            if (alias != null) info.Path = alias;
            string? pkg = StorePackagePath();
            if (pkg != null) info.Location = pkg;
        }
        info.Version = FileVersion(info.Location);
        if (info.Version.Length == 0) info.Version = FileVersion(path);
        if (info.Version.Length == 0)
        {
            var m = Regex.Match(info.Location, @"\\Microsoft\.PowerShell_(\d+\.\d+\.\d+)");
            if (m.Success) info.Version = m.Groups[1].Value;
        }
        return info;
    }

    static string? AliasPath()
    {
        string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var d in new[] { @"Microsoft\WindowsApps\pwsh.exe", @"Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe" })
        {
            string p = System.IO.Path.Combine(la, d);
            if (FileExists(p)) return p;
        }
        return null;
    }

    /// <summary>Install folder of the Store package: the newest C:\Program Files\WindowsApps\Microsoft.PowerShell_* folder
    /// that can be listed (often it cannot; the alias is used to start it anyway).</summary>
    static string? StorePackagePath()
    {
        try
        {
            string pf = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var dirs = Directory.GetDirectories(System.IO.Path.Combine(pf, "WindowsApps"), "Microsoft.PowerShell_*");
            return dirs.OrderByDescending(d => d).Select(d => System.IO.Path.Combine(d, "pwsh.exe")).FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }

    public static string FileVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";
            string v = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
            var m = Regex.Match(v, @"^(\d+\.\d+(\.\d+)?(-[\w.]+)?)");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch { return ""; }
    }

    public static string? FindWinget()
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            try
            {
                string p = System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')), "winget.exe");
                if (d.Trim().Length > 0 && FileExists(p)) return p;
            }
            catch { }
        }
        string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string w = System.IO.Path.Combine(la, @"Microsoft\WindowsApps\winget.exe");
        return FileExists(w) ? w : null;
    }

    public const string PwshReleasePage = "https://aka.ms/powershell-release?tag=stable";
    public const string WingetArgs = "install --id Microsoft.PowerShell --source winget --accept-package-agreements --accept-source-agreements";

    /// <summary>The only addresses the MSI is downloaded from (regular expression, also used by the install script).</summary>
    public const string MsiUrlPattern = @"^https://github\.com/PowerShell/PowerShell/releases/download/[^?#]+\.msi$";
    /// <summary>The certificate subject the MSI must be signed with (Microsoft Corporation).</summary>
    public const string MicrosoftSubjectPattern = @"(^|, )CN=Microsoft Corporation(,|$)";

    /// <summary>The PowerShell code of the window that installs PowerShell 7 (winget, or the MSI from GitHub: the address, the
    /// SHA-256 published by GitHub when there is one and the Microsoft signature are checked before msiexec starts).</summary>
    public static string InstallScript(string method, string? winget)
    {
        string Q(string t) => Quoting.PsString(t);
        if (method == "winget")
            return "& " + Q(winget ?? "winget.exe") + " " + WingetArgs + "\r\nWrite-Host ''; Write-Host (" + Q(Loc.T("p7ExitCode")) + " -f $LASTEXITCODE); Write-Host " + Q(Loc.T("p7Done"));
        return @"$ProgressPreference = 'SilentlyContinue'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072 } catch {}
$arch = 'x64'
if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { $arch = 'arm64' }
$asset = $null
try {
    $rel = Invoke-RestMethod 'https://api.github.com/repos/PowerShell/PowerShell/releases/latest' -UseBasicParsing -Headers @{ 'User-Agent' = 'Shelly' }
    $asset = @($rel.assets | Where-Object { $_.name -match ('^PowerShell-[\d.]+-win-' + $arch + '\.msi$') }) | Select-Object -First 1
} catch { Write-Host $_.Exception.Message }
if (-not $asset) {
    Write-Host MSG_NOMSI
    Start-Process 'PAGE_URL'
} elseif ([string]$asset.browser_download_url -notmatch 'URL_PATTERN') {
    # Only a release download of the PowerShell repository on github.com is fetched
    Write-Host MSG_BADURL
} else {
    $url = [string]$asset.browser_download_url
    # A new folder in the user's own profile (not the shared temp folder), removed at the end
    $dir = Join-Path $env:LOCALAPPDATA ('Shelly\installer\' + [guid]::NewGuid().ToString('N'))
    $lock = $null
    try {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $file = Join-Path $dir $asset.name
        Write-Host (MSG_DOWNLOADING -f $url)
        Invoke-WebRequest $url -OutFile $file -UseBasicParsing
        # The file stays open from the checks until the installer is done: others can read it, nobody can change or replace it
        $lock = [IO.File]::Open($file, 'Open', 'Read', 'Read')
        $ok = $true
        if ([string]$asset.digest -match '^sha256:([0-9a-fA-F]{64})$') {
            if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Matches[1]) { $ok = $false; Write-Host MSG_BADHASH }
        }
        if ($ok) {
            $sig = Get-AuthenticodeSignature -LiteralPath $file
            $subject = ''
            if ($sig.SignerCertificate) { $subject = [string]$sig.SignerCertificate.Subject }
            if ($sig.Status -ne 'Valid' -or $subject -notmatch 'SUBJECT_PATTERN') { $ok = $false; Write-Host (MSG_BADSIG -f $sig.Status) }
        }
        if ($ok) {
            Write-Host MSG_RUNINSTALLER
            $p = Start-Process msiexec.exe -ArgumentList ('/i ""' + $file + '""') -Wait -PassThru
            Write-Host (MSG_EXITCODE -f $p.ExitCode)
        }
    } catch { Write-Host $_.Exception.Message }
    finally {
        if ($lock) { $lock.Dispose() }
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
Write-Host ''
Write-Host MSG_DONE
".Replace("MSG_NOMSI", Q(Loc.T("p7NoMsi"))).Replace("PAGE_URL", PwshReleasePage).Replace("MSG_DOWNLOADING", Q(Loc.T("p7Downloading")))
 .Replace("MSG_RUNINSTALLER", Q(Loc.T("p7RunInstaller"))).Replace("MSG_EXITCODE", Q(Loc.T("p7ExitCode"))).Replace("MSG_DONE", Q(Loc.T("p7Done")))
 .Replace("MSG_BADURL", Q(Loc.T("p7BadUrl"))).Replace("MSG_BADHASH", Q(Loc.T("p7BadHash"))).Replace("MSG_BADSIG", Q(Loc.T("p7BadSig")))
 .Replace("URL_PATTERN", MsiUrlPattern).Replace("SUBJECT_PATTERN", MicrosoftSubjectPattern);
    }
}
