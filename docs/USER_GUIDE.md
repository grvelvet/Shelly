# Shelly 1.5.0 User Guide

[Русская версия](USER_GUIDE_RU.md) · [README](../README.md) · [CHANGELOG](../CHANGELOG.md)

Shelly is a portable Windows 10/11 launcher for PowerShell, Python, Batch, and custom interpreter scripts. It indexes scripts from `Scripts` and external folders, builds parameter dialogs, checks safety before launch, captures live console output, keeps a full run history, schedules tasks, includes a built-in editor with a live linter, and compiles `.ps1` scripts to `.exe`.

## Contents

1. [Install, requirements and folder layout](#1-install-requirements-and-folder-layout)
2. [The main window: Scripts, Favorites, History](#2-the-main-window-scripts-favorites-history)
3. [Running scripts, parameters, presets and schedules](#3-running-scripts-parameters-presets-and-schedules)
4. [Languages and runtimes](#4-languages-and-runtimes)
5. [Writing scripts for Shelly](#5-writing-scripts-for-shelly)
6. [Safety checks](#6-safety-checks)
7. [The editor and built-in linter](#7-the-editor-and-built-in-linter)
8. [Omnibox, shortcuts and system integration](#8-omnibox-shortcuts-and-system-integration)
9. [Build to .exe](#9-build-to-exe)
10. [Settings reference, updates and FAQ](#10-settings-reference-updates-and-faq)

---

## 1. Install, requirements and folder layout

**Requirements**

- Windows 10 or 11 (x64). Full styling (rounded corners, Mica, colored title bar) is available on Windows 11; Windows 10 uses simplified styling.
- Windows PowerShell 5.1 (built into Windows). PowerShell 7 and Python 3 are optional and only needed for scripts that use them.
- **Build .exe** uses the built-in .NET Framework 4 C# compiler (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).

**Two builds**

| File | `Shelly.exe` | Needs |
|---|---|---|
| `shelly-<version>.zip` (recommended) | ≈ 2 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) or `winget install Microsoft.DotNet.DesktopRuntime.10` |
| `shelly-<version>-selfcontained.zip` | ≈ 70 MB | nothing (runtime bundled inside the exe; slightly slower startup) |

**Install and first run.** Unzip anywhere (for example `C:\Tools\Shelly`) and run `Shelly.exe`. On first start, Shelly displays a short disclaimer (scripts run with your user rights; Shelly is not a sandbox) and initializes its working folders. Only one Shelly instance runs per folder; launching it again brings the existing window forward and passes any `--run` argument to it.

**Where things are stored**

| Data | Location |
|---|---|
| Default scripts folder | `<Shelly folder>\Scripts\` (subfolders become groups; root files go to **General**) |
| Settings | `<Shelly folder>\launcher-settings.json` |
| History & output logs | `<Shelly folder>\launcher-history\` (`history.json` and `<id>.log` files) |
| Built `.exe` files | `<Shelly folder>\build\` (mirrors `Scripts` subfolders) |
| Icons | `<Shelly folder>\assets\shelly.ico` and `<Shelly folder>\assets\script-icons\` |
| Log, analysis cache, perf log | `%TEMP%\Shelly\` (`shelly.log`, `analysis-cache.json`, `perf.log`) |
| Autostart (if enabled) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Shelly` |

Shelly reads supported scripts up to 8 folder levels deep. Files and folders starting with `.` or `_` (helper Python modules), hidden items, `__pycache__`, `site-packages`, `node_modules`, virtual environments (`pyvenv.cfg`), and directory links (junctions/symlinks) are skipped. External disk changes are picked up automatically; press **F5** to refresh manually.

### External script folders

Besides `Scripts`, you can connect up to 20 external folders anywhere on disk: drag and drop a folder onto the Shelly window or add it in **Settings → Folders and files → Additional folders → Add folder…**.

- **Run in place:** nothing is copied, and the working directory is the script's own folder. Each external folder appears as a group with a link icon (its tooltip shows the full path).
- **Isolated metadata:** scripts are keyed by `ext:<id>\path`, keeping favorites, trust, parameters, and history separate from `Scripts`. Removing a folder in Settings leaves the files on disk untouched and discards Shelly's metadata for them.
- **Limits:** up to 8 levels deep, 1,000 scripts, and 2,000 subfolders; junctions/symlinks are not followed. Rename, Delete, and **Build .exe** are disabled for external scripts (Edit modifies the file in place). Overlapping folders or `Scripts` itself cannot be added.

### Importing a folder (scripts with their own files)

When a script depends on local configs, data, or modules, use **Import folder…** (`>` commands or the `Scripts` group menu). Dropping a folder onto the window connects it as an external folder; dropping individual files copies them into `Scripts`.

1. Shelly scans the folder in the background. Over **100 MB** or **2,000 files**, it suggests **Add as external folder** or **Import anyway**.
2. In the dialog, choose the folder name inside `Scripts` and untick any helper scripts you want copied without appearing in the list (stored in `.shelly.json`).
3. Files are copied atomically via a temporary folder. Python environments (`.venv`, `venv`, `env` with `pyvenv.cfg`), `__pycache__`, `.git`, `node_modules`, and symlinks are skipped.

Imported scripts show a folder mark and run in their own folder. Deleting the last visible script offers to move the entire imported folder to the Recycle Bin (if multiple scripts are visible, you can instead hide just that entry via `.shelly.json`). Resolve companion files relative to the script (`$PSScriptRoot` in PowerShell, `Path(__file__).parent` in Python). Pre-run SHA-256 verification covers the script file only.

---

## 2. The main window: Scripts, Favorites, History

The navigation pane switches between **Scripts** (`Ctrl+1`), **Favorites** (`Ctrl+2`), **History** (`Ctrl+3`), and **Settings** (`Ctrl+,`). `Ctrl+M` or `☰` collapses the pane to icons.

### Scripts page

- **Toolbar:** *New script*, *New folder* | *Sort* (Name, Date modified, Last run, Run count), *Collapse / Expand all* (`Ctrl+Shift+[` / `]`), *View* (**Cards** 64 px or **Compact list** 40 px), *Filter* (Ran before, Never run, Needs PS 5.1, Has safety warnings, Run as admin, Favorites only; filters reset on exit), the **omnibox** (filters by name, folder, and description, or `>` commands), *Refresh* (`F5`), and *Open folder*. Narrow windows fold buttons into `…`.
- **Script rows:**
  - The **favorite star** appears directly before the script title when starred.
  - Titles **preserve the file extension** (`.ps1`, `.py`, `.bat`, `.cmd`, or custom extension). Badges appear for **venv**, custom interpreter names, scheduled clocks, and imported folders.
  - **Safety shield:** green = trusted for the current SHA-256, amber = warnings, red = danger findings or Mark of the Web (orange download arrow); clock = checking AST, `?` = could not verify. Clean scripts show no shield.
  - **Last result:** shown only for failed runs (with exit code) or stopped runs. Hovering reveals ▶ *Run* and ⚙ *Run settings* (while active: spinner, timer, and ⏹ *Stop*).
- **Context menu:** *Stop*, *Run*, *Run with inline output / in separate window*, *Run as administrator*, *Run with preset*, *Run settings…*, *Schedule…*, *Edit*, *Open in external editor*, *Build .exe*, *Add to / Remove from favorites*, *Remove from trusted* (when trusted), *Change icon… / Default icon*, *Show runs*, *Rename*, *Open file location*, *Copy path*, *Delete* (to Recycle Bin).

### Output panel, Favorites, and History

- **Output panel (bottom):** streams the selected run's output live (up to 4 MB or 100,000 lines, noting any truncation). Actions: *Stop*, *Run again*, *Open script*, *Copy*, *Save to `.txt`*, *Clear view*, *Word wrap*, *Find* (`Ctrl+F`).
- **Favorites (`Ctrl+2`):** tiles of starred scripts. Sort by *Manual* (reorder via context menu; preserved in the tray menu and taskbar jump list), *By name*, *By last run*, *By run count*, or *By type* (PowerShell → Python → Batch → custom). Tile size: *Small*, *Medium*, *Large*, or `Ctrl + mouse wheel`.
- **History (`Ctrl+3`):** records every run (inline, separate window, admin, tray, editor, scheduled) with status, exit code, duration, engine, mode, preset, parameters, and **full captured output** (including separate-window runs). Filter by outcome, search, press `Enter` to re-run with the same parameters, or `Del` to remove.

---

## 3. Running scripts, parameters, presets and schedules

### Execution modes

| Mode (`# Shelly-Mode:`) | Behavior |
|---|---|
| **Separate window** (`window`, default) | Opens a console window (`-NoExit`, stays open after completion) while streaming output live into Shelly's output panel and History via transcript capture. Interactive input (`Read-Host`, `input()`, `pause`) works normally. |
| **Inline output** (`inline`) | Runs hidden with `-NonInteractive` and streams stdout/stderr directly into the output panel. No keyboard input. |
| **As administrator** (`admin`) | Elevates via UAC; always asks for confirmation and re-verifies SHA-256 after elevation. |
| **GUI mode** (`gui`) | Launches GUI scripts without flashing a console window. |
| **Hidden mode** (`hidden`) | Runs silently in the background with no console window or taskbar flash. |

Precedence for ▶ / `Enter` / double-click: remembered per-script mode → `# Shelly-Mode:` header → **Settings → Run → Default run mode**. Context menu items enforce their named mode; tray favorites always open in a window; `F5` in the editor always runs inline.

Different scripts can run concurrently; the same script runs inline at most once at a time. **Stop** (`⏹`) terminates the entire child process tree.

### Run settings and parameter presets

**Run settings** (`Ctrl+I` or `Ctrl+Enter`) displays:
- for PowerShell, a dynamic form generated from `param()` (mandatory `*`, types, checkboxes, `ValidateSet` dropdowns); for Python, Batch, and custom scripts, a single **Arguments** field;
- PowerShell engine (`Auto / 5.1 / 7`) and execution mode selectors;
- **Remember for this script** to persist values and mode as the new default.

**Parameter presets** save named sets of values via *Save as…*. Launch them from **Run with preset** in the context menu or assign them to schedules. Missing parameters after script edits are skipped with a warning. **Presets are stored as plain text in `launcher-settings.json` — do not store passwords or tokens in them.**

### Scheduled runs

**Schedule…** in the context menu creates a Windows Task Scheduler task under `\Shelly\` running as your user (no administrator rights needed). Schedules support *Once*, *Daily*, *On days of the week*, *At Windows sign-in* (+30 s), or *Every N minutes* (1–1440), with an optional preset and *Hidden* or *Separate window* mode. Scripts configured to run as administrator cannot be scheduled.

Tasks invoke `Shelly.exe --scheduled <id>`. If Shelly is open, it runs the script directly; otherwise a headless instance runs it, logs to History, and fires a notification. Unattended runs cannot prompt: any untrusted finding, danger prompt, Mark of the Web, missing preset, or missing mandatory parameter **skips the run** (exit code `-1` with the reason in History). Manage, run immediately (*Run now*), or *Repair* tasks after moving Shelly in **Settings → Schedules**.

### Command line

| Argument | Effect |
|---|---|
| `--run "Folder\Script.ps1"` (also `-run`, `/run`) | Runs a script relative to `Scripts` (or `ext:<id>\Script.ps1` for external folders) |
| `--preset "<name>"` | Used with `--run` to apply a parameter preset |
| `--headless` | Used with `--run` for unattended execution (like a schedule) |
| `--scheduled <id>` | Internal entry point invoked by Task Scheduler |
| `--history <run id>` | Opens Shelly at the specified History entry |
| `--minimized` (also `-minimized`, `/minimized`, `--tray`) | Starts minimized in the system tray |
| `--perf` | Writes startup timings to `%TEMP%\Shelly\perf.log` |

---

## 4. Languages and runtimes

- **PowerShell (`.ps1`):** **Auto** inspects `#Requires` (`-Version`, `-PSEdition`) and script syntax (PS 7 features like `??`, `&&`, `? :`, `ForEach-Object -Parallel` vs 5.1-only commands like `workflow`), falling back to **Settings → Environment → PowerShell version** (*System*, *5.1*, or *7+*). Runs use `-NoProfile -ExecutionPolicy Bypass`. Install PowerShell 7 via the `>` command *Install PowerShell 7…* (using `winget` or the verified GitHub MSI).
- **Python (`.py`):** configured in **Settings → Environment → Python**. Resolution order: nearest virtual environment (`.venv`, `venv`, or `env` containing `pyvenv.cfg` walking up from the script's folder to the root of `Scripts` or the external folder, when *Use .venv next to the script* is enabled) → custom `python.exe` path → auto-detect (`py -3`, `PATH` skipping the empty Store stub, `%LOCALAPPDATA%\Programs\Python`, `Program Files`). Runs as `python -u script.py` with `PYTHONUTF8=1`, `PYTHONIOENCODING=utf-8`, and `VIRTUAL_ENV`/`PATH` configured. A broken `.venv` (missing `python.exe`) fails explicitly instead of falling back to system Python.
- **Batch (`.bat`, `.cmd`):** built in via `%ComSpec% /d /s /c "call …"` with no setup required. UTF-8 files without BOM run `chcp 65001`; inline output is decoded line by line (UTF-8 or OEM code page). `"`, `%`, and `^` are rejected in *Arguments*. Scripts needing interactive input (`pause`, `set /p`) should run in a separate window. Saving `.bat` as UTF-8 with BOM triggers a warning (`cmd.exe` fails on the BOM).
- **Custom interpreters:** in **Settings → Environment → Custom interpreters**, map any non-built-in extension to a program with an arguments template (`{script} {args}`, `{dir}`) and working folder choice. Arguments are quoted with `CommandLineToArgvW`.

### Language capability matrix

| Capability | PowerShell | Python | Batch | Custom |
|---|:-:|:-:|:-:|:-:|
| Parameter form (`param()`) | yes | no | no | no |
| Arguments box | no | yes | yes | yes |
| Parameter presets | yes | yes | yes | yes |
| Pre-run safety check | full (AST) | no | basic | no |
| Live editor linter (`F8`) | yes (AST + rules) | yes (rules + `ast.parse`) | yes (rules + scan) | basic |
| Syntax highlighting (light / dark) | yes | yes | yes | by extension |
| Code completion (`Ctrl+Space`) | yes | yes | yes | no |
| Mark of the Web, UAC, schedules | yes | yes | yes | yes |
| Window output capture & History | yes | yes | yes | yes |
| Auto-detect & `.venv` | PS 5.1 / 7 | Python + `.venv` | `cmd.exe` | no |
| Build `.exe` | yes | no | no | no |

**What Shelly does not do:** it is not a sandbox; inline runs do not accept keyboard input; administrator scripts cannot be scheduled; Python, Batch, and custom scripts cannot be built into `.exe`; there is no cloud sync.

---

## 5. Writing scripts for Shelly

- **Description:** extracted from the first `#` comment within the first 8 lines (`Description:` / `Описание:` prefixes are stripped; `#Requires`, `#!`, and `# Shelly-…` are skipped) or the first paragraph of `.SYNOPSIS` in a `<# … #>` block (within the first 60 lines).
- **Header directives:**
  - `# Shelly-Icon: E9D2` (within the first 40 lines; `E9D2`, `U+E9D2`, `0xE9D2`, or `\uE9D2`) sets a Segoe Fluent Icons glyph (custom `.ico`/`.png` files set via *Change icon…* override it).
  - `# Shelly-Mode: window | inline | admin | gui | hidden` sets the default execution mode.
- **Encoding:** save `.ps1` files as **UTF-8 with BOM** (required for non-ASCII text in Windows PowerShell 5.1); save `.py`, `.bat`, `.cmd`, and custom scripts as **UTF-8 without BOM**. Shelly's editor enforces this automatically.
- **PowerShell `param()` mapping:**

| Declaration in `.ps1` | Form control | Passed as |
|---|---|---|
| `[string]$Name`, `[datetime]$When` | text box | `-Name 'value'` |
| `[int]$Count = 3` (numeric types) | text box (placeholder `3`) | `-Count '5'` |
| `[switch]$Loud`, `[bool]$Flag` | checkbox | `-Loud` / `-Loud:$false`, `-Flag:$true` |
| `[string[]]$Items` | comma-separated text box | `-Items 'a','b'` |
| `[ValidateSet('A','B')]` | drop-down list | `-Param 'A'` |
| `[Parameter(Mandatory, HelpMessage='…')]` | required `*` + tooltip | — |

**Example `.ps1`:**

```powershell
# Description: Copies files of a selected extension.
# Shelly-Icon: E8C8
#Requires -Version 5.1

param(
    [Parameter(Mandatory = $true, HelpMessage = 'Source folder')]
    [string]$Source,

    [Parameter(Mandatory = $true, HelpMessage = 'Destination folder')]
    [string]$Destination,

    [ValidateSet('txt', 'log', 'csv')]
    [string]$Extension = 'txt',

    [switch]$WhatIf
)

$files = Get-ChildItem -LiteralPath $Source -Filter "*.$Extension" -File
if (-not $files) { Write-Host "No matching files"; exit 1 }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach ($f in $files) {
    if ($WhatIf) { Write-Host "Would copy $($f.Name)" }
    else { Copy-Item -LiteralPath $f.FullName -Destination $Destination }
}
```

---

## 6. Safety checks

> **A guardrail, not a sandbox.** Scripts run with your Windows account rights (full control when elevated). Static analysis helps spot risky commands but cannot guarantee safety. Run only scripts you trust and have read.

### How the check works

For `.ps1` scripts, Shelly uses PowerShell's own parser (AST) in PowerShell 7 or 5.1 (while waiting, a fast built-in C# scanner runs and shows *checking*). Before launch, Shelly waits up to 60 seconds for the analysis result.

The analyzer resolves:
- **commands & aliases:** built-in PS 5.1 and 7 aliases (`rm`, `ri`, `del`, `iwr`, `irm`, `iex`, `curl`, `wget`…), script-defined `Set-Alias` / `New-Alias` chains, module-qualified names, and shortened parameters;
- **constant folding & nesting:** constant variables, string concatenation, `-f`, `-join`, splatting (`@params`), and nested payloads inside `cmd /c`, `powershell -Command`, `Start-Process`, `Invoke-Command`, and `Start-Job` (nesting deeper than 3 levels triggers a warning);
- **.NET, WMI, and native tools:** `[IO.File]::Delete`, `[Microsoft.Win32.Registry]`, `Process.Start`, `Win32_ShadowCopy`, `Win32Shutdown`, `vssadmin`, `wbadmin`, `bcdedit`, `diskpart`, `format`, `cipher /w`, `takeown`/`icacls`, `reg delete`, `schtasks`, `sc delete`, `wevtutil cl`, `Set-MpPreference`, `netsh advfirewall`, `mshta`/`regsvr32`/`msiexec`/`certutil`, and `Run` startup keys.

Batch files receive a simplified scan (no `%VAR%` expansion). Python and custom scripts are not scanned for commands.

### Severities and confirmation

| Severity | Shield | Behavior | Examples |
|---|---|---|---|
| **Danger** | red | Requires checking *I understand* + a 3-second wait. Confirmation is remembered by **path + SHA-256** and re-prompts whenever the file changes. | Deleting in system folders, drive roots, profiles, or HKLM; formatting, `bcdedit`, disabling Defender/firewall; deleting shadow copies/services/event logs; **dynamic invocation** (`iex`, `& $var`, `-enc`, `FromBase64String` + execution, reflection, `Add-Type` from variable); **downloading and running code**. |
| **Warning** | amber | Prompts before launch. *Don't warn again for this version* grants a **green shield** tied to the current SHA-256. | `Remove-Item -Recurse` outside TEMP; `Start-Process -Verb RunAs`; HKLM / autostart writes; inline C# `Add-Type`; scheduled tasks; deep nesting or analysis failure. |
| **Info** | none | Shown in the safety dialog without blocking execution. | Deletion in `$env:TEMP` without `-Recurse`; calling local `.ps1` files; code from a **trusted source**. |

Revoke trust at any time from the script's context menu (**Remove from trusted**). **Administrator** runs always prompt. Turning off warnings in **Settings → Safety** only suppresses analyzer prompts — **Mark of the Web and UAC checks remain active**.

### Trusted sources and Mark of the Web

- **Trusted sources** (**Settings → Safety**): HTTPS endpoints whose download-and-run commands (`irm … | iex`) downgrade to *Info*. Plain `http://` and IP addresses are never trusted; shared hosts (`github.com`, `raw.githubusercontent.com`, `aka.ms`, CDNs) require `host/owner/repo` or a file path. Built-in entries (WinUtil, Scoop, Chocolatey, PowerShell, .NET, uv, Deno, Bun, pnpm, Oh My Posh) can be toggled individually.
- **Mark of the Web (`Zone.Identifier`):** files marked from the Internet or Restricted sites (ZoneId 3 or 4) on NTFS show an orange download arrow and are **blocked from running and building `.exe`** until you click **Unblock** (applies to statically loaded local modules too). Shelly never removes MOTW silently.

---

## 7. The editor and built-in linter

Open the built-in AvalonEdit editor with `Ctrl+E`, *Edit*, or *New script*.

### Features and shortcuts

- **Syntax highlighting** for Light and Dark themes: PowerShell, Python, Batch, and extension-based highlighting for custom scripts.
- **Code completion (`Ctrl+Space`):** supported for PowerShell, Python, and Batch (also triggers automatically on commands, `$` variables, and `-` parameters).
- **Live built-in linter:** underlines issues with red/amber squiggles (`[Code] Message` on hover) and lists them in the collapsible **Problems** panel (`F8` or click the status bar indicator):
  - *PowerShell:* AST parse errors, safety/compatibility findings, empty `catch {}` (`PS-EMPTY-CATCH`), hardcoded secrets (`PS-SECRET`), aliases (`PS-ALIAS`), global `$ErrorActionPreference = 'SilentlyContinue'` (`PS-ERRPREF`).
  - *Python:* mixed tabs/spaces (`PY-TAB-SPACE`), bare `except:` (`PY-BARE-EXCEPT`), mutable defaults (`PY-MUTABLE-DEFAULT`), Python 2 `print` (`PY-PRINT2`), `== None` (`PY-EQ-NONE`), and live `ast.parse` syntax checks (`PY-SYNTAX`).
  - *Batch:* UTF-8 BOM (`BAT-BOM`), `!VAR!` without `EnableDelayedExpansion` (`BAT-DELAYED-EXP`), missing `goto` labels (`BAT-MISSING-LABEL`), single `%i` in `for` (`BAT-FOR-PERCENT`), unbalanced parens (`BAT-PARENS`), and dangerous commands.
  - *Custom scripts:* UTF-8 BOM (`CUSTOM-BOM`) and unmatched quotes (`CUSTOM-QUOTES`).
- **Save encodings:** `.ps1` files are saved as **UTF-8 with BOM**; all other scripts as **UTF-8 without BOM** (non-UTF-8 batch files open in OEM).

| Keys | Action |
|---|---|
| `Ctrl+S` / `F5` | Save / Save and run (inline) |
| `F8` | Toggle the Problems panel |
| `Ctrl+Space` | Code completion |
| `Ctrl+F`, `F3` / `Shift+F3`, `Ctrl+G` | Find, next / previous, go to line |
| `Ctrl+=` / `Ctrl+-` | Font size (9–32) |

### External editor selection

Click the gear button on the editor toolbar or open **Settings → General → Script editor**:
- choose the default editor for `Ctrl+E` (*Built-in Shelly editor* or *External editor*);
- pick the external program (*Automatic* — VS Code → Notepad++ → ISE for `.ps1` / IDLE for `.py` → Notepad; *VS Code*; *Notepad++*; *PowerShell ISE* for `.ps1`; *Notepad*; or *Custom program…* with `{file}` and `{line}`).
- `Ctrl+Shift+E` in the script list always opens the file in the external editor at the target line.

---

## 8. Omnibox, shortcuts and system integration

### Omnibox commands (`>`)

Focus the search box (`Ctrl+K`, or `Ctrl+F` on Scripts) and type `>`:
- *New script*, *New folder*, *Add scripts…*, *Import folder…*, *Refresh list*
- *Open Scripts folder*, *Build folder*
- *Switch language (English / Русский)*, *Theme and colors*, *Script editor…*
- *Safety warnings: off / on*
- *History*, *Settings*, *Install PowerShell 7…*

### Main window shortcuts

| Keys | Action |
|---|---|
| `Ctrl+1` / `Ctrl+2` / `Ctrl+3` / `Ctrl+,` | Scripts / Favorites / History / Settings |
| `Ctrl+M` / `Ctrl+K` | Collapse navigation pane / Focus omnibox |
| `Ctrl+N` / `Ctrl+Shift+N` | New script / New folder |
| `F5` / `Ctrl+F` | Refresh list / Search (in output panel: find in output) |
| `Enter` / `Ctrl+Enter` (`Ctrl+I`) | Run / Run settings |
| `Ctrl+E` / `Ctrl+Shift+E` | Built-in editor / External editor |
| `Ctrl+D` / `Ctrl+B` | Toggle favorite / Build `.exe` |
| `F2` / `Del` | Rename / Delete (in History: remove entry) |
| `Ctrl+Shift+[` / `Ctrl+Shift+]` | Collapse / expand all groups |

### Tray, hotkey, notifications, and Jump List

- **Tray & close behavior:** by default, `✕` minimizes Shelly to the tray (left-click restores, right-click lists Favorites; closing while inline runs are active prompts first).
- **Global hotkey:** defaults to `Ctrl+Alt+L` (customize or disable in **Settings → General**).
- **Start with Windows:** registers `"…\Shelly.exe" --minimized` under `HKCU\…\Run\Shelly` (flags mismatches if moved to another folder).
- **Notifications (toast):** *Off*, *Only errors*, or *Always*, with minimum duration threshold (*any*, 10, 20, 30, 60 s; `toastAfterSec` supports 0–3600). Clicking a notification opens the run in History.
- **Taskbar jump list:** right-click Shelly on the taskbar to launch favorites in their remembered mode.

---

## 9. Build to .exe

`Ctrl+B` or **Build .exe** (`.ps1` only) compiles a standalone console `.exe` in `build\<subfolders>\<Name>.exe` using the built-in `csc.exe`.

- **Safety:** files with Mark of the Web must be unblocked first. Scripts with any warning, danger finding, or no PowerShell check require typing the script name or `BUILD` in a second confirmation dialog. **Do not distribute `.exe` files built from unverified scripts: the executable runs without Shelly's checks.**
- **Engine & icon:** embeds the script's pinned or Auto-selected engine (falling back if missing on the target PC). Uses the script's custom `.ico`/`.png` or `assets\shelly.ico` (`# Shelly-Icon` glyphs are not embedded).
- **Runtime behavior:** writes a temporary `.ps1` copy with a random GUID suffix next to the `.exe` (or in `%TEMP%` if read-only) so `$PSScriptRoot` points to the `.exe` folder, forwards all CLI arguments, and deletes the temporary script on exit.

---

## 10. Settings reference, updates and FAQ

### Settings (`launcher-settings.json`)

Close Shelly before editing `launcher-settings.json` manually. Unknown keys are preserved on save.

| Section | Key settings | Default |
|---|---|---|
| **General** | Language (`English` / `Русский`), Start with Windows, Start minimized, Close button hides to tray, Global hotkey, Script editor | `English`, tray on, `Ctrl+Alt+L`, built-in |
| **Appearance** | Theme (`System` / `Light` / `Dark`), Accent color (Windows accent or 9 presets) | System |
| **Run** | Default run mode (`In a separate window` / `Inline output`), Notifications, Only if longer than, Favorites in jump list | Separate window, Always, 20 s, on |
| **Schedules** | Scheduled tasks: enable/disable, Run now, Edit, Delete, Repair, clean leftover tasks | none |
| **Environment** | Runtime (`PowerShell` / `Python` / `Custom interpreters`), PS version (`System` / `5.1` / `7+`), Python path & `.venv`, Custom interpreters (up to 20) | PS System, Python auto + `.venv` on |
| **Safety** | Warn before running risky scripts, Trusted sources (built-in toggles + custom entries) | on |
| **History** | Keep runs (`50` / `100` / `200`), Saved output per run (`256 KB` / `1 MB` / `5 MB` / `20 MB`) | 100, 1 MB |
| **Folders and files** | Default folder (`Scripts`), Additional folders (up to 20 external), Build folder, `launcher-settings.json` | none added |
| **Updates** | Check for updates at startup (checks GitHub Releases at most once every 20 h; never auto-downloads) | on |
| **About** | Version and GitHub repository link | — |

### Troubleshooting and FAQ

- **"Scripts are disabled on this system":** Shelly passes `-ExecutionPolicy Bypass`, overriding local policies, but cannot bypass **Group Policy (GPO)** restrictions.
- **SmartScreen "Unknown publisher":** `Shelly.exe` is unsigned. Verify the archive hash against `SHA256SUMS.txt` (`Get-FileHash .\shelly-1.5.0.zip`), then click **More info → Run anyway**.
- **".NET is missing":** install [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) or use the `-selfcontained.zip` build.
- **Microsoft Store PowerShell 7 not detected:** enable the `pwsh.exe` alias under *Windows Settings → Apps → Advanced app settings → App execution aliases*.
- **Scripts using `Read-Host`, `input()`, or `pause` fail or hang:** switch them to **Separate window** mode.
- **Garbled non-ASCII text in PowerShell 5.1:** re-save the file in Shelly's editor to apply **UTF-8 with BOM**.
- **Slow startup & logs:** run `Shelly.exe --perf` (`%TEMP%\Shelly\perf.log`). Runtime errors and failed runs are logged to `%TEMP%\Shelly\shelly.log` (rotates at 512 KB).

### Privacy

Shelly includes no telemetry, analytics, or accounts. Network requests occur only for update checks (`api.github.com`), user-initiated PowerShell 7 installation, or opening clicked links in your browser. All scripts, settings, history, and logs remain on your PC.
