# Changelog

All notable changes to Shelly are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Versions 0.9.x were the PowerShell + WPF edition; 1.0.0 and later are the .NET edition.

## [Unreleased]

## [1.4.3] - 2026-10-29

### Changed

- **Drag & Drop external folders (`MainWindow`, `ScriptActions`, `SettingsPage`):** dropping a folder anywhere onto the main Shelly window now connects it directly as an external folder (`ExternalFolders`) instead of importing a copy into `Scripts\`.
- **Group header layout (`ExternalScripts`, `ScriptsPage.xaml`):** removed the `(added folder)` suffix from external folder group titles (leaving only the `\uE71B` link icon) and tightened the gap between the group icon and title to `6 px`.
- **Settings → About (`SettingsPage.cs`):** simplified the About card to display `"Shelly <version>"` as a direct link to the GitHub repository above the `Created by grvelvet © 2026 • All rights reserved` caption.
- **Local build script (`build.ps1`, `build.bat`, `Directory.Build.props`):** `build.bat` (`build.ps1 local`) now builds and copies both `Shelly.exe` (framework-dependent) and `Shelly-selfcontained.exe` (self-contained) to the repository root, and adds `NU1900` to `WarningsNotAsErrors` so offline NuGet audit checks do not fail the build.

### Fixed

- **Collapsed output panel strip (`OutputView.cs`, `ScriptsPage.xaml.cs`):** hid the output body container (`OutputView.SetCollapsed`) and `Splitter` when the console is collapsed (`OutputRow.Height = GridLength.Auto`), removing the narrow bordered strip under the header.
- **`ComboBox` in-place expansion, rounding & screen-bounds validation (`Styles.xaml`, `ScrollGutter.cs`, `Motion.cs`, `ThemeManager.cs`):** removed the `Motion.ApplyResources` `ComboBox` override that shadowed `Styles.xaml`, added `ComboPopup` to unfold the dropdown directly over the button with width/height/work-area clamping, and hid the underlying button while open so its border corners never show behind the rounded popup.

## [1.4.2] - 2026-10-26

### Fixed

- **Window run output capture & size limit (`TranscriptCleaner`, `RunManager`, `Unattended`, `Settings`):**
  - Fixed completed window runs being prematurely truncated to the last 64 KB by `TranscriptCleaner.ReadLogFile` before
    applying the user's `historyOutputKB` limit; full transcript logs are now read via `TranscriptCleaner.ReadRawLogFile`.
  - Fixed passing `Settings.HistoryOutputChars` (already multiplied by `1024`) into `TranscriptCleaner.Cap`'s `maxKB`
    parameter, which multiplied by `1024` a second time and overflowed `Int32` (`ArgumentOutOfRangeException`) when
    `historyOutputKB` was set to `20480` (20 MB). Added `Settings.HistoryOutputKBValue` and clamped `maxKB * 1024L` in
    `TranscriptCleaner.Cap`.
- **Custom script syntax highlighting theme switching (`EditorWindow`):** cached built-in AvalonEdit syntax definitions
  (`GetDefinitionByExtension`) now preserve their original light-theme foreground brushes in `OriginalGenericBrushes`
  so switching from Dark theme back to Light theme restores light colors instead of keeping mutated dark brushes.
- **External editor command-line quoting & process launch (`ExternalEditors`, `ShellOps`):** `ExternalEditors.Quote` now
  delegates to `Quoting.NativeArg` (`CommandLineToArgvW` backslash/quote rules), and `ShellOps.ExternalEditor` starts
  resolved editor executables with `UseShellExecute = false`.
- **Built `.exe` wrapper hardening & cleanup (`ExeStub.cs.txt`):** temporary `.ps1` extraction now uses a per-run random
  GUID suffix with `FileMode.CreateNew` and `FileShare.None`, and legacy `#if LAUNCHER` blocks from the pre-.NET edition
  were removed.
- **History store thread-safety & atomic output writes (`HistoryStore`):** synchronized all reads and mutations of
  `Entries` and `removed` (`Add`, `Remove`, `Clear`, `Limit`, `RemoveOrphanOutputs`, `Get`, `LastOf`, `MoveRel`) under
  `lock (Entries)` and switched `SaveOutput` to `AtomicFile.WriteAllText`.
- **`InlineRunner` process release ordering (`Runner.cs`):** cleared `Proc` inside `lock (gate)` and disposed the process
  handle *before* raising `Finished`, eliminating a race condition where listeners waking on `Finished` could observe a
  non-null `Proc` before the `finally` block ran (`HardeningTests.Runner_ends_without_waiting_and_releases_the_process`).

## [1.4.1] - 2026-10-24

### Changed

- **Windows 11 `ComboBox` & `ComboBoxItem` styles (`Styles.xaml`, `SettingsPage.cs`):**
  - Replaced the mixed sharp/rounded popup and item borders of the default theme with explicit Windows 11 / Fluent styles:
    `CornerRadius="4"` closed button, `CornerRadius="8"` transparent flyout popup (`PART_Popup`) with soft shadow and 4 px
    inner padding, and `CornerRadius="4"` items with smooth hover and an accent left selection pill.
  - Reduced `SettingsPage.ComboWidth` from `200` to `148` so compact values (such as *English* / *Русский* or *100*) do not
    leave a wide blank gap on the right, while longer items still expand naturally.
- **Editor selection dialog (`EditorChoiceDialog.cs`):** redesigned into compact two-column Windows 11 cards (title and
  caption on the left, right-aligned `ComboBox` on the right, and the custom `.exe` path/arguments panel expanding inside the
  second card when *Custom editor…* is selected).
- **Settings → Folders and files (`SettingsPage.cs`, `Strings.json`):**
  - Renamed *Scripts folder* (*Папка скриптов*) to **Default folder** (**Папка по умолчанию**) and simplified its subtitle
    to show only the folder path.
  - Renamed *Script folders outside Scripts* (*Папки со скриптами вне Scripts*) to **Additional folders**
    (**Дополнительные папки**) with the subtitle **Connected external folders** (**Подключаемые внешние папки**), and added a
    collapse/expand chevron button next to *Add folder…* (matching *Trusted sources*).

## [1.4.0] - 2026-10-24

### Added

- **External editor selection (`EditorChoiceDialog`, Settings → General → *Script editor*, or the gear button on the editor toolbar):**
  choose the default editor for `Ctrl+E` / *Edit* (*Built-in Shelly editor* or *External editor*) and the external editor
  program (*Automatic*, *VS Code*, *Notepad++*, *PowerShell ISE*, *Notepad*, or *Custom program…* with `{file}` and `{line}`
  placeholders). In automatic mode Shelly detects installed VS Code and Notepad++ and passes the current line (`-g file:line`
  or `-n<line>`), or falls back by script type (`.ps1` → PowerShell ISE / Notepad, `.py` → IDLE / Notepad, `.bat`/`.cmd` and
  custom → Notepad). `.py`, `.bat`, `.cmd` and custom scripts are never opened in `powershell_ise.exe`.
- **Console output capture for runs in a separate window (`RunModes.Window` and `RunModes.Admin`):** scripts launched in an
  external console window now record their console output to a temporary log file (`run-<id>.log`) via `Start-Transcript`,
  stream it live to the output panel while the window is open, strip the PowerShell transcript header/footer via
  `TranscriptCleaner.Clean`, and save the output in `HistoryEntry.Output`. Interactive console prompts (`Read-Host`, `pause`,
  `set /p`, `input()`) continue to work normally in the console window.
- **Built-in linter (`ScriptLinter` in `Shelly.Core` and live diagnostics in `EditorWindow`):**
  - **PowerShell (`.ps1`):** AST parse errors, safety/compatibility findings, empty `catch {}` (`PS-EMPTY-CATCH`), hardcoded
    passwords/secrets (`PS-SECRET`), common aliases in scripts (`PS-ALIAS`), and global `$ErrorActionPreference = 'SilentlyContinue'`
    (`PS-ERRPREF`).
  - **Python (`.py`):** mixed tabs and spaces (`PY-TAB-SPACE`), bare `except:` (`PY-BARE-EXCEPT`), mutable default arguments
    `def f(x=[])` (`PY-MUTABLE-DEFAULT`), Python 2 `print` statements (`PY-PRINT2`), `== None` / `!= None` (`PY-EQ-NONE`), and
    debounced background `ast.parse` syntax checking (`PY-SYNTAX`) when Python is available.
  - **Batch (`.bat`, `.cmd`):** UTF-8 BOM warning (`BAT-BOM`), `!VAR!` without `setlocal EnableDelayedExpansion`
    (`BAT-DELAYED-EXP`), `goto` to a missing `:label` (`BAT-MISSING-LABEL`), single `%i` in `for` loops (`BAT-FOR-PERCENT`),
    unbalanced parentheses (`BAT-PARENS`), plus `BatchScanner` danger findings.
  - **Custom scripts:** UTF-8 BOM check (`CUSTOM-BOM`) and unmatched quotes (`CUSTOM-QUOTES`).
  - **Editor UI:** red/amber wavy underlines (`LintUnderlineRenderer`) with hover tooltips (`[Code] Message`) and a collapsible
    bottom **Problems** panel (`F8` or clicking the status bar check indicator) that jumps to the selected issue's line and column.
- **Syntax highlighting & code completion for all script types:** embedded `Batch.xshd` (`.bat`/`.cmd`) with light and dark
  theme palettes; improved `PowerShell.xshd` (attributes, scoped variables, here-strings) and Python highlighting (decorators,
  `async`/`await`/`match`/`case`, `f`/`r`/`b` strings, built-ins); dark theme color adaptation (`GenericDarkColors`) for custom
  script extensions (`.js`, `.json`, `.xml`, `.cs`, `.sh`, etc.); keyword/command completion (`Ctrl+Space`) for Python and Batch.
- **Trusted script badge in the main window:** scripts whose current SHA-256 hash is trusted display a green **TRUSTED**
  (**ДОВЕРЕН**) badge in both Card and Compact views with a tooltip and a context-menu action *Remove from trusted*
  (*Убрать из доверенных*).

### Changed

- Build system (`build.ps1`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`) with output in `artifacts/`.
- In the secret About window (triple-click on the logo), removed the bottom *Close* button, leaving only the title-bar `✕`
  button and the *Buy me a coffee ☕* link.

### Removed

- Removed the **Compatibility** section and the **Trusted scripts** table from Settings (trusted scripts are now marked directly
  in the main script list and can be untrusted from their context menu).

## [1.3.1] - 2026-10-24

Maintenance release: technical debt and dead code. Running scripts works as in 1.3.0, except as listed under *Changed*.

### Changed

- Before a run Shelly waits up to **60 s** for PowerShell's check of a script everywhere (it was 30 s for runs from the
  window and *Build .exe*, 60 s for scheduled runs). A slow first check no longer stops a run that a schedule would
  have allowed.
- The checks before a run (mode, safety, Mark of the Web, compatibility, Python / venv, batch and custom interpreters,
  presets, the content hash) are one piece of code, `RunPlanner` in Shelly.Core, used by runs from the window and by
  scheduled runs. Scheduled runs still skip changed scripts and ones that need a confirmation; administrator scripts
  still can't be scheduled; an administrator run still checks the hash after the UAC prompt.
- A failed save of the History shows an error in the status bar also after runs in a console window (it was checked
  for inline runs only). Errors that were ignored silently are now written to the log.
- Version numbers of the files follow the product version (1.3.1.0).
- The build treats warnings as errors and checks the code style rules of `.editorconfig`.

### Removed

- Dead code: unused methods, settings and string resources, the pre-1.2 command lines of window runs (without the exit
  code file), renaming of presets in the core (there was no UI for it).
- The setting `toastOnFinish` is no longer a separate property: a 1.1 settings file is still converted to `notify` when
  it is read, and the key is still written (from `notify`) for older versions.
- `assets/shelly.svg` and `assets/shelly-small.svg` moved to `docs/design/` (design sources, not used by the build).

## [1.3.0] - 2026-10-22

### Added

- **Batch files (`.bat`, `.cmd`)**, built in: listed in Scripts and the added folders, run with
  `%ComSpec% /d /s /c "call …"` (exit code from `exit /b` / `errorlevel`), inline or in a window (the window stays
  open), as administrator, from favorites, History, the tray, the jump list, `--run` and schedules. Row mark **BAT** /
  **CMD**. One *Arguments* box with presets (`"` `%` `^` inside a value are refused). Nothing to set up in Settings.
- Non-English output of batch files works inline: a UTF-8 file gets `chcp 65001`, and every output line is decoded as
  UTF-8 when it is valid UTF-8, else in the OEM code page (also for `dir`, `ping` and other OEM programs).
- A basic safety check for batch files with the PowerShell severities (`del /s`, `rd /s`, `format`, `reg delete`,
  `bcdedit`, `vssadmin`, `cipher /w`, download-and-run with `curl` / `bitsadmin` / `certutil`…), following `&`, `|`,
  brackets, `^` continuation, `if`, `for … do`, `call` and `start`.
- **Custom interpreters** (Settings → Environment → *Custom interpreters*): name, extensions, program (*Browse…*),
  arguments template with `{script}`, `{args}`, `{dir}` (default `{script} {args}`), run in the script's folder or the
  program's. Built-in extensions are refused and the program must exist. The name is the row mark. Arguments, presets,
  history, schedules, notifications and favorites work; there is no safety check and no auto-detection (shown as a
  note). Every argument is quoted by the CommandLineToArgvW rules. Stored as `customInterpreters` in
  `launcher-settings.json`; older settings files load unchanged.
- Favorites *By type*: PowerShell, Python, Batch, then custom interpreters.
- New script: a name ending in `.bat` / `.cmd` creates a batch file (`@echo off`, UTF-8 without a BOM).

### Changed

- The editor saves only `.ps1` with a BOM; batch files that are not UTF-8 open in the OEM code page. PowerShell colors,
  completion and checks are for `.ps1` only.
- *Add scripts…* and drag and drop take `.bat`, `.cmd` and the custom interpreters' extensions.

## [1.2.0] - 2026-10-22

### Added

- **Schedules.** *Schedule…* in a script's context menu: *Once*, *Daily*, *On days of the week*, *At Windows sign-in*
  or *Every N minutes*, with a preset, hidden (output to History) or in a separate window, on/off. Each schedule is a
  task in Windows Task Scheduler (`\Shelly\<copy>-<id>`), created with `schtasks.exe` and a task XML: per user, only
  while signed in, normal rights, no administrator rights needed, nothing extra installed. The task starts
  `Shelly.exe --scheduled <id>`; the schedules in `launcher-settings.json` (`schedules`) are the source of truth, so
  renames and moves of the script need no task change. A running Shelly takes the run over; otherwise a Shelly without
  a window runs it, writes the history and shows the notification. Settings > Schedules lists all schedules (next run,
  on/off, Run now, Edit, Delete); at startup they are checked against Task Scheduler in the background: missing or
  outdated tasks get *Repair*, leftover tasks of this copy are offered for deletion. Scheduled scripts have a clock
  after their name in the list and on Favorites tiles. Deleting a script deletes its schedules and tasks.
- Scheduled runs never ask: everything that would ask before a normal run (untrusted findings for this exact SHA-256,
  danger findings, Mark of the Web, an unverified script, a missing preset, mandatory parameters without a value)
  skips the run, with a failed history entry (exit code -1, the reason as output) and a notification. Scripts that run
  as administrator can't be scheduled.
- **Parameter presets.** Named sets of parameter values per script (Python: the arguments line): *Preset*,
  *Save as…* and delete in Run settings; *Run with preset* in the context menu of the list and the Favorites; schedules
  can use one. Presets follow renames; values of parameters a script no longer has are skipped with a note. Stored as
  plain text in `launcher-settings.json` (`paramPresets`): not for passwords.
- **Command line:** `--run <script> [--preset <name>] [--headless]`, `--scheduled <id>`, `--history <run id>`. A second
  start hands them to the running Shelly of the folder (named pipe), as before for `--run`.
- History entries show the preset they ran with; the origin *Schedule*.

### Changed

- **Default run mode is a separate window.** Settings > Run > *Default run mode*: *In a separate window* (default) or
  *Inline output*. A remembered mode and `# Shelly-Mode:` still win; menu items, re-runs and the tray keep their own
  mode; Save and Run in the editor stays inline. The context menu's second item is the other mode.
- **Window runs report their result.** The window writes the script's exit code when the script ends (the window stays
  open), so the history, the last-run mark and the notification get *Done* / *Error* with the code at once instead of
  waiting for the window to be closed. Closing the window early is still recorded as closed.
- **Notifications** (tray balloon, a native toast on Windows 10/11; no extra runtime): Settings > Run > *Notifications*
  *Off* / *Only errors* / *Always* replaces the on/off switch, and *Notify after* is now *Only if longer than* (*Any
  length*, 10, 20, 30, 60 s). Window runs and scheduled runs notify too. The text has the result, the script, the exit
  code and the duration; a click opens the run in History. An inline run does not notify while the main window is
  active. A 1.1 settings file keeps its choice (`toastOnFinish` off → *Off*).
- The history file is merged on save, so runs written by a scheduled Shelly while this one was open are not lost.


## [1.1.2] - 2026-10-21

### Added

- **Favorites sort.** A Sort button in the Favorites header: *Manual* (default, the order they were starred in, as
  before), *By name*, *By last run*, *By run count* (from the History) and *By type* (PowerShell, then Python). Ties go
  by name (culture-aware, so Cyrillic sorts correctly), then by the manual order. In the manual order the tile menu has
  *Move earlier* / *Move later*. Saved as `favoritesSort`; the tray menu and the jump list keep the manual order.
- **Favorites tile size.** A Size button next to Sort: *Small* (200 x 112), *Medium* (248 x 132, the default and the
  size before 1.1.2) and *Large* (312 x 164); the icon and the text scale with the tile. Ctrl + mouse wheel over the
  page steps the size. Saved as `favoritesSize` (an unknown value means Medium).
- **Support the project.** Settings > About has a *Support the project ☕* link (Boosty) next to the GitHub link; also in
  the README and `.github/FUNDING.yml`. No pop-ups or reminders.
- About hides a small secret.

### Changed

- Settings > Environment: the **Shell** list is now **Runtime** ("Choose which environment to configure (PowerShell or
  Python)"); **Default engine for Auto** is now **PowerShell version** ("Used for automatic selection or when no version
  is set explicitly") with the items *System* (was *Auto*), *PowerShell 5.1* and *PowerShell 7+*. Only the labels
  changed: `engineDefault` still stores `auto`, `5.1` or `7`. The per-script engine choice still says *Auto*.

### Fixed

- Drop-down widths: every drop-down in Settings (and the History filter) has the same minimum width (200 px), sized
  for the longest English and Russian item, so the boxes line up and no longer change width with the selection
  (before: 120 to 220 px, and Theme grew past its 150 px for *Use system setting*). In the run settings window a text
  that is longer than its box (for example *Интерпретатор: .venv · Python 3.12*) ends with an ellipsis instead of being
  cut off; the open list and the tooltip show it in full.
- Lists no longer run under the scroll bar. The WPF Fluent ScrollViewer draws its vertical bar over the right edge of
  the content, so the border of the selected script card, the keyboard focus ring and the right edge of the rightmost
  items sat under the bar (and its wider hover track). The script list, the search suggestions, History, Favorites, the
  output panel, the menus and the scrolling lists in dialogs now keep a right gutter (the bar's overlap + 4 px) while
  the bar is shown. The keyboard focus ring of a script card is drawn inside the card (its -1 px margin was cut off at
  the left edge of the list).
- Spacing: one 4 px scale for gaps. The first group header is 8 px from its first card like the others (was 6);
  History rows have 16 px padding on both sides (right was 12); Settings has 16 px from the page title to the first
  section like the other pages (was 32); dialog gaps of 10 / 6 px are now 12 / 8 px, the safety findings card no longer
  doubles its padding (22 px at the top and bottom, 20 between findings; now 12), and the "Don't warn again" check box
  has the same 16 px gap as "I understand" (was 10).

### Removed

- The **Script icons** row in Settings > Folders and files (it only explained the context menu). **Change icon…** in a
  script's context menu works as before, and `scriptIcons` in the settings file is kept.

## [1.1.1] - 2026-10-20

### Added

- **Python virtual environments.** A `.venv`, `venv` or `env` folder (with `pyvenv.cfg`) in the script's folder or a parent,
  up to the root of Scripts or of the added folder, runs the script; the nearest one wins. Order: the venv, then the path
  from Settings, then auto-detect. The run gets `VIRTUAL_ENV`, the venv's `Scripts` folder first on PATH and no
  `PYTHONHOME` (in the Shelly panel, a separate window and administrator runs). The row shows a **venv** mark, the output
  header and the console window show the interpreter (`.venv · Python 3.12`). Links and junctions are not followed. A
  venv without `python.exe` stops the run with a message instead of falling back to another Python.
- Setting **Use .venv next to the script** (`useVenv`, on by default; older settings files load as before).
- **Import folder…** (the `>` commands, the menu of the Scripts group, or a folder dropped on the Scripts page): a script
  is copied into Scripts with its own files. A dialog asks for the name (conflicts refused) and which `.ps1` / `.py`
  files to show, with the number of files and the size. Python environments, `__pycache__`, `.git` and `node_modules`
  are left out, links are not followed, the copy goes to a hidden temporary folder and is renamed into place at the end.
  Over 100 MB or 2000 files Shelly suggests adding the folder as an external folder instead. Such scripts carry a folder
  mark; deleting one deletes the whole folder (to the Recycle Bin, after a confirmation with its size) or, with several
  scripts in it, only removes that script from the list. A `.shelly.json` in the folder keeps the hidden scripts.
- **Shell** list in Settings > Environment: *PowerShell* shows the default engine for Auto (as before), *Python* shows the
  Python settings.

### Changed

- Settings section **Engines** is now **Environment** (also in messages that point to it).
- The Python interpreter is one summary line (`Python 3.12 · custom path`, `Python 3.13 · auto-detect`,
  *Python not found*, *Custom path unavailable*; the full path in the tooltip) with two small buttons, **Choose…** and
  **Reset**, instead of the long path box. The search still runs in the background.
- The Auto item of the default engine list is just "Auto" (the explanation moved to its tooltip).
- Compatibility no longer lists "Windows 11: full Windows 11 styling"; Windows 10 and older still get their row.
- *Check again* in Compatibility also searches for Python again.

### Fixed

- Settings could keep showing "Looking for Python…" after the search had finished, until the page was opened again.
- Changing the Python path while a search was running could show the result of the old path.

## [1.1.0] - 2026-10-20

### Added

- **Python scripts.** `.py` files are listed and run next to `.ps1` ones, in Scripts and in added folders, with the same
  output panel, history, exit code, Stop (the whole process tree), separate window and administrator runs, working folder
  (the script's own) and SHA-256 check right before the start (after the UAC prompt too). A Python row carries the **PY**
  mark and a code icon. Scripts run as `python -u script.py` with `PYTHONUTF8=1` and `PYTHONIOENCODING=utf-8`.
- **Python interpreter** in Settings > Engines: a path to `python.exe`, or empty to find it (the `py` launcher, `python.exe`
  on PATH without the Microsoft Store placeholder, `%LOCALAPPDATA%\Programs\Python\Python3*`, `Program Files\Python3*`).
  The version and the path found are shown there and in the compatibility check. When Python is missing, a run stops
  with a dialog (Open Settings, Download Python).
- **Arguments for Python scripts** in Run settings: one text box, split like a command line into `sys.argv`; kept by
  Remember and in the history (re-run uses them).
- **Add scripts…** (the `>` commands and the menu of the Scripts group): a file dialog with the filter
  "Scripts (*.ps1;*.py)" that copies like drag and drop.
- New script: a name ending in `.py` creates a Python script; the editor colors Python and saves it as UTF-8 without BOM.

### Changed

- Drag and drop and the external folder watcher take `.py` files as well as `.ps1`. Rename keeps the extension.
- Not listed: Python modules starting with `_` (such as `__init__.py`), `__pycache__`, `site-packages`, `node_modules`
  and virtual environments (folders with `pyvenv.cfg`).
- *Build .exe* stays PowerShell only: for a `.py` script the menu item is disabled with a tooltip and Ctrl+B explains why.
  The PowerShell safety scan does not read Python; dialogs and the editor say so instead of "nothing found".

## [1.0.15] - 2026-10-18

### Added

- **External script folders.** In Settings > Folders and files you can add any number of folders (up to 20) outside the
  Scripts folder. Their scripts are listed on the Scripts page in a group named after the folder, marked "added folder",
  and run in place: nothing is copied and the working directory is the script's own folder. The analysis, the danger
  review, the SHA-256 check before the start, the Mark of the Web check and administrator runs work as for any other script.
  Favorites, history, trust, run settings, icons and the engine choice are kept per script and cannot clash with the scripts
  in Scripts. Links (junctions, symbolic links) inside an added folder are not followed; at most 1000 scripts and 2000
  folders are read per added folder. Scripts from added folders are not renamed, deleted or built into an `.exe` by Shelly;
  drag and drop still copies into Scripts only.

## [1.0.14] - 2026-10-18

### Added

- **Drag and drop on the Scripts page.** Drop `.ps1` files from Explorer onto the page (the list, the empty state or any other
  part of it) and they are copied into the Scripts folder; a hint is shown while dragging. An existing script is never
  overwritten: an identical file counts as already there, another one with the same name gets ` (2)`, ` (3)`. Other file
  types are skipped with a short message. Nothing is run on drop, and a copy keeps the Mark of the Web, so downloaded scripts
  still need the usual unblock. Not available when Shelly runs as administrator and Explorer does not (Windows blocks it).

### Changed

- **Analysis cache** is keyed by the SHA-256 of the script, not by its file time. It is rebuilt once after the update (the
  scripts are analyzed again in the background).
- **CI.** A new `ci.yml` builds and tests every pull request and every push to `main` (with PowerShell 7 required, so the
  analyzer tests cannot be skipped unnoticed); `release.yml` stops when the tag differs from `Version` in `Directory.Build.props`.
  Analyzer tests without PowerShell are reported as skipped (with the reason) and fail when `CI` is set.

### Fixed

- **Runs.** The process of an inline or window run is released when the run ends; no thread is parked for 3 seconds
  after every run; stderr lines of CLIXML noise (`#< CLIXML`) are filtered before the `! ` mark is added; the end of a
  run that finishes at once is no longer lost (the handler is attached before the start).
- **Update check.** A tag with a huge number (`v99999999999.0`) is ignored instead of raising an error; the first
  matching release asset is used.
- **Editor.** Removed the unused search field behind the compiler warning CS0414 (the search itself is unchanged).

### Security

- **A script is checked as it is when it runs.** Analysis, "don't warn again" and the confirmation belong to the
  SHA-256 of the content; the analyzer reads a private copy of exactly the bytes that were hashed. Shelly reads the file
  again right before the start and, if it changed, checks and asks again. An administrator run also compares the hash
  inside the elevated PowerShell, after the UAC prompt.
- **PowerShell 7 from the MSI.** The address must be a release download of the PowerShell repository on github.com; the
  SHA-256 published by GitHub (when present) and the Microsoft Corporation signature are checked before `msiexec`
  starts. The file is downloaded into a new folder of the user's profile and kept open (no writes) until the installer is done.
- **Update download.** The link is opened only when it is HTTPS on github.com (under the repository), the GitHub
  assets host, aka.ms or microsoft.com.

## [1.0.13] - 2026-10-18

### Changed

- **Compatibility** moved from the navigation pane into Settings (a section after Engines). The *Compatibility check*
  command and the info bar open Settings at that section. It is one compact card: the summary line with *Check again*
  and a chevron, and the checks as short one-line rows below it. The rows are folded when everything is fine and open
  by themselves when something needs attention.
- **Settings → Engines** keeps only the default engine for Auto; the PowerShell 7 and 5.1 status, *Check again* and
  *Install* are in Settings → Compatibility.
- **Language.** On the first start (no saved language) Shelly uses the Windows display language: Russian for ru, English
  for everything else. A saved choice is kept.
- **Tray menu** is narrower: no minimum width, no empty accelerator gap, and a shorter text when there are no favorites.
- **Settings polish.** The hotkey box and its buttons are flush with the right edge like the other rows; the drop-down
  lists on the Settings page have compact items (the theme list is as wide as its control); the *Trusted sources* row
  has a proper chevron icon, the same as the Compatibility card, instead of a small text triangle.

### Added

- **Shelly log** now records failed script runs: the script name, the exit code and the error text (non-zero exit code or
  anything written to the error stream; for runs in a separate window, the exit code only).

### Fixed

- **Safety check: bypasses closed.** `cmd /c` nested more than 3 levels deep is no longer silently ignored: it is a
  warning ("nested too deep"). `powershell` / `pwsh -Command` without quotes checks every word after `-Command`, not
  only the first one (and `pwsh Remove-Item …` without `-Command` too). `mshta`, `regsvr32` and `msiexec` with an
  http(s)/ftp address and `certutil -urlcache` followed by running the file are danger (download and run). Writing a
  Run key (`Set-ItemProperty` / `New-ItemProperty` / `reg add` on `…\CurrentVersion\Run`) is a new warning, *Autostart*.
- **Administrator runs always ask**, as SECURITY.md says: trusting a script or turning off Safety warnings no longer
  skips the question, and the dialog no longer offers "don't warn again" for an administrator run.
- **Run again from the history** no longer pastes the saved parameters into the command as they are. The text from
  `history.json` must have the form Shelly writes (`-Name 'value'`, `-Switch`, `-Flag:$false`, `-List 'a','b'`);
  otherwise it is ignored and the Run settings dialog opens.
- **The analyzer can't hang on a catastrophic regular expression** in `-replace`: the pattern runs with a 100 ms limit and
  the value is just not folded.
- **The analyzer doesn't swallow errors silently.** If the scan fails midway (for example a script that builds an
  enormous string), the script gets an *analysis error* warning instead of looking clean.
- **No data loss on a damaged file.** A broken `history.json` no longer makes Shelly delete the run logs: the file and
  the logs are moved to a `corrupt-…` folder next to them. A broken `launcher-settings.json` is copied to
  `launcher-settings.json.corrupt-…` and is not overwritten at startup. Settings and history are written to a temporary
  file and swapped in one step; the previous version stays as `.bak`.

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
