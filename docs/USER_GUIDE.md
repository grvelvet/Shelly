# Shelly 1.0.11 user guide

[Русская версия](USER_GUIDE_RU.md) · [README](../README.md) · [CHANGELOG](../CHANGELOG.md)

Shelly is a portable Windows launcher for PowerShell scripts. It lists every `.ps1` in its `Scripts` folder and runs
each one on the engine it needs (Windows PowerShell 5.1 or PowerShell 7). It reads the script's parameters, warns about
dangerous commands, keeps a history of runs, and builds scripts into `.exe` files.

## Contents

1. [Install and requirements](#1-install-and-requirements)
2. [First run and the folder layout](#2-first-run-and-the-folder-layout)
3. [The main window](#3-the-main-window)
4. [Running scripts](#4-running-scripts)
5. [Writing scripts for Shelly](#5-writing-scripts-for-shelly)
6. [Safety checks](#6-safety-checks)
7. [The editor](#7-the-editor)
8. [Omnibox commands](#8-omnibox-commands)
9. [Keyboard shortcuts](#9-keyboard-shortcuts)
10. [Tray, global hotkey, autostart, notifications, jump list](#10-tray-global-hotkey-autostart-notifications-jump-list)
11. [Build to .exe](#11-build-to-exe)
12. [Installing PowerShell 7](#12-installing-powershell-7)
13. [Updates](#13-updates)
14. [Settings reference](#14-settings-reference)
15. [Troubleshooting and FAQ](#15-troubleshooting-and-faq)
16. [Privacy](#16-privacy)

---

## 1. Install and requirements

**Requirements**

- Windows 10 or 11, x64. Windows 11 gets the full styling (rounded corners, Mica, colored title bar). On Windows 10
  Shelly works with simplified styling.
- Windows PowerShell 5.1, which comes with Windows. PowerShell 7 is optional: you need it only for scripts that require
  it (see [section 12](#12-installing-powershell-7)).
- For **Build .exe**: the C# compiler of .NET Framework 4
  (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), which is part of Windows 10/11.

**Two builds**

| File | `Shelly.exe` | Needs |
|---|---|---|
| `shelly-<version>.zip` (recommended) | ≈ 2 MB | the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) |
| `shelly-<version>-selfcontained.zip` | ≈ 70 MB | nothing, the runtime is inside the exe (it starts a bit slower) |

If the runtime is missing, Windows shows a dialog with a download link the first time you start the small build.
You can also install the runtime with `winget install Microsoft.DotNet.DesktopRuntime.10`.

**Install.** Unzip anywhere (for example `C:\Tools\Shelly`) and run `Shelly.exe`. There is no installer.

**Upgrading from 0.9.x (the PowerShell edition).** Put `Shelly.exe` into your existing Shelly folder, next to `Scripts`
and `launcher-settings.json`. Your favorites, engine choices, remembered run settings, trusted scripts, history and
other settings are read as they are. Settings keys that Shelly 1.0 doesn't know are kept in the file. Shelly also
works when its exe is in the old `build\` subfolder: if that folder has no `Scripts` folder of its own but its parent
does, the parent is used as the Shelly folder.

**Where things are stored**

| What | Where |
|---|---|
| Scripts | `<Shelly folder>\Scripts\` |
| Settings | `<Shelly folder>\launcher-settings.json` |
| Run history | `<Shelly folder>\launcher-history\` (`history.json` and one `<id>.log` per run with output) |
| Built .exe files | `<Shelly folder>\build\` |
| Icons you picked for scripts | `<Shelly folder>\assets\script-icons\` |
| Log, analysis cache, perf log | `%TEMP%\Shelly\` (`shelly.log`, `analysis-cache.json`, `perf.log`) |
| Autostart entry (only if you turn it on) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Shelly` |

Shelly writes nothing else outside its folder.

## 2. First run and the folder layout

**Disclaimer.** On the very first start Shelly shows a short notice: scripts run with your user rights (as
administrator, with full control after the UAC prompt), Shelly is not a sandbox, the safety check is a guardrail that
can miss things, run only scripts you trust and have read, and the authors aren't responsible for damage caused by
scripts. **I understand** continues (and the notice doesn't come back), **Exit** closes Shelly. Read it again any time
from **Settings → About → Disclaimer**.

On start Shelly creates `Scripts`, `build` and `%TEMP%\Shelly` if they are missing. Only one Shelly runs per folder. If
you start it a second time, the first window comes to the front, and a `--run` argument is passed on to it.

```
Shelly\
  Shelly.exe
  Scripts\                 your scripts; subfolders become groups
    usd_rub.ps1
    Examples\Hello with parameters.ps1
    Examples\Chris Titus WinUtil.ps1
    System\System summary.ps1
  build\                   built .exe files (same subfolders as in Scripts)
  assets\shelly.ico        default icon for built .exe files
  assets\script-icons\     icons you picked for scripts
  launcher-settings.json   settings (created on first start)
  launcher-history\        run history
```

- Every `*.ps1` under `Scripts` is listed, down to 8 folder levels. Shelly skips files and folders whose names start
  with a dot, hidden files and folders, and folder links (junctions and symlinks).
- Each subfolder is a collapsible **group**. Scripts directly in `Scripts` are in the **General** group. Starred
  scripts also appear in a **Favorites** group at the top.
- The list follows the folder: files you add, rename or delete outside Shelly show up by themselves. **F5** refreshes
  the list by hand.

**Command line**

| Argument | Effect |
|---|---|
| `--run "Folder\Script.ps1"` (also `-run`, `/run`) | Runs the script, given as a path relative to `Scripts`. Used by the jump list. |
| `--minimized` (also `-minimized`, `/minimized`, `--tray`) | Starts hidden in the tray. Used by autostart. |
| `--perf` | Writes start-up timings to `%TEMP%\Shelly\perf.log`. |

## 3. The main window

**Navigation pane** (left): **Scripts** (Ctrl+1), **Favorites** (Ctrl+2), **History** (Ctrl+3), **Compatibility**,
**Settings** (Ctrl+,). Ctrl+M or the ☰ button shrinks the pane to icons. On windows narrower than 800 px the pane
stays as icons and opens over the page; Esc closes it.

**Scripts page**

- Header toolbar, left of the search box: **New script**, **New folder** | **Sort**, **Collapse all / Expand all**,
  **View**, **Filter**. Then the **omnibox** (search and `>` commands, see [section 8](#8-omnibox-commands)), and right of
  it **Refresh** (F5) and **Open folder** (the Scripts, build or Shelly folder). Every button has a tooltip.
- In a narrow window the search box shrinks first. Then Sort, Collapse, View and Filter move into a **…** (More) menu.
  Only after that does the search box turn into a search button.
- **Sort** (the ⇅ button): Name, Date modified, Last run or Run count, Ascending or Descending. The order applies inside
  each group, and Favorites stay on top as their own group. Ties are sorted by name. With Last run, scripts that never ran
  go to the end in both directions. Run count counts the runs in the history (see **Keep runs** in Settings). The button
  is tinted with the accent color when the order is not Name, ascending. The choice is saved.
- **Collapse all / Expand all**: one button. It collapses every group (Favorites, the root scripts and all folders)
  while any group is open, and expands them all otherwise. Shortcuts: **Ctrl+Shift+[** and **Ctrl+Shift+]**. Group
  states are saved as before. The button is disabled while you search, because results are a flat list.
- **View**: **Cards** (the default 64 px cards) or **Compact list** (one 40 px line per script: icon, name, then the
  description in grey on the same line, the status marks and the hover buttons; no card border). The choice is saved.
- **Filter**: check any of **Ran before**, **Never run**, **Needs PowerShell 5.1**, **Has safety warnings**, **Run as
  admin (remembered mode)** and **Favorites only**. Ran before and Never run widen each other. Every other filter
  narrows the list. Filters combine with the search. Only groups with matching scripts are shown. With "Favorites only"
  the list has no separate Favorites group. While a filter is on, the button shows an accent dot and a **Filters: N ·
  Clear** chip appears under the header (its tooltip lists the filters). When nothing matches, the page says **No
  scripts match the filters** and offers **Clear filters**. Filters last until you close Shelly; they are not saved.
- Search filters by name, folder and description. Words separated by spaces must all match.
- **Script rows.** Each row shows the icon, name and description. Hover a row for ▶ **Run** and ⚙ **Run settings**.
  While a script runs, the row shows a spinner, the elapsed time and ⏹ **Stop**. The quiet marks to the left of the
  buttons:
  - **PS 5.1** or **PS 7**: the engine. It is shown only when it tells you something: the script runs on Windows
    PowerShell 5.1, its engine was set by hand, or the engine it needs is not installed. Auto → 7 shows nothing. The
    tooltip explains why Auto picked the engine.
  - **Safety**: a red shield means danger, an amber shield means warning (info findings show no mark). An orange
    **download arrow** means the file was downloaded from the internet and is blocked until you unblock it. A clock
    means PowerShell is still checking the script; a question mark means no PowerShell could check it. The tooltip
    lists the findings and, for downloaded files, the URL. See [section 6](#6-safety-checks).
  - **Last result**: success, failure (with the exit code), stopped, or window closed. The tooltip has the details.
- Row tooltip: relative path, parameters (`*` = mandatory) and the time the file was last modified.
- Double-click or Enter runs the script. Right-click opens the menu: Stop (while running), Run, Run in separate window,
  Run as administrator, Run settings…, Edit, Open in external editor, Build .exe, Add to/Remove from favorites,
  Change icon…, Default icon (when a custom icon is set), Show runs (opens the last run in History), Rename, Open file
  location, Copy path, Delete.
- Right-click a group or empty space for New script here, New folder, Rename folder, Delete folder and Open in Explorer. Deleting
  sends scripts and non-empty folders to the Recycle Bin, after a confirmation.
- **Output panel** (bottom): the live output of the selected run. Buttons: Stop, Run again, Open script, Copy, Save to
  `.txt`, Clear the view (the history keeps the output), Word wrap, Find (Ctrl+F). Drag the splitter to resize the panel;
  the chevron collapses it.
- **Info bar**: appears when an update is available, when some scripts need PowerShell 7 and it is missing (**Install**),
  or when there are compatibility notes. You can close it.

**Favorites page.** Tiles of starred scripts with Run, Run settings and Stop. Double-click a tile to run it. Star or
unstar with Ctrl+D or the context menu. Favorites also appear in the tray menu and the taskbar jump list.

**History page.** Every run: inline, separate window, administrator, from the tray, from the editor, and re-runs. Each
entry shows the result, start time, duration, engine, mode and parameters. Filter by All runs, Successful, Failed,
Stopped or Separate window, and search. Select a run to see its saved output. **Enter** runs it again with the same
parameters, mode and engine. **Del** deletes the entry. **Clear history** deletes all entries except running ones. Runs
in a separate window keep no output.

**Compatibility page.** Checks Windows, the .NET runtime Shelly runs on, Windows PowerShell 5.1, PowerShell 7 (with
**Install PowerShell 7…** and **Check again**), the execution policy set by Group Policy, script analysis, the C#
compiler for exe builds, .NET Framework 4 (4.5 or newer is needed, 4.8 is recommended), the global hotkey and the
tray icon. Each check shows a status and a hint.

**Settings page.** See [section 14](#14-settings-reference).

## 4. Running scripts

**Modes**

| Mode | What happens |
|---|---|
| **Normal** (inline) | Runs hidden. The output streams into the output panel. The working folder is the script's folder. |
| **Separate window** | A PowerShell console window that stays open after the script ends (`-NoExit`). |
| **As administrator** | The same, elevated through UAC. Shelly always asks before an administrator run (see [section 6](#6-safety-checks)). |

Shelly starts every run with `-NoProfile -ExecutionPolicy Bypass`. Inline runs are also `-NonInteractive` with input
closed, so scripts that ask for input (`Read-Host`, prompts) must run in a separate window. Inline output is plain text
(no ANSI colors).

**Which mode is used**

- ▶, Enter, double-click and the jump list use the mode you remembered for the script, otherwise Normal.
- Context-menu items use their own mode.
- Favorites in the tray menu always run in a separate window.
- **F5** in the editor runs inline.

**Engines.** Each script runs on **Auto**, **5.1** or **7**. In Auto, Shelly picks:

1. PowerShell 7 if the script has `#Requires -Version 6` or higher, or `#Requires -PSEdition Core`.
2. Windows PowerShell 5.1 for `#Requires -PSEdition Desktop`, or for `#Requires -Version` 5.x or lower.
3. PowerShell 7 if the script uses 7-only features: `??`, `??=`, `?.`, `?[]`, `&&`, `||`, the ternary `? :`,
   `clean { }`, `$PSStyle`, 7-only cmdlets or parameters (such as `ForEach-Object -Parallel`), or if it parses only in 7.
4. Windows PowerShell 5.1 if it uses 5.1-only commands (such as `workflow` or `Get-Content -Encoding Byte`), or if it
   parses only in 5.1.
5. Otherwise, the **Default engine for Auto** setting. Its default is "PowerShell 7 if installed, else 5.1".

You can pin an engine per script in **Run settings**. The choice is saved as soon as you change it. When the chosen
engine is missing, the script needs administrator rights, or a required module is missing, Shelly says so before the
run. It offers a way out (run with the other engine, run as administrator, install PowerShell 7) or **Run anyway**.

**Run settings** (⚙, Ctrl+I or Ctrl+Enter, or the context menu) is a compact dialog with:

- the script's icon, name and description;
- one field per parameter. The placeholder shows the default, or "set by the script" when the default is computed, or
  the type ("number", "text"…). An empty field is not passed, so the script uses its own default. Switches and `[bool]`
  are checkboxes. `ValidateSet` becomes a drop-down list. A red `*` marks mandatory parameters. The tooltip shows the
  type, "required", the default and the `HelpMessage`. Arrays (`[string[]]`) take a comma-separated list;
- **PowerShell: Auto (x) / 5.1 / 7** and **Mode: Normal / Separate window / As administrator** side by side;
- the last run in one line, with **Show in history**;
- warnings, if there are any;
- **Remember for this script**, **Run** and **Cancel**.

With **Remember** checked, Run saves the parameters and the mode, and ▶ / Enter / double-click use them from then on.
If you uncheck it and click Run, the saved settings are forgotten. When a mandatory parameter has no value, Run opens
this dialog instead of starting the script, and the dialog's Run button tells you which fields are missing.

**Concurrent runs.** Several scripts can run at the same time, each with its own output, spinner and Stop. The same
script can run inline only once at a time; separate-window runs are not limited. Window and administrator runs are
tracked until the window closes. An elevated window may not be trackable, and then it is recorded as "started".

**Stop.** ⏹ on the row or tile, the output panel's Stop button, or the context menu. Stop ends the script's whole
process tree. When you exit Shelly while inline runs are active, Shelly asks first and then stops them.

**Output.** The panel shows at most 4 MB or 100,000 lines of a run, then notes that the rest was skipped. The history
keeps up to the **Saved output per run** limit. When the output is longer, the beginning and the last lines are kept.

**Re-run from history.** On the History page, press Enter or click **Run again with the same parameters**. The output
panel's Run again button does the same.

## 5. Writing scripts for Shelly

**Description.** Shelly shows a one-line description under the name (in the compact list, next to it). It reads the
script's leading comments, before any code, and uses whichever comes first:

- a one-line `#` comment among the first 8 lines. `#Requires`, `#!` and `# Shelly-…:` lines are skipped. A
  `Description:` (or `Описание:`) prefix is dropped, so `# Description: Cleans temp files` shows "Cleans temp files";
- the `.SYNOPSIS` of a comment-based help block `<# … #>` at the top (within the first 60 lines). The first paragraph
  after `.SYNOPSIS` is used, up to the next keyword such as `.DESCRIPTION`. A help block without `.SYNOPSIS` is skipped.

```powershell
<#
.SYNOPSIS
    Backs up the user profile to a zip file.
.DESCRIPTION
    Longer text that Shelly does not show.
#>
param([string]$Target = "D:\Backup")
```

The template for **New script** starts with `# What this script does`; replace it with your own line.

**Parameters.** Shelly reads the script's `param()` block with PowerShell's own parser in the background. Until that
finishes, it uses a quick built-in reader.

| In the script | In Run settings | Passed as |
|---|---|---|
| `[string]$Name` | text box ("text") | `-Name 'value'` |
| `[int]$Count = 3` (any number type) | text box, placeholder `3` | `-Count '5'` |
| `[datetime]$When` | text box ("date") | `-When '…'` |
| `[switch]$Loud` | checkbox | `-Loud` (a switch that defaults to `$true` and is unchecked gets `-Loud:$false`) |
| `[bool]$Flag` | checkbox | `-Flag:$true` / `-Flag:$false` |
| `[string[]]$Items` | text box ("comma-separated list") | `-Items 'a','b'` |
| `[ValidateSet('A','B')]` | drop-down list with "(default)" first | `-Param 'A'` |
| `[Parameter(Mandatory)]` | red `*`; Run is blocked while it's empty | |
| `[Parameter(HelpMessage = '…')]` | tooltip text | |

**Icon.** A line in the leading comments sets the default glyph, a Segoe Fluent Icons code point from the private use
area (`E000`–`F8FF`):

```powershell
# Shelly-Icon: E9D2
```

- The line must be in the leading comment block, within the first 40 lines. It may come after a `<# … #>` help block.
- `U+E9D2`, `0xE9D2`, `&#xE9D2;` and `\uE9D2` also work.
- The bundled examples use `E9D2` (usd_rub), `E77B` (Hello with parameters), `E977` (System summary) and `E90F` (Chris Titus WinUtil). Code points
  are listed in Microsoft's "Segoe Fluent Icons font" documentation.
- A picture you choose with **Change icon…** (`.ico` or `.png`) takes priority. It is copied to `assets\script-icons`.

**Default run mode.** `# Shelly-Mode: inline`, `window` or `admin` in the leading comments sets how a double-click,
Enter or the jump list runs the script, until you remember another mode in Run settings. Administrator runs still ask
first and show the UAC prompt.

**Encoding.** Windows PowerShell 5.1 reads a `.ps1` without a BOM as ANSI, which garbles non-English text. Save scripts
as **UTF-8 with BOM**. The Shelly editor always saves that way, and new scripts are created that way.

**Exit code.** Inline runs finish with the script's `exit N`, or with the last native program's exit code, or 1 after
a terminating error, otherwise 0. Shelly shows success for 0 and failure otherwise.

**Full example**

```powershell
# Copies files of one type from one folder to another.
# Shelly-Icon: E8C8
#Requires -Version 5.1

param(
    [Parameter(Mandatory = $true, HelpMessage = 'Folder to copy from')]
    [string]$Source,

    [Parameter(Mandatory = $true, HelpMessage = 'Folder to copy to')]
    [string]$Destination,

    [ValidateSet('txt', 'log', 'csv')]
    [string]$Extension = 'txt',

    [int]$MaxFiles = 100,

    [switch]$WhatIf
)

$files = Get-ChildItem -LiteralPath $Source -Filter "*.$Extension" -File | Select-Object -First $MaxFiles
if (-not $files) { Write-Host "No .$Extension files in $Source"; exit 1 }

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach ($f in $files) {
    if ($WhatIf) { Write-Host "Would copy $($f.Name)" }
    else { Copy-Item -LiteralPath $f.FullName -Destination $Destination; Write-Host "Copied $($f.Name)" }
}
Write-Host "Done: $($files.Count) file(s)."
```

The list shows "Copies files of one type from one folder to another." with the copy glyph. Run settings shows Source *
and Destination * (required), Extension as a drop-down list, MaxFiles with the placeholder 100, and WhatIf as a
checkbox.

## 6. Safety checks

> **A guardrail, not a sandbox.** Shelly reads a script before it runs and warns about risky commands. It doesn't
> isolate or restrict anything: the script runs with your rights (as administrator: with full control of the PC). The
> check can miss things. Run only scripts you trust and have read.

### How the check works

Shelly parses the script with PowerShell's own parser (the AST) in PowerShell 7 or Windows PowerShell 5.1 and looks
at the commands that actually run, not at the text:

- **Command names** come from the parsed command. Aliases are resolved with the full built-in alias table of 5.1 and 7
  (`rm`, `ri`, `del`, `erase`, `rd`, `rmdir`, `iwr`, `irm`, `iex`, `curl`, `wget`, `sal`…), including aliases the script
  defines itself with `Set-Alias` / `New-Alias` and chains of them. Matching ignores case and module prefixes
  (`Microsoft.PowerShell.Management\Remove-Item`) and understands shortened parameters (`-Rec`, `-fo`).
- **Values** are followed where they are constant: variables (the assignment in effect at that line), string
  concatenation, `-f`, `-join`, splatting (`@params`) and expanded strings.
- **Nested code** in `cmd /c`, `powershell -Command`, `Start-Process`, `Invoke-Command`, `Start-Job` and script blocks
  is checked too.
- **.NET and native tools** are covered: `[IO.File]::Delete`, `[IO.Directory]::Delete`, the VisualBasic `FileSystem`,
  `[Microsoft.Win32.Registry]`, `[Diagnostics.Process]::Start`, `Invoke-CimMethod` / WMI (`Win32_ShadowCopy`,
  `Win32Shutdown`), `vssadmin`, `wbadmin`, `bcdedit`, `diskpart`, `format`, `cipher /w`, `takeown` / `icacls` on system
  paths, `reg delete`, `schtasks`, `sc delete`, `wevtutil cl`, `Set-MpPreference`, `netsh advfirewall … off`.

Until PowerShell reports (a second or two; results are cached), Shelly's simpler built-in scanner stands in and the
row shows **checking**. A script is never shown as "nothing found" before PowerShell's parser has checked it. If no
PowerShell can check it, the row shows **not verified** and Shelly asks before running it. Before a run Shelly waits up
to 30 s for the check.

### Severities

| Level | Mark | What happens | Examples |
|---|---|---|---|
| **Danger** (red) | red shield | Asks before **every** run. Can't be trusted. | Deleting in system locations, drive roots, user profiles or HKLM; disk format/wipe/partition; `bcdedit`; shutdown/restart; `reg delete` in HKLM; execution policy Unrestricted/Bypass for good; weakening Defender; turning off the firewall; deleting services, shadow copies, backups or event logs; changing accounts or the Administrators group; ACL/ownership of system files; scheduled tasks as SYSTEM or with highest rights; **code that is built at run time** (see below); **downloading and running code** |
| **Warning** (amber) | amber shield | Asks before a run; "don't warn again" is allowed. | Deleting with `-Recurse` outside temp folders; deleting through .NET recursively; `Start-Process -Verb RunAs`; HKLM writes; scheduled tasks; compiling C# with `Add-Type` from text in the script; disabling a network adapter; session-only execution policy |
| **Info** | none | Shown in the dialog, never asks. | `Remove-Item -Force` on a named item; deleting under `$env:TEMP` without `-Recurse`; calls of other local `.ps1` files; a base64 string that isn't run; downloads that aren't run; code from a **trusted source** |

### Code built at run time (dynamic invocation)

If a script builds the command it runs, Shelly can't know what runs. These are **danger** with the message
"The script builds commands dynamically; Shelly can't verify what it runs":

- `& (…)`, `& $var`, `. $var` and `& ('Rem'+'ove-Item')`-style names (a constant name is resolved and checked as that
  command instead);
- `Invoke-Expression` / `iex`, `[scriptblock]::Create(…)`, `.Invoke()` / `.InvokeReturnAsIs()` on a built script block,
  `$ExecutionContext.InvokeCommand…`, `Invoke-Command` / `Start-Job` with a script block from a variable;
- `-EncodedCommand` / `-enc` / `-e`, and `[Convert]::FromBase64String` when the script also runs code;
- `Add-Type` with code from a variable or a file, reflection (`Assembly.Load`, `GetMethod(…).Invoke`,
  `InvokeMember`, `Marshal.GetDelegateForFunctionPointer`).

A script block that the script writes itself (`$sb = { … }; & $sb`) and a `[scriptblock]` parameter of one of its own
functions are checked like normal code and are not flagged.

### Downloading and running code

One-liners that download code and run it are **danger** with the message
"Downloads and runs code from the internet: <url>", showing the URL found. Covered: `irm` / `iwr` /
`Invoke-RestMethod` / `Invoke-WebRequest` / `curl` / `wget` / `Net.WebClient.DownloadString` / `DownloadFile` /
`Start-BitsTransfer`, piped or passed to `iex`, `&`, `[scriptblock]::Create`, through a variable
(`$s = irm …; iex $s`), and downloads of a `.ps1` / `.exe` / `.msi` / `.bat` that are then started with `&` or
`Start-Process`. Examples:

```powershell
irm https://christitus.com/win | iex
iex (irm https://example.org/x.ps1)
iex(New-Object Net.WebClient).DownloadString('https://example.org/x.ps1')
& ([scriptblock]::Create((irm https://example.org/x.ps1)))
```

### Trusted sources

**Settings → Safety → Trusted sources** lists sources whose download-and-run code is shown as **info**
("Runs code from trusted source X", with the URL) instead of danger, so it doesn't ask. **Trusting a source means
trusting whatever it serves at the moment you run the script**: Shelly can't see that code.

- Only **HTTPS** URLs match. Plain `http://`, and `irm example.com/x` without a scheme (which PowerShell fetches over
  http), are never trusted.
- A host matches itself and its subdomains (`christitus.com` covers `www.christitus.com`, not
  `christitus.com.evil.example`).
- On shared hosts (`github.com`, `raw.githubusercontent.com`, gists, `aka.ms`, URL shorteners, paste and file hosts,
  CDNs, cloud storage) only **host + path** is accepted: `raw.githubusercontent.com/owner/repo/`, never the whole host.
  IP addresses are not accepted.
- The **Safety check** dialog offers **Trust this source (host)** for each HTTPS URL it found. It adds the source and
  checks the script again.

Shelly ships a **built-in** list (marked *built-in*; each one can be turned off, but not deleted). Each entry is the
install command documented by the project itself:

| Source | For | Documented at |
|---|---|---|
| `christitus.com` | Chris Titus Tech WinUtil | https://github.com/ChrisTitusTech/winutil#quick-start |
| `get.scoop.sh` | Scoop | https://github.com/ScoopInstaller/Install#typical-installation |
| `community.chocolatey.org/install.ps1` | Chocolatey | https://chocolatey.org/install |
| `aka.ms/install-powershell.ps1` | PowerShell (Microsoft) | https://github.com/PowerShell/PowerShell/blob/master/tools/install-powershell.ps1-README.md |
| `raw.githubusercontent.com/PowerShell/PowerShell/` | PowerShell repository (Microsoft) | same |
| `dot.net/v1/dotnet-install.ps1` | .NET install script (Microsoft) | https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script |
| `astral.sh` | uv (Astral) | https://docs.astral.sh/uv/getting-started/installation/ |
| `deno.land/install.ps1` | Deno | https://docs.deno.com/runtime/getting_started/installation/ |
| `bun.sh`, `bun.com` | Bun | https://bun.sh/docs/installation |
| `get.pnpm.io` | pnpm | https://pnpm.io/installation |
| `ohmyposh.dev` | Oh My Posh | https://ohmyposh.dev/docs/installation/windows |

Activation and piracy tools are deliberately not included. The example `Examples\Chris Titus WinUtil.ps1` runs
`irm https://christitus.com/win | iex` as administrator (`# Shelly-Mode: admin`): it shows the info notice, the
administrator confirmation and the UAC prompt, and WinUtil opens in its own window.

### Downloaded from the internet (Mark of the Web)

Browsers, mail and chat programs mark downloaded files with an NTFS stream `Zone.Identifier`. Shelly reads it:

- **Internet** or **restricted** zone (ZoneId 3 or 4): the row shows an orange **download arrow**; the tooltip shows
  the download URL and page. The script **doesn't run** from anywhere (list, tray, hotkey, jump list, history,
  omnibox) and **can't be built**. Running it opens **Downloaded from the internet** with the URLs and the safety check
  result. **Unblock** removes the mark (like *Unblock* in the file's Properties); then start the script again: it goes
  through the normal check.
- The same applies to local scripts and modules the script loads by a fixed path (`. .\lib.ps1`,
  `. "$PSScriptRoot\lib.ps1"`, `& .\x.ps1`, `Import-Module .\m.psm1`); paths built at run time are not followed.
- **Intranet** or **trusted sites** (ZoneId 1 or 2): only a note in the tooltip.
- Shelly never removes the mark by itself. Saving in the Shelly editor, renaming and moving keep it.
- The mark exists only on NTFS. Files on FAT32/exFAT drives, some network shares, files extracted by archivers that
  don't copy the mark, and files created by scripts have none. This part was tested with a simulated stream; please
  verify it on your Windows PC (for example, download a `.ps1` with a browser).

### The confirmation

The **Safety check** dialog lists every finding with its level, line and code. **Open in editor** opens the script
instead.

- **Danger**: tick **I understand what this script does**; **Run** unlocks after 3 seconds. This happens on every run:
  scripts with danger findings can't be marked as trusted.
- **Warning**: **Don't warn again for this version of the script** trusts it by path + SHA-256 of the file; any change
  brings the warning back. Manage the list in **Settings → Safety → Trusted scripts**.
- **Administrator** runs always ask, even when nothing was found.
- **Not verified** (no PowerShell could check the script): Shelly asks before running and shows the built-in scanner's
  findings.

**Turning warnings off.** Settings → Safety → **Warn before running risky scripts**, or `>` in the omnibox (asks
first). With warnings off, warnings and the administrator question are skipped; **danger findings and downloaded
scripts are still checked on every run**. The marks stay in the list.

### Limits

The check can't see code it doesn't have: what a URL serves, files the script reads or downloads at run time, other
scripts called through variables, compiled programs it starts, or encrypted/obfuscated payloads it can't fold. It
flags such patterns as dynamic or remote code but can't tell good from bad. False positives happen too. When in doubt,
read the script, or run it in a virtual machine or Windows Sandbox.

## 7. The editor

Open it with **Ctrl+E**, the context menu, or **New script** (which creates the script from a template and opens it).
The editor (AvalonEdit) has PowerShell syntax highlighting.

| Keys | Action |
|---|---|
| Ctrl+S | Save |
| F5 | Save and run (inline) |
| Ctrl+F, F3 / Shift+F3 | Find, next / previous |
| Ctrl+G | Go to line |
| Ctrl+Space | Completion. It also opens by itself after `$`, after `-` that follows a space, and after three letters of a command or keyword. |
| Ctrl+= / Ctrl+- | Font size (9–32) |

- Toolbar: save, run, word wrap, line numbers, font size, and **external editor**. The external editor is PowerShell
  ISE when it exists, otherwise Notepad (Ctrl+Shift+E in the list).
- Files are always saved as **UTF-8 with BOM**. A file read in another encoding shows a note that it will be
  converted on save.
- The status bar shows the cursor position, the encoding, unsaved changes, and parse errors and warnings. Click it to
  jump to the first issue.
- Closing the editor or Shelly with unsaved changes asks first.

## 8. Omnibox commands

Click the search box (Ctrl+K, or Ctrl+F on the Scripts page). Type to filter scripts. Down moves into the list, Enter
runs the selected script, and Esc clears the box. Type `>` for commands; the text after `>` filters them. Use
Up/Down and Enter, or click.

| Command | Does |
|---|---|
| New script | Asks for a name (`Folder\Name` creates it in a folder) and opens the editor |
| New folder | Creates a subfolder of `Scripts` |
| Open Scripts folder | Opens it in Explorer |
| Build folder | Opens `build` in Explorer |
| Refresh list | Rescans `Scripts` |
| Switch language (English / Русский) | Applies at once |
| Theme and colors | Opens Settings |
| Safety warnings: off / on | Toggles the warnings (turning them off asks first) |
| Compatibility check | Opens the Compatibility page |
| History, Settings | Open those pages |
| Install PowerShell 7… | See [section 12](#12-installing-powershell-7) |

## 9. Keyboard shortcuts

**Main window**

| Keys | Action |
|---|---|
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Scripts / Favorites / History |
| Ctrl+, | Settings |
| Ctrl+M | Shrink or expand the navigation pane |
| Ctrl+K | Go to Scripts and focus the omnibox |
| Esc | Close the overlay navigation pane; in the omnibox, clear it |

**Scripts page**

| Keys | Action |
|---|---|
| Ctrl+F | Focus the omnibox (when the output panel isn't focused; there it finds in the output) |
| Ctrl+N / Ctrl+Shift+N | New script / new folder |
| F5 | Refresh the list |
| Enter | Run the selected script |
| Ctrl+Enter or Ctrl+I | Run settings |
| Ctrl+E / Ctrl+Shift+E | Edit in Shelly / in the external editor |
| Ctrl+D | Add to or remove from Favorites |
| Ctrl+B | Build .exe |
| F2 | Rename the script or folder |
| Del | Delete the script or folder (to the Recycle Bin) |
| Enter or Space on a group | Collapse or expand the group |
| ← / → on a group | Collapse / expand |
| Ctrl+Shift+[ | Collapse all groups |
| Ctrl+Shift+] | Expand all groups |

**History page:** Enter runs the entry again, Del deletes it. **Editor:** see [section 7](#7-the-editor).

## 10. Tray, global hotkey, autostart, notifications, jump list

- **Tray icon.** Left-click opens Shelly. Right-click shows the menu: **Open Shelly**, your **Favorites** (they run in a
  separate window), and **Exit**. The icon's tooltip shows what is running.
- **Close button.** By default it hides Shelly to the tray. The first time, a notification tells you how to open it
  again. Turn off **Close button hides Shelly to the tray** to make it quit. When inline runs are active, Shelly asks
  before quitting.
- **Global hotkey.** Opens Shelly from anywhere. The default is **Ctrl+Alt+L**. In Settings → General, click the box and
  press a combination with Ctrl, Alt or Win plus a letter, digit, F1–F24 or Space. **Off** disables it, and the reset
  button restores Ctrl+Alt+L. If another program already uses the combination, the previous hotkey stays.
- **Start with Windows** writes `"…\Shelly.exe" --minimized` to `HKCU\…\Run\Shelly`. If an entry points to another
  Shelly folder, Settings tells you; switch the toggle off and on to point it to this one. **Start minimized to the
  tray** hides the window at every start.
- **Notifications.** When an inline run finishes (success or failure) after more than the set delay (20 seconds by default) while Shelly is not in
  front, a tray notification appears. Click it to see the output. You can turn it off in Settings → Run, and pick the
  delay there under **Notify after**: 10, 20, 30 or 60 seconds. (`toastAfterSec` in `launcher-settings.json` takes any
  value from 0 to 3600.)
- **Jump list.** Right-click Shelly on the taskbar to run a favorite. It uses your remembered mode, and Shelly starts if
  it isn't running. You can turn it off in Settings → Run.

## 11. Build to .exe

**Ctrl+B** or **Build .exe** in the context menu compiles the script into a console `.exe` with the C# compiler of .NET
Framework 4, so you don't need to install anything.

- Output: `build\<same subfolders as in Scripts>\<Name>.exe`. Shelly asks before overwriting, then shows the file in
  Explorer.
- A script downloaded from the internet must be unblocked first (see [section 6](#6-safety-checks)).
- A script with danger findings goes through the full danger review (tick **I understand**, wait 3 seconds,
  **I have reviewed it**).
- If the script has any warning or danger, a Mark of the Web, or couldn't be checked by PowerShell, a second dialog
  asks you to **type the script's name or `BUILD`**. A checkbox isn't enough on purpose: the `.exe` runs the script
  without any of Shelly's checks, and building gives no trust. Don't distribute `.exe` files built from scripts you
  haven't verified.
- The engine is fixed at build time: the script's pinned engine, or what Auto picked. If that engine is missing on the
  target PC, the exe uses the other one and prints a note.
- The exe icon is the script's custom picture (PNG is converted), otherwise `assets\shelly.ico`. The `# Shelly-Icon`
  glyph is not used for exe files.
- When it runs, the exe writes a temporary copy of the script next to itself (`.<Name>-<pid>.ps1`, or in `%TEMP%` if
  that fails), so `$PSScriptRoot` is the exe's folder. It runs the copy with `-NoProfile -ExecutionPolicy Bypass`,
  passes its own arguments to the script (for example, `Name.exe -Count 5 -Loud`), and deletes the copy at the end.
- The build log is written to `shelly.log`. If the build fails, Shelly shows the compiler output.

## 12. Installing PowerShell 7

Shelly finds PowerShell 7 in Program Files (stable and preview), through App Paths and PATH, and as the Microsoft Store
app through its app execution alias in `%LOCALAPPDATA%\Microsoft\WindowsApps`.

To install it, use **Install PowerShell 7…** (Settings → Engines, the Compatibility page, the info bar, `>` commands,
or the dialog shown when a script needs 7):

- **With winget**, if it's available: a separate window runs `winget install --id Microsoft.PowerShell --source winget
  --accept-package-agreements --accept-source-agreements`.
- **Without winget**: **Download and run installer** gets the latest MSI (x64 or ARM64) from the PowerShell GitHub
  releases and starts it. **Open download page** opens the release page in your browser.

Shelly detects the new installation by itself, with no restart needed. **Check again** in Settings → Engines or on the
Compatibility page re-runs the search.

## 13. Updates

When **Check for updates at startup** is on (Settings → Updates; it is on by default), Shelly asks GitHub Releases
(`api.github.com/repos/grvelvet/shelly/releases/latest`) for the latest version about 4 seconds after start, about
once a day (at most once every 20 hours). If a newer version exists, an info bar and the About card show **Download**, which opens the
release page. Nothing is downloaded or installed automatically. **Check now** in Settings → About checks right
away.

## 14. Settings reference

| Section | Setting | Default |
|---|---|---|
| General | Language: English / Русский (applies at once) | English |
| | Start with Windows | off |
| | Start minimized to the tray | off |
| | Close button hides Shelly to the tray | on |
| | Global hotkey | Ctrl+Alt+L |
| Appearance | Theme: System / Light / Dark | System |
| | Accent color: Windows accent or one of 9 colors | Windows accent |
| Run | Notify when a long run finishes | on |
| | Notify after: 10 / 20 / 30 / 60 s | 20 s |
| | Favorites in the taskbar jump list | on |
| Engines | Default engine for Auto: Auto (7 if installed, else 5.1) / 5.1 / 7 | Auto |
| | PowerShell 7 and Windows PowerShell 5.1 status, Check again, Install | |
| Safety | Warn before running risky scripts | on |
| | Trusted scripts (Revoke, Clear all) | |
| | Trusted sources: built-in entries (on/off) and your own (Add, Remove) | built-ins on |
| History | Keep runs: 50 / 100 / 200 | 100 |
| | Saved output per run: 256 KB / 1 MB / 5 MB / 20 MB | 1 MB |
| Folders and files | Scripts folder, build folder, settings file (Open / Show), script icons | |
| Updates | Check for updates at startup | on |
| About | Version, Check now, links to GitHub, the user guide (in the UI language), README, CHANGELOG, LICENSE, the disclaimer, the log | |

The Scripts page also remembers the sort (`listSort`, `listSortDesc`), the view (`listView`) and the collapsed groups
(`collapsedGroups`). The filters are not saved.

`launcher-settings.json` is plain JSON. Shelly saves it itself; close Shelly before editing it by hand. Keys left over
from 0.9.x (`favTilesCollapsed`, `detailsCollapsed`, `detailsOpen`, `historyThisScript`, `migrationDeclined`,
`launcherHost`) are read and then dropped on the next save. Unknown keys are kept.

## 15. Troubleshooting and FAQ

**"Scripts are disabled on this system" / execution policy.** Shelly runs scripts with `-ExecutionPolicy Bypass`, so
the local policy doesn't matter. A policy set by **Group Policy** can't be bypassed: if it is Restricted or blocks
unsigned scripts, Windows blocks them. The Compatibility page shows the policy.

**A script doesn't run: "Downloaded from the internet".** Windows marked the file as downloaded. Read it, then click
**Unblock** in the dialog that opens when you run it (see [section 6](#6-safety-checks)).

**Windows SmartScreen says "Windows protected your PC" / unknown publisher.** `Shelly.exe` isn't code-signed. Check
the SHA-256 of the zip against `SHA256SUMS.txt` from the release (`Get-FileHash .\shelly-1.0.11.zip`), then click
**More info → Run anyway**.

**Shelly doesn't start: ".NET is missing".** Install the .NET 10 Desktop Runtime (x64), or use the self-contained build.
See [section 1](#1-install-and-requirements).

**PowerShell 7 from the Microsoft Store isn't found.** Shelly starts Store installs through the `pwsh.exe` app
execution alias. Make sure the alias is on (Windows Settings → Apps → Advanced app settings → App execution aliases),
then click **Check again** in Settings → Engines.

**A script waits for input and hangs, or `Read-Host` fails.** Inline runs have no console input. Use **Run in separate
window**, or remember that mode in Run settings.

**Garbled non-English text in Windows PowerShell 5.1.** Save the script as UTF-8 with BOM, for example by opening and
saving it in the Shelly editor.

**Wrong engine.** The engine mark's tooltip says why Auto picked it. Pin an engine in Run settings, or add `#Requires`
to the script.

**"Build .exe" is unavailable.** The C# compiler of .NET Framework 4 wasn't found. See the Compatibility page.

**Slow start.** Run `Shelly.exe --perf` (or set `SHELLY_PERF=1`, or add `"perfLog": true` to `launcher-settings.json`)
and read `%TEMP%\Shelly\perf.log`.

**Log.** `%TEMP%\Shelly\shelly.log` (rotated at 512 KB). It is also linked from Settings → About.

## 16. Privacy

Shelly has no telemetry, analytics or accounts. It contacts the network only:

- for the update check: one request to `api.github.com` when the update check is on, or when you click **Check now**;
- when you choose **Install PowerShell 7**: winget, or the PowerShell GitHub releases for the MSI;
- when you click a link (GitHub, download pages), which opens your browser.

Your scripts, settings, history and logs stay on your PC. Your scripts themselves can, of course, do anything their code
does.
