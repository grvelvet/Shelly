# Shelly 1.4.1 user guide

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

### External script folders

Besides `Scripts`, Shelly can list the scripts of any other folders on your disk (up to 20), for example one folder per
project, each with a script that works on the files in that folder.

- **Add:** drop a folder onto the Shelly window, or open **Settings → Folders and files → Additional folders → Add folder…**, then pick the folder. **Open**
  shows it in Explorer, **Remove** takes it off the list (the folder and its files are not touched; the favorites, trust,
  run settings, icons and engine choice kept for its scripts are forgotten).
- **In the list:** each folder is a group named after it with the link icon (the full path when two folders
  have the same name), with its subfolders below. The tooltip of the group shows the folder on disk. Search, filters,
  sort, favorites, history, the tray and the jump list work for these scripts as for the others.
- **Run in place:** nothing is copied. The script runs from its own file with its own folder as the working directory,
  so a script that handles "the files here" works on the files of that folder.
- **Same safety:** the analysis, the danger review, the SHA-256 check right before the start, the Mark of the Web check
  and administrator runs are the same as for scripts in `Scripts`. A script is identified by `ext:<id>\path` (the id comes
  from the folder's full path), so trust, favorites and history of an added script never mix with a script of the same
  name in `Scripts` or in another folder. Moving a folder to another path makes its scripts new ones.
- **Limits:** a folder is read down to 8 levels, at most 1000 scripts and 2000 subfolders. Hidden files and names that
  start with a dot are skipped. Links (junctions and symbolic links) inside an added folder are not followed, so a link
  cannot lead out of the folder. A folder that cannot be read right now (an unplugged drive, no access) is skipped and
  marked "not found" in Settings; it comes back by itself when it is available again.
- **Not allowed for these scripts:** rename, delete and **Build .exe** are not offered; **Edit** works on the file in
  place. A folder cannot be added if it is `Scripts` or lies inside it, contains `Scripts`, is already added, or overlaps
  an added folder (one lies inside the other). Drag and drop still copies only into `Scripts`.
- Changes to `.ps1` files in added folders show up by themselves; a new subfolder shows up after **F5**.

**Command line**

| Argument | Effect |
|---|---|
| `--run "Folder\Script.ps1"` (also `-run`, `/run`) | Runs the script, given as a path relative to `Scripts` (for an added folder: `ext:<id>\Script.ps1`). Used by the jump list. |
| `--preset "<name>"` (with `--run`) | Runs it with this parameter preset. |
| `--run "…" --headless` | Runs the script unattended, like a schedule: hidden, no dialogs, result in the history and a notification; skipped (with a notification) when a normal run would ask something. |
| `--scheduled <id>` | What a Task Scheduler task starts: the schedule with this id (see [Scheduled runs](#scheduled-runs)). |
| `--history <run id>` | Opens Shelly at this run in History (the click on the notification of a scheduled run). |
| `--minimized` (also `-minimized`, `/minimized`, `--tray`) | Starts hidden in the tray. Used by autostart. |
| `--perf` | Writes start-up timings to `%TEMP%\Shelly\perf.log`. |

### Importing a folder (scripts with their own files)

A script that reads its own config, data or modules needs those files next to it. *Import folder…* (the `>` commands,
the menu of the Scripts group) copies the whole folder into Scripts (dropping a folder onto the Shelly window connects it as an external folder instead; dropped
files are added as before):

1. Shelly reads the folder in the background.
2. Over **100 MB** or **2000 files** it suggests **Add as external folder** (the scripts run in place, see above) or
   **Import anyway**.
3. A dialog asks for the **name** of the new folder in Scripts (the source folder's name by default; invalid names and
   names already in Scripts are refused) and lists the script files (`.ps1`, `.py`, `.bat`, `.cmd` and custom extensions) with a tick each, all ticked. Unticked
   scripts are copied but not listed. The dialog shows how many files and how much will be copied.
4. **Import** copies in the background into a hidden temporary folder inside Scripts and renames it into place at the
   end, so an error never leaves half a folder.

Not copied: Python environments (`.venv`, `venv`, `env` with `pyvenv.cfg`; create one again with `python -m venv .venv`),
`__pycache__`, `.git`, `node_modules`. Links and junctions are not followed; the depth limit is the same as for external
folders. The folder gets a small `.shelly.json` (where it came from, which scripts are hidden).

Scripts of an imported folder have a small **folder mark** after the name (tooltip: *Script with its own files*) and run
in their own folder. **Delete** asks first and shows that the whole folder goes to the Recycle Bin (name, number of files,
size). When the folder has more than one script, you can choose **Delete whole folder** or **Remove only this script
from the list** (the file stays). Shelly deletes only a folder that it imported, inside Scripts.

Write paths relative to the script, not to the current folder: `Path(__file__).parent / "config.json"` in Python,
`Join-Path $PSScriptRoot 'config.json'` in PowerShell. The SHA-256 check before a run covers the script file only, not
the other files of the folder.

## 3. The main window

**Navigation pane** (left): **Scripts** (Ctrl+1), **Favorites** (Ctrl+2), **History** (Ctrl+3),
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
The **Sort** button at the top right orders the tiles: **Manual** (the default: the order you starred them in),
**By name**, **By last run** (newest first, never-run scripts at the end), **By run count** (most runs first; counted
from the History, so only the runs it still keeps) and **By type (PowerShell, Python, Batch, then custom)**. Ties go by name, then by
the manual order. In the manual order a tile's context menu also has **Move earlier** and **Move later**. The choice
is saved (`favoritesSort`); the tray menu and the jump list keep the manual order.
The **Size** button next to it sets the tile size: **Small**, **Medium** (the default) or **Large**; the icon and the
text grow with the tile. **Ctrl + mouse wheel** over the page also changes the size. The choice is saved (`favoritesSize`).

**History page.** Every run: inline, separate window, administrator, from the tray, from the editor, and re-runs. Each
entry shows the result, start time, duration, engine, mode and parameters. Filter by All runs, Successful, Failed,
Stopped or Separate window, and search. Select a run to see its saved output. **Enter** runs it again with the same
parameters, mode and engine. **Del** deletes the entry. **Clear history** deletes all entries except running ones. Runs
in a separate window keep no output.

**Compatibility** (a section of Settings). Checks Windows, the .NET runtime Shelly runs on, Windows PowerShell 5.1, PowerShell 7 (with
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

- ▶, Enter, double-click and the jump list use the mode you remembered for the script, then its `# Shelly-Mode:`
  line, otherwise **Settings → Run → Default run mode**. Since 1.2.0 that default is **In a separate window**; pick
  **Inline output** there to get the 1.1 behaviour back.
- Context-menu items use their own mode. The second item is the other everyday mode: *Run with inline output* when a
  plain Run opens a window, *Run in a separate window* otherwise.
- Favorites in the tray menu always run in a separate window.
- **F5** in the editor runs inline (the output appears in Shelly's window).

**PowerShell engines.** Each script runs on **Auto**, **5.1** or **7**. In Auto, Shelly picks:

1. PowerShell 7 if the script has `#Requires -Version 6` or higher, or `#Requires -PSEdition Core`.
2. Windows PowerShell 5.1 for `#Requires -PSEdition Desktop`, or for `#Requires -Version` 5.x or lower.
3. PowerShell 7 if the script uses 7-only features: `??`, `??=`, `?.`, `?[]`, `&&`, `||`, the ternary `? :`,
   `clean { }`, `$PSStyle`, 7-only cmdlets or parameters (such as `ForEach-Object -Parallel`), or if it parses only in 7.
4. Windows PowerShell 5.1 if it uses 5.1-only commands (such as `workflow` or `Get-Content -Encoding Byte`), or if it
   parses only in 5.1.
5. Otherwise, the **PowerShell version** setting (Settings → Environment, runtime PowerShell). Its default is *System*: PowerShell 7 if installed, else 5.1.

You can pin an engine per script in **Run settings**. The choice is saved as soon as you change it. When the chosen
engine is missing, the script needs administrator rights, or a required module is missing, Shelly says so before the
run. It offers a way out (run with the other engine, run as administrator, install PowerShell 7) or **Run anyway**.

**Run settings** (⚙, Ctrl+I or Ctrl+Enter, or the context menu) is a compact dialog with:

- the script's icon, name and description;
- **Preset** with **Save as…** and a delete button (see [Parameter presets](#parameter-presets));
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
tracked until the script in it ends: since 1.2.0 the window reports the script's exit code when the script is done
(the window itself stays open), so the history, the row mark and the notification don't wait for you to close it.
Closing the window before the script ends records it as "closed". An elevated window may not be trackable, and then it
is recorded as "started".

**Stop.** ⏹ on the row or tile, the output panel's Stop button, or the context menu. Stop ends the script's whole
process tree. When you exit Shelly while inline runs are active, Shelly asks first and then stops them.

**Output.** The panel shows at most 4 MB or 100,000 lines of a run, then notes that the rest was skipped. The history
keeps up to the **Saved output per run** limit. When the output is longer, the beginning and the last lines are kept.

**Re-run from history.** On the History page, press Enter or click **Run again with the same parameters**. The output
panel's Run again button does the same.

### Parameter presets

A preset is a named set of parameter values for one script (for a Python script: the arguments line), for example
*Full backup* and *Quick check*. In **Run settings**:

- **Preset** fills the form with the preset's values over the script's defaults;
- **Save as…** saves what is in the form now under a name (an existing name asks before it is replaced);
- the delete button removes the selected preset. Schedules that use it are skipped until you pick another preset.

Run a preset straight from the script's context menu, **Run with preset**, in the list and in Favorites. A schedule can
use a preset too. Presets follow the script when it is renamed or moved. When the script was edited and a parameter of
a preset no longer exists, that value is skipped and Shelly says which ones (the status bar, the log, and a note in Run
settings). **Presets are stored as plain text in `launcher-settings.json` (`paramPresets`): don't keep passwords or
tokens in them.**

### Scheduled runs

**Schedule…** in a script's context menu runs it on its own, also when Shelly is closed: **Once** (date and time),
**Daily**, **On days of the week**, **At Windows sign-in** (30 s after sign-in) or **Every N minutes** (1 to 1440).
Pick a preset (or none: the remembered values or the script's defaults), **Hidden, output to History** or **In a
separate window**, and **Enabled**. A script can have several schedules; the menu then lists them with *Add schedule…*.
Scripts on a schedule have a clock after their name (in the list and on the Favorites tile; the tooltip lists the
schedules).

How it works:

- Each schedule is a task in Windows **Task Scheduler**, folder `\Shelly\`, named after this Shelly folder and the
  schedule. It runs as you, only while you are signed in, with normal rights; creating it needs no administrator
  rights. Shelly uses `schtasks.exe` with a task XML file, nothing extra is installed.
- The task starts `Shelly.exe --scheduled <id>`. The schedules in `launcher-settings.json` (`schedules`) are the source
  of truth: the id says which script, preset and mode, so renaming or moving the script needs no task change. If Shelly
  is open, it runs the script itself; if not, a Shelly without a window runs it, writes the result to the history and
  shows the notification.
- **Scripts that run as administrator can't be scheduled** (`#Requires -RunAsAdministrator`, or *As administrator*
  remembered or set by `# Shelly-Mode: admin`): a task with full rights would run without any UAC prompt.
- A scheduled run can't ask you anything, so every check of a normal run is made, and anything that would ask **skips
  the run** instead: findings that are not trusted for this exact version of the script (trust it once with *Don't
  warn again* in a normal run), danger findings (they are confirmed every time), Mark of the Web, a script PowerShell
  could not check, a missing preset, mandatory parameters without a value. A skipped run is a failed history entry
  (exit code -1) with the reason as its output, and a notification. The SHA-256 of the script is read again right
  before the start, as for every run: an edited script goes through the checks again.
- If the same script is still running inline from the last time, the new run is skipped.

**Settings → Schedules** lists all schedules with the next run: switch one on or off, **Run now** (the same checks as
a scheduled run), edit or delete it. At startup Shelly compares the list with Task Scheduler in the background: a
schedule whose task is missing, or whose task starts a Shelly.exe in another folder (the folder was moved), is marked
with **Repair**; tasks of this Shelly without a schedule are offered for deletion. Deleting a script also deletes its
schedules and tasks.

**Command line.** `Shelly.exe --run "<path in Scripts>" [--preset "<name>"]` runs a script like a click in the list
(the running Shelly takes it over); add `--headless` to run it unattended like a schedule: hidden, no dialogs, result in
the history.

### Python scripts (.py)

Shelly lists `.py` files (since 1.1.0) next to `.ps1` ones, in Scripts and in added folders. A Python row has the **PY** mark (red
when no Python 3 was found; **venv** when it has a virtual environment) and a code icon.

- **Interpreter.** Settings → Environment, *Runtime*: *Python*. One line shows what is used: `Python 3.12 · auto-detect`,
  `Python 3.13 · custom path`, *Python not found* or *Custom path unavailable*; the full path is in its tooltip.
  *Choose…* picks your own `python.exe`, *Reset* (greyed out when already on auto-detect) clears it. Auto-detect tries the
  `py` launcher (`py -3`), `python.exe` on PATH (the Microsoft Store placeholder is skipped unless it starts Python),
  then `%LOCALAPPDATA%\Programs\Python\Python3*` and `Program Files\Python3*`. The search runs in the background;
  *Check again* in Compatibility searches once more (for example after installing Python).
- **Virtual environments.** With *Use .venv next to the script* on (the default), Shelly looks for `.venv`, `venv` or
  `env` (a folder with `pyvenv.cfg`) in the script's folder, then in each parent up to the root of Scripts or of the
  added folder, never above. The nearest one wins. Order: the venv, then your path, then auto-detect. A run from a venv
  gets `VIRTUAL_ENV`, the venv's `Scripts` folder first on PATH and no `PYTHONHOME`, in the Shelly panel and in separate
  window and administrator runs alike. The row shows the **venv** mark; the output header shows `.venv · Python 3.12`.
  Links and junctions are not followed. A venv without `Scripts\python.exe` stops the run with a message (it never
  falls back to another Python): create it again with `python -m venv .venv`.
- **How it runs.** `python -u script.py` in the script's folder with `PYTHONUTF8=1` and `PYTHONIOENCODING=utf-8`: the
  output arrives line by line and Cyrillic text is kept. The header of the output shows the interpreter (`Python 3.x` or `.venv · Python 3.x`); the footer shows the
  exit code. Stop ends the whole process tree. Separate window and administrator runs open a console window (through
  PowerShell, which keeps it open and checks the SHA-256 again after the UAC prompt).
- **Arguments.** Run settings has one *Arguments* box instead of the parameter form. The text is split like a command
  line and passed in `sys.argv`; put values with spaces in double quotes. Remember keeps it like any other run settings.
- **No Python.** The run stops with a dialog (Open Settings, Download Python). Shelly does not crash.
- **Not for Python.** The safety check (it reads PowerShell), the engine choice and *Build .exe* (the menu item is greyed
  out with a tooltip). Mark of the Web still blocks downloaded `.py` files until you unblock them.
- **Hidden files.** Names starting with `_` (helper modules, `__init__.py`), `__pycache__`, `site-packages`,
  `node_modules` and virtual environments (folders with `pyvenv.cfg`) are not listed.
- **New script.** End the name with `.py` (for example `Tools\report.py`) to create a Python script.

### Batch files (.bat, .cmd)

Since 1.3.0 Shelly lists and runs `.bat` and `.cmd` files in Scripts and in the added folders. There is nothing to set
up: `cmd.exe` is part of Windows, so Settings → Environment has no section for them. The row mark is **BAT** or **CMD**
(red when the file is saved as UTF-8 with a BOM: cmd.exe would fail on the first line).

- **How it runs.** `%ComSpec% /d /s /c "call "script.bat" args"` in the script's folder (`/d`: no AutoRun from the
  registry). The exit code is the one of `exit /b N` or the last `errorlevel`. Like other scripts it runs in a separate
  window by default; the window stays open (through the PowerShell host, as for Python, which also checks SHA-256 after
  UAC for administrator runs).
- **Non-English text.** A file saved as UTF-8 (no BOM) gets `chcp 65001` first and its output is read as UTF-8. A file in
  the console code page runs without chcp. Inline output is decoded line by line: a line that is valid UTF-8 is read as
  UTF-8, anything else in the OEM code page, so programs that write OEM (`dir`, `ping`, `ipconfig`) read correctly too.
  The editor opens such files in OEM and saves batch files as UTF-8 without a BOM.
- **Arguments.** One *Arguments* box (as for Python), passed as `%1`, `%2`…; quote values with spaces. The characters
  `"` `%` `^` inside a value can't be passed (cmd.exe expands them) and are refused. Presets, *Remember*, history,
  schedules, notifications and favorites work as for other scripts.
- **pause and set /p.** There is no keyboard input in the output panel: `pause` returns at once and `set /p` gets an
  empty line. Run scripts that wait for input in a window.
- **Safety check.** A basic one: known dangerous commands (`del /s`, `rd /s`, `format`, `reg delete`, `bcdedit`,
  `vssadmin`, `cipher /w`, downloads with `curl`/`bitsadmin`/`certutil` and running what was downloaded…) with the same
  severities as for PowerShell. It follows `&`, `|`, brackets, `^` line continuation and `if`/`for … do`/`call`/`start`,
  but does not expand variables (`%VAR%`), so it can miss things.
- **Not available:** the parameter form, engine choice, *Build .exe*.
- **New script.** End the name with `.bat` or `.cmd`: the file starts with `@echo off`.

### Custom interpreters

Settings → Environment → *Runtime*: *Custom interpreters*. Give any extension to a program of your choice, for example
`.js` to `node`, `.rb` to `ruby`, `.lua` to `lua`.

- **Fields.** *Name* (also the row mark), *Extensions* (`.js .mjs`), *Program* (a full path or a name on PATH,
  *Browse…*), *Arguments* (default `{script} {args}`; `{script}` is required, `{dir}` is the script's folder; paths need
  no quotes), *Run in the script's folder* (otherwise in the program's folder).
- **Checked on save.** `.ps1 .py .bat .cmd .exe .com .lnk .msi …` can't be taken, an extension belongs to one
  interpreter, the program must exist.
- **Running.** Every argument is quoted again by the Windows rules (CommandLineToArgvW), so the *Arguments* box can't
  start another command. A `.bat`/`.cmd` program goes through cmd.exe with the batch rules. Output is decoded as for batch
  files (UTF-8, else OEM).
- **Works:** arguments, presets, history, schedules, notifications, favorites, folder import, drag and drop.
- **Not available:** a safety check (the row and the editor say so), auto-detection of the program, the parameter form,
  *Build .exe*. When the interpreter is deleted or its program is gone, the mark turns red and a run offers to open
  Settings.

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
- Code points are listed in Microsoft's "Segoe Fluent Icons font" documentation.
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
- **Nested code** in `cmd /c`, `powershell -Command` (the quoted text, or every word after `-Command`), `Start-Process`,
  `Invoke-Command`, `Start-Job` and script blocks is checked too. Code nested deeper than 3 levels (`cmd /c cmd /c …`)
  can't be read and is reported as a warning.
- **.NET and native tools** are covered: `[IO.File]::Delete`, `[IO.Directory]::Delete`, the VisualBasic `FileSystem`,
  `[Microsoft.Win32.Registry]`, `[Diagnostics.Process]::Start`, `Invoke-CimMethod` / WMI (`Win32_ShadowCopy`,
  `Win32Shutdown`), `vssadmin`, `wbadmin`, `bcdedit`, `diskpart`, `format`, `cipher /w`, `takeown` / `icacls` on system
  paths, `reg delete`, `schtasks`, `sc delete`, `wevtutil cl`, `Set-MpPreference`, `netsh advfirewall … off`, `mshta` / `regsvr32` / `msiexec` with an http(s)/ftp address and `certutil -urlcache`
  (download and run), and Run keys of the registry (autostart).

Until PowerShell reports (a second or two; results are cached), Shelly's simpler built-in scanner stands in and the
row shows **checking**. A script is never shown as "nothing found" before PowerShell's parser has checked it. If no
PowerShell can check it, the row shows **not verified** and Shelly asks before running it. Before a run Shelly waits up
to 60 s for the check (scheduled runs too).

### Severities

| Level | Mark | What happens | Examples |
|---|---|---|---|
| **Danger** (red) | red shield | Asks before **every** run. Can't be trusted. | Deleting in system locations, drive roots, user profiles or HKLM; disk format/wipe/partition; `bcdedit`; shutdown/restart; `reg delete` in HKLM; execution policy Unrestricted/Bypass for good; weakening Defender; turning off the firewall; deleting services, shadow copies, backups or event logs; changing accounts or the Administrators group; ACL/ownership of system files; scheduled tasks as SYSTEM or with highest rights; **code that is built at run time** (see below); **downloading and running code** (also through `mshta`, `regsvr32`, `msiexec` or `certutil`) |
| **Warning** (amber) | amber shield | Asks before a run; "don't warn again" is allowed. | Deleting with `-Recurse` outside temp folders; deleting through .NET recursively; `Start-Process -Verb RunAs`; HKLM writes; scheduled tasks; compiling C# with `Add-Type` from text in the script; disabling a network adapter; session-only execution policy; adding a program to startup (a Run key); commands nested too deeply to read; a check that failed midway (**analysis error**) |
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

Activation and piracy tools are deliberately not included.

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
- **Administrator** runs always ask, even when nothing was found, the script is trusted or warnings are off.
- **Not verified** (no PowerShell could check the script): Shelly asks before running and shows the built-in scanner's
  findings.

**Turning warnings off.** Settings → Safety → **Warn before running risky scripts**, or `>` in the omnibox (asks
first). With warnings off, warnings are skipped; **danger findings, downloaded
scripts and administrator runs are still checked on every run**. The marks stay in the list.

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
- `.ps1` files are always saved as **UTF-8 with BOM**; other scripts as UTF-8 without a BOM. A file read in another
  encoding shows a note that it will be converted on save. A batch file that is not UTF-8 opens in the OEM code page.
- PowerShell highlighting, completion and checks are for `.ps1` only. Other scripts get simple highlighting by their
  extension in the light theme; the status bar says when a script is not checked (Python, custom interpreters) or
  gets the basic check (batch files).
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
| Compatibility check | Opens Settings at the Compatibility section |
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
- **Notifications.** When a run finishes, Windows shows a notification (a tray balloon, which Windows 10 and 11 show as
  a toast) with the result, the script, the exit code and the duration. Click it to open the run in History. Settings →
  Run → **Notifications**: *Off*, *Only errors* or *Always* (default); **Only if longer than**: any length, 10, 20
  (default), 30 or 60 seconds (`toastAfterSec` in `launcher-settings.json` takes any value from 0 to 3600). An inline
  run doesn't notify while Shelly's main window is active, its output is on screen. Scheduled runs notify by the level
  alone, whatever their length. Errors are a non-zero exit code, a run that could not start, or a skipped scheduled
  run; a window you close yourself and a run you stop are not.
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

To install it, use **Install PowerShell 7…** (Settings → Compatibility, the info bar, `>` commands,
or the dialog shown when a script needs 7):

- **With winget**, if it's available: a separate window runs `winget install --id Microsoft.PowerShell --source winget
  --accept-package-agreements --accept-source-agreements`.
- **Without winget**: **Download and run installer** gets the latest MSI (x64 or ARM64) from the PowerShell GitHub
  releases and starts it. **Open download page** opens the release page in your browser.

Shelly detects the new installation by itself, with no restart needed. **Check again** in Settings → Compatibility
re-runs the search.

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
| Run | Default run mode: In a separate window / Inline output | In a separate window |
| | Notifications: Off / Only errors / Always | Always |
| | Only if longer than: any length / 10 / 20 / 30 / 60 s | 20 s |
| | Favorites in the taskbar jump list | on |
| Schedules | Scheduled scripts: on/off, Run now, Edit, Delete, Repair; leftover tasks: Delete them | none |
| Environment | Runtime: PowerShell / Python / Custom interpreters (which settings are shown); batch files need no setup | PowerShell |
| | PowerShell version (used by Auto): System / PowerShell 5.1 / PowerShell 7+ (System: 7 if installed, else 5.1) | System |
| | Python: interpreter (Choose…, Reset), Use .venv next to the script | auto-detect, on |
| | Custom interpreters: Add, Edit, Delete (up to 20) | none |
| Compatibility | Status of Windows (10 and older), .NET, PowerShell 5.1 and 7, policy, compiler, hotkey, tray; Check again, Install PowerShell 7 | |
| Safety | Warn before running risky scripts | on |
| | Trusted scripts (Revoke, Clear all) | |
| | Trusted sources: built-in entries (on/off) and your own (Add, Remove) | built-ins on |
| History | Keep runs: 50 / 100 / 200 | 100 |
| | Saved output per run: 256 KB / 1 MB / 5 MB / 20 MB | 1 MB |
| Folders and files | Scripts folder, additional folders (connected external folders) (Add folder…, Open, Remove), build folder, settings file (Open / Show) | none added |
| Updates | Check for updates at startup | on |
| About | Program name and version (link to the GitHub repository), author and copyright (`Created by grvelvet © 2026 • All rights reserved`) | |

The Scripts page also remembers the sort (`listSort`, `listSortDesc`), the view (`listView`) and the collapsed groups
(`collapsedGroups`). The filters are not saved.

`launcher-settings.json` is plain JSON. Shelly saves it itself; close Shelly before editing it by hand. Unknown keys are kept.
1.2.0 adds `defaultMode` (`window` | `inline`), `notify` (`off` | `errors` | `always`; a file without it keeps the old
`toastOnFinish` switch), `paramPresets` and `schedules`; a 1.1 file loads with these defaults. 1.3.0 adds
`customInterpreters` (`id`, `name`, `ext`, `program`, `args`, `scriptDir`); a file without it has no custom interpreters.

## 15. Troubleshooting and FAQ

**"Scripts are disabled on this system" / execution policy.** Shelly runs scripts with `-ExecutionPolicy Bypass`, so
the local policy doesn't matter. A policy set by **Group Policy** can't be bypassed: if it is Restricted or blocks
unsigned scripts, Windows blocks them. Settings → Compatibility shows the policy.

**A script doesn't run: "Downloaded from the internet".** Windows marked the file as downloaded. Read it, then click
**Unblock** in the dialog that opens when you run it (see [section 6](#6-safety-checks)).

**Windows SmartScreen says "Windows protected your PC" / unknown publisher.** `Shelly.exe` isn't code-signed. Check
the SHA-256 of the zip against `SHA256SUMS.txt` from the release (`Get-FileHash .\shelly-1.1.2.zip`), then click
**More info → Run anyway**.

**Shelly doesn't start: ".NET is missing".** Install the .NET 10 Desktop Runtime (x64), or use the self-contained build.
See [section 1](#1-install-and-requirements).

**PowerShell 7 from the Microsoft Store isn't found.** Shelly starts Store installs through the `pwsh.exe` app
execution alias. Make sure the alias is on (Windows Settings → Apps → Advanced app settings → App execution aliases),
then click **Check again** in Settings → Compatibility.

**A script waits for input and hangs, or `Read-Host` fails.** Inline runs have no console input. Use **Run in separate
window**, or remember that mode in Run settings.

**Garbled non-English text in Windows PowerShell 5.1.** Save the script as UTF-8 with BOM, for example by opening and
saving it in the Shelly editor.

**Wrong engine.** The engine mark's tooltip says why Auto picked it. Pin an engine in Run settings, or add `#Requires`
to the script.

**"Build .exe" is unavailable.** The C# compiler of .NET Framework 4 wasn't found. See Settings → Compatibility.

**Slow start.** Run `Shelly.exe --perf` (or set `SHELLY_PERF=1`, or add `"perfLog": true` to `launcher-settings.json`)
and read `%TEMP%\Shelly\perf.log`.

**Log.** `%TEMP%\Shelly\shelly.log` (rotated at 512 KB). It is also linked from Settings → About. Failed script runs are logged there too: the script name, the exit code and the error text (a non-zero exit code or anything written to the error stream; for runs in a separate window, only the exit code).

## 16. Privacy

Shelly has no telemetry, analytics or accounts. It contacts the network only:

- for the update check: one request to `api.github.com` when the update check is on, or when you click **Check now**;
- when you choose **Install PowerShell 7**: winget, or the PowerShell GitHub releases for the MSI;
- when you click a link (GitHub, download pages), which opens your browser.

Your scripts, settings, history and logs stay on your PC. Your scripts themselves can, of course, do anything their code
does.
