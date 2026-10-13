# Changelog

All notable changes to Shelly are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Versions 0.9.x are the PowerShell + WPF edition (now in `legacy/`); 1.0.0 and later are the .NET edition.

## [Unreleased]
## [1.0.12] - 2026-10-13

### Changed

- **About card.** One card, Windows 11 style: the app name and version, one caption line, and the update check on the right.
- The caption line is the credit plus links to the GitHub repository and the log.
- Removed the tagline and the extra links (author, README, CHANGELOG, license, disclaimer, user guide) from About.

## [1.0.11] - 2026-10-11

Safety release. The safety check of 1.0.10 matched command names and arguments from a fixed list. It caught simple
aliases (`rm`, `del`, `rd`, `ri`), case changes, module-qualified names and shortened parameters, but a test corpus of
100 scripts showed 61 bypasses: aliases defined by the script (`Set-Alias`/`New-Alias`/`sal`) and alias chains, every
form of dynamic invocation (`& ('Rem'+'ove-Item')`, `-f`, `-join`, `& $var`, `. $var`, `Get-Command`,
`$ExecutionContext`, `.Invoke()`, `Invoke-Command` with a variable, `Add-Type`, reflection), splatting, .NET calls
(`[IO.File]::Delete`, `[IO.Directory]::Delete`, VisualBasic `FileSystem`, `[Microsoft.Win32.Registry]`,
`Process.Start`), WMI/CIM (shadow copies, shutdown), `Clear-EventLog`/`wevtutil`, `schtasks`, `vssadmin resize`;
`iex`, download-and-run and `-EncodedCommand` were only "caution". The safety check is still a guardrail that reads the
script, not a sandbox, and the documentation now says so.

### Added

- **AST-based safety analyzer.** Command names come from PowerShell's parser; the full built-in alias table of
  Windows PowerShell 5.1 and PowerShell 7 plus the script's own `Set-Alias`/`New-Alias` (with chains); case-insensitive,
  module-qualified names and shortened parameters; constant folding of variables (the assignment in effect at the
  line), concatenation, `-f`, `-join`, expandable strings and splatting; nested code in `cmd /c`, `powershell -Command`,
  `Start-Process`, `Invoke-Command`, `Start-Job`.
- **Dynamic invocation is danger** with the message *The script builds commands dynamically; Shelly can't verify what
  it runs*: `& (expr)`, `& $var`, `. $var`, `iex`, `[scriptblock]::Create`, `.Invoke()`, `$ExecutionContext.InvokeCommand`,
  `-EncodedCommand`/`-enc`/`-e`, `FromBase64String` together with execution, `Add-Type` from a variable or file,
  reflection (`Assembly.Load`, `GetMethod().Invoke`, `InvokeMember`, `GetDelegateForFunctionPointer`).
- **Download-and-run is danger** with *Downloads and runs code from the internet: <url>* and the URL found:
  `irm`/`iwr`/`Invoke-RestMethod`/`Invoke-WebRequest`/`curl`/`wget`/`Net.WebClient`/`Start-BitsTransfer` piped or passed
  to `iex`, `&` or `[scriptblock]::Create`, through a variable, and downloaded `.ps1`/`.exe`/… started with `&` or
  `Start-Process`.
- **.NET and native rules**: `[IO.File]`/`[IO.Directory]::Delete`, VisualBasic `FileSystem`, `[Microsoft.Win32.Registry]`,
  `Process.Start`, `Invoke-CimMethod`/WMI `Win32_ShadowCopy` and `Win32Shutdown`, `vssadmin delete/resize shadowstorage`,
  `Clear-EventLog`/`Remove-EventLog`/`wevtutil cl`, `schtasks`/`Register-ScheduledTask` (danger as SYSTEM or with highest
  rights), `Remove-Service`, `Add-Type` with inline C# (warning).
- **Three severities**: danger (red), warning (amber), info (shown in the dialog, never asks, no mark in the list).
  Fewer false alarms: `Remove-Item -Force` on a named item, deleting under `$env:TEMP` without `-Recurse`, calling
  local scripts, base64 that isn't run and downloads that aren't run are info.
- **Built-in scanner (C#)** with the same rules for the time before PowerShell reports and for PCs where no
  PowerShell can check a script. PowerShell's parser stays the source of truth.
- **Scan states**: the list shows *checking* (clock) until PowerShell's parser has checked a script and *not verified*
  (question mark) if no PowerShell could; a script is never shown as "nothing found" before that, and an unverified
  script asks before running.
- **Trusted sources** (Settings → Safety): download-and-run from these HTTPS sources becomes info (*Runs code from
  trusted source X*, the URL is still shown). A built-in list, stored apart from your entries, marked *built-in*, each
  one can be turned off: `christitus.com`, `get.scoop.sh`, `community.chocolatey.org/install.ps1`,
  `aka.ms/install-powershell.ps1`, `raw.githubusercontent.com/PowerShell/PowerShell/`, `dot.net/v1/dotnet-install.ps1`,
  `astral.sh`, `deno.land/install.ps1`, `bun.sh`, `bun.com`, `get.pnpm.io`, `ohmyposh.dev` (each is the install command
  documented by the project; activation and piracy tools are not included). Add or remove your own. HTTPS only (plain
  http and scheme-less URLs are never trusted); a host matches itself and its subdomains; on shared hosts (GitHub, gists,
  `aka.ms`, shorteners, paste and file hosts, CDNs, cloud storage) only host + owner/repo or a file path, never the whole
  host. The safety dialog offers *Trust this source (host)*. Trusting a source means trusting whatever it serves at
  run time.
- **Mark of the Web.** Shelly reads the `Zone.Identifier` stream. Internet/restricted zone (3/4): an orange *Downloaded
  from the internet* mark with the URL in the tooltip, and the script doesn't run from any entry point (list, tray,
  hotkey, jump list, history, omnibox) or build until you click **Unblock** in a dialog with the download URL and the
  scan result; Unblock removes the stream, then the script is checked again. Local scripts and modules loaded by a fixed
  path are checked too. Intranet/trusted sites (1/2): a note. Shelly never removes the mark on its own, and saving in
  the editor keeps it. Tested with a simulated stream; needs verification on Windows/NTFS.
- **Build .exe confirmation.** A script with any warning or danger, a Mark of the Web, or no PowerShell check needs a
  second dialog where you type the script's name or `BUILD`. Danger scripts go through the full danger review first;
  marked scripts must be unblocked first. Built exes get no trust. The dialog says not to distribute unverified scripts.
- **First-run disclaimer** (English and Russian, compact Windows 11 dialog): scripts run with your rights (full rights as
  administrator via UAC), Shelly is not a sandbox, the safety check is a static guardrail and can miss things, run only
  scripts you trust and have read, the authors aren't responsible for damage caused by scripts. *I understand* or
  *Exit*; shown once (`disclaimerAccepted` stores the version); Settings → About → *Disclaimer* shows it again.
- **`# Shelly-Mode: inline | window | admin`** header line: the default run mode for the list and the jump list.
- **Example `Examples\Chris Titus WinUtil.ps1`**: `irm https://christitus.com/win | iex`, as administrator, engine Auto.
- **SECURITY.md**: supported versions, how to report a vulnerability, the security model.
- **SHA256SUMS.txt** with each release; README explains how to check it and what to do about SmartScreen.
- Tests: a corpus of 121 scripts (bypasses, remote-code one-liners, false-positive checks) run through the PowerShell
  analyzer and the built-in scanner; trusted-source matching, Mark of the Web parsing with a fake stream reader, settings
  round-trip, the no-trust rule for danger.

### Changed

- **Danger can't be trusted.** Scripts with danger findings ask before every run: tick *I understand*, and *Run*
  unlocks after 3 seconds. The *Don't warn again* checkbox is only offered for warnings (trusted by path + SHA-256 as
  before). Turning safety warnings off no longer silences danger findings or downloaded scripts.
- `iex`/`Invoke-Expression`, download-and-run and `-EncodedCommand` are danger instead of caution.
- Labels: *Danger*, *Warning*, *Info* (were *High risk* and *Caution*).
- The legacy PowerShell edition (`legacy/Launcher.ps1`) no longer calls `Unblock-File` on every listed script, which
  silently removed the Mark of the Web.

### Fixed

- `cmd /c "rd /s /q C:\"` and `Process.Start("cmd", "/c del …")` were not resolved to deletions.
