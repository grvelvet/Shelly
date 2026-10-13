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

## [1.0.10] - 2026-10-11

### Added

- **Scripts page toolbar** (File Explorer style), left of the search box: *New script*, *New folder* | *Sort*,
  *Collapse all / Expand all*, *View*, *Filter*. Icon buttons with tooltips (English and Russian); *Refresh* and
  *Open folder* stay right of the search.
  - **Sort** (Windows 11 menu): Name, Date modified, Last run, Run count; Ascending or Descending. Applies inside each
    group, Favorites stay on top; ties go by name, never-run scripts stay last when sorting by last run. The button is
    tinted with the accent when the order is not Name, ascending. Saved (`listSort`, `listSortDesc`).
  - **Collapse all / Expand all**: one button whose icon shows what it will do, plus **Ctrl+Shift+[** and
    **Ctrl+Shift+]**. Group states are saved as before (`collapsedGroups`).
  - **View**: *Cards* (as before) or *Compact list*: one 40 px line per script (icon, name, the description inline in
    secondary text, status marks, Run and Run settings), 2 px apart, no card border, hover fill and an accent pill on
    the selected line. Same virtualizing list and the same narrow-width rules for the status marks. Saved (`listView`).
  - **Filter** (checkable menu): Ran before, Never run, Needs PowerShell 5.1, Has safety warnings, Run as admin
    (remembered mode), Favorites only. Ran before / Never run widen each other, the rest narrow the list; works together
    with the search; only groups with matches are shown. An active filter puts an accent dot on the button and a
    *Filters: N · Clear* chip under the header; when nothing matches, *No scripts match the filters* with *Clear
    filters*. Filters are for the session and are not saved.
  - In a narrow window the search box shrinks to 200 px first, then Sort, Collapse, View and Filter fold into a **…**
    menu (with submenus), and only then does the search box shrink further and turn into a button.
- **`.SYNOPSIS` as the description**: a script that starts with a comment-based help block shows the first paragraph
  of its `.SYNOPSIS` under the name.
- **Notify after** in Settings → Run: 10, 20, 30 or 60 seconds (was only `toastAfterSec` in the settings file).
- **User guide link** in Settings → About: the local `docs/USER_GUIDE.md` or `USER_GUIDE_RU.md` in the UI language,
  else the copy on GitHub.

### Changed

- Description comments: a `Description:` / `Описание:` prefix is dropped. The *New script* template starts with
  `# What this script does` instead of `# Description: what this script does`.
- Settings written by 0.9.x (`favTilesCollapsed`, `detailsCollapsed`, `detailsOpen`, `historyThisScript`,
  `migrationDeclined`, `launcherHost`) are still read but no longer written; they disappear on the next save.
- The update check is described as it works: about once a day, at most every 20 hours.

### Fixed

- The first-close tray message said *double-click* the icon; a single click opens Shelly.
- Removed unused strings left from earlier versions: the *Build Shelly.exe* family (with its Ctrl+Shift+B tooltip),
  the old *Folders* tooltip, the build engine message and the Run settings icon section.
- Code comments: `Perf` names `launcher-settings.json`; duplicate summaries on the page timers.

## [1.0.9] - 2026-10-11

### Changed

- **Run settings is a compact dialog** in the style of a WinUI ContentDialog, 520 px wide and as tall as its content
  (about 340 px for a script with two parameters, was about 910 px):
  - the header shows the script's own icon (24 px) and its name (20 px semibold), with the description on one line
    (the full text in the tooltip);
  - parameters are a plain two-column grid, label and field, with no card or section title. The default is the
    field's placeholder (`30`, or *set by the script* when the script computes it, otherwise the type), required
    parameters have a red asterisk, and type, default and help are in the tooltip. The *Empty fields use the script's
    defaults* note is gone. With no parameters there is no section;
  - engine and mode are two drop-downs side by side (*PowerShell: Auto (7)*, *Mode: Normal*); why Auto picks an
    engine and the installed versions are in the engine tooltip;
  - the last run is one secondary line with a *Show in history* link, only when there is one; warnings are compact
    lines with an icon;
  - the buttons sit in a shaded band with 24 px padding: *Remember for this script* on the left, *Run* (accent,
    default) and *Cancel* on the right. The separate *Forget* button is gone: unchecking *Remember* and running
    forgets the settings.
- The icon section left the dialog; *Change icon…* and *Reset icon* stay in the script's context menu.

## [1.0.8] - 2026-10-11

### Fixed

- **Navigation pane animation stuttered and showed a frame of its own.** The width and label animations had no start
  value and were started right after the final value was set, so they began at the end and the pane jumped; when the
  pane is open by default from 800 px (1.0.7), opening and closing ran inline, where the pane also got the overlay's
  background and stroke for the length of the animation and the page jumped sideways at the end. Now:
  - inline (window 800 px and wider) the pane stays plain Mica, with no background or border; the page layer slides
    with the pane's edge on the same curve (a render transform), and its margin changes once, at the end of an open
    or the start of a close, so the script list is not laid out on every frame;
  - overlay (narrower windows) the pane floats over the page on an opaque surface (the WinUI acrylic fallback color)
    with a soft shadow and no stroke;
  - the width, the label fade and the page slide all start from what is on screen, so reversing halfway is smooth;
  - resizing the window across 800 px switches modes without animating.
- Pane widths come from one constant in code; the XAML no longer repeats them.

## [1.0.7] - 2026-10-11

The main window follows the WinUI NavigationView layout (compared with Wintoys).

### Changed

- **Layers:** the navigation pane and the title row sit directly on Mica; the page is its own layer (LayerFillColorDefault,
  `#80FFFFFF` light / `#4C3A3A3A` dark) with a 1 px stroke on the top and left and an 8 px top-left corner. The title
  row of the main window is 48 px, so the layer starts 48 px from the top, right of the pane. The status line moved
  inside the layer instead of running under the pane.
- **Navigation pane:** open by default from 800 px window width (was 1008), compact 48 px with the overlay pane below
  that; the open pane is 240 px (was 280). Items keep the WinUI metrics: 36 px tall, 4 px apart, icon at 16 px, label
  at 48 px, the selected item with a subtle fill and the 3 × 16 accent pill; all items, Compatibility and Settings
  too, show their labels when open.
- **Selected script card:** a 1 px accent border on the card, as in Wintoys; the accent bar on the left and the thick
  black focus border are gone.
- **Focus rings only for the keyboard:** rows, icon buttons, toggles, navigation items and color swatches show the
  focus ring when reached with Tab or the arrow keys, not after a mouse click.
- **Card spacing:** 8 px between script cards (was 4), 20 px above a group header and 8 px between the header and its first card.

## [1.0.6] - 2026-10-11

Empty states, script icons, Windows 11 menus, a narrow-window layout, an animated navigation pane and a performance pass.

### Added

- **Empty states** for Favorites, History, a search with no results and an empty Scripts folder: a large icon, a short
  title, one line of explanation and the action that helps (*Go to scripts*, *Clear search*, *New script* /
  *Open folder*). English and Russian.
- **Script icons from the script itself:** a `# Shelly-Icon: E9D2` line in the leading comments (a Segoe Fluent Icons
  code point, private-use range) sets the script's default glyph. The bundled examples use it: a chart for
  *usd_rub*, a person for *Hello with parameters*, a PC for *System summary*. The line is not taken as the description.
- **Icon in Run settings:** a preview with *Change icon…* and *Use default*; the row icon's tooltip points to
  right-click → *Change icon…*.
- **Hidden perf log:** start with `--perf`, set `SHELLY_PERF=1`, or add `"perfLog": true` to `settings.json`, and
  Shelly appends start-up phase timings, the first frame and memory after start to `perf.log` next to `shelly.log`.

### Changed

- **Menus in the Windows 11 style:** context menus, the *Folders* menu and submenus have 8 px corners, the flyout
  background and border, a soft shadow, 36 px items with a 16 px icon column, shortcuts right-aligned in secondary
  text, thin separators and a fade-in.
- **Tray menu** is now the same WPF menu (icons, favorites with their glyphs, the running script), opened at the
  pointer; it takes focus so the keyboard works and it closes on an outside click. If it can't be shown, the previous
  Windows Forms menu is used.
- **Narrow windows** (minimum 500 × 400): below 1008 px the navigation pane stays compact and the menu button opens
  it over the page with a shadow (closed by a click outside, Esc or choosing a page). The search box shrinks with the
  header and below 160 px becomes a search button that opens the box across the header. Script names trim with an
  ellipsis, and the row marks drop out in order: engine, last run, then the safety shield.
- **Navigation pane animation:** 48 ↔ 280 px in 250 ms on the decelerate curve, labels fade, and the pane moves over
  the page during the animation, so the script list is laid out once instead of on every frame. The selection
  indicator slides between items. Off when Windows *Animation effects* is off.

### Performance

- The script list is built in one pass (scripts bucketed by folder) and replaced in a single list update; on a
  synthetic library of 1000 scripts in 50 folders grouping takes about 1.1 ms and a search about 0.3 ms (Linux test
  box, see below). Typing in the search waits 120 ms between rebuilds once there are more than 300 scripts.
- Lists use pixel scrolling and container recycling (Scripts and History).
- No periodic timers while idle: the output pump and the elapsed-time tickers run only while a script runs, and the
  tickers stop while the window is minimized or hidden in the tray.
- The output panel keeps at most 4 MB or 100 000 lines of a run (it already appends in one batch per 100 ms).
- ReadyToRun stays off: it would grow `Shelly.exe` from 1.9 to 3.5 MB (the zip from 0.7 to 1.5 MB), and WPF and the
  rest of the framework are precompiled already. Dynamic PGO is on (the .NET default).
- A benchmark test (`Benchmark_1000_scripts_50_folders`) generates the library above and reports scan, grouping,
  search and parse times.

## [1.0.5] - 2026-10-11

UI polish: Windows 11 title bars, the WinUI dark-theme accent, Fluent motion and quieter script rows.

### Changed

- **Title bars integrated with Mica** in the main window, the editor and every dialog: the content extends into the
  caption, which shows the app icon and the title (12 px, Segoe UI Variable). On Windows 11 22H2+ the minimize /
  maximize / close buttons are still the system ones drawn by Windows (native hover and pressed states, red close,
  Snap Layouts when hovering maximize, dark glyphs in the dark theme). Dragging, double-click to maximize, the system
  menu (right-click, Alt+Space or a click on the icon) and the resize borders work as before. Where Windows can't draw
  its buttons through the window (Windows 10, Windows 11 21H2, High Contrast), Shelly draws matching ones.
- **Accent in the dark theme:** accent fills (primary buttons, toggles, checkboxes, radio buttons, the selection bar)
  use the lighter accent shade with black text, the light theme the darker shade with white text, as in WinUI. Custom
  swatches get the full set of light and dark shades too, and the fill is adjusted if the text on it would fall
  under 4.5:1.
- **Motion:** pages fade in and rise slightly (250 ms, decelerate), the output panel slides up and fades in, menus,
  tooltips and drop-downs fade in, hover fills of cards, rows, navigation items and icon buttons fade in 83 ms. All
  of it follows the Windows *Animation effects* setting and is off when that is off.
- **Script rows:** the *7* / *5.1* pills are gone. An engine mark appears only when it says something: quiet *PS 5.1*
  when the script runs on Windows PowerShell 5.1, *PS 7* / *PS 5.1* when the engine was set by hand, red when the
  engine is not installed; the tooltip explains why. Auto → 7 shows nothing. The engine, the safety shield and the
  last-run mark sit in fixed slots right before the row buttons, so they line up down the list. The engine choice
  stays in the Run settings dialog.

## [1.0.4] - 2026-10-11

### Added

- **Credits:** *Created by nah and Vika*, in Settings → About, in the README and in the properties of `Shelly.exe`
  (company, copyright). LICENSE names the authors.
- **About card:** name and version, the credit line, links to the repository (github.com/grvelvet/shelly) and the
  author's GitHub profile, README, CHANGELOG, LICENSE and the log, and *Check now* for updates (moved here from the
  Updates card, which keeps the automatic check switch).

### Changed

- The repository is set to `grvelvet/shelly`, so the update check and the GitHub link are on.

## [1.0.3] - 2026-10-11

Menu font fix and a spacing pass against the WinUI guidelines.

### Fixed

- **Menus and tooltips of icon buttons showed boxes instead of text** (for example the *Folders* menu in the Scripts
  header). The icon buttons set the symbol font (Segoe Fluent Icons) on the button itself, and a menu or tooltip placed
  on a button inherits its font. The symbol font and glyph size now go only to the button's content, on every icon
  button and toggle (page headers, rows, output panel, editor toolbar, hotkey buttons). The status icons of a row (last
  result, safety) carried their tooltips on the glyph itself; the tooltips moved to a container.
- **Menu icons:** the *Folders* menu and the History menu have icons like the script menus; the symbol font is set on
  the icon only.
- **Selection on group headers:** a selected group header showed the accent bar like a script. Headers now show only
  hover and the keyboard focus ring; the accent bar is for script cards.
- A test checks the sources: the symbol font may only be set on glyph TextBlocks without tooltips or menus, and the
  icon button styles must not set it on the button.

### Changed (spacing, on the 4 px grid)

- **Pages** (Scripts, Favorites, History, Compatibility, Settings): 36 px from the navigation pane and on the right,
  24 px from the top; 16 px between the header and the list. The status bar uses the same 36 px inset and is 32 px high.
- **Script cards:** 16 px inner padding left and right, 64 px high (12 px above and below the two lines), 4 px between
  cards, corner radius 4 like Windows Settings cards; the right edge lines up with the header's right icons.
- **Group headers:** 20 px above (none above the first), 4 px below, so a header belongs to its cards; the folder
  icon is centered on the cards' icon column and the folder name lines up with the script names.
- **Navigation pane:** items 36 px high with the icon 16 px from the edge; the menu button is centered on the item
  icons; 280 px open, 48 px compact.
- **Settings and Compatibility:** section titles 24 px above and 8 px below; card icon column 40 px; content under a
  card's header 12 px below it.
- **Dialogs:** 24 px padding all round, 16 px under the header, cards with 16 × 12 padding, section titles 16 px above and
  8 px below; parameter rows and hints on the same grid.

## [1.0.2] - 2026-10-11

Settings and Run settings polish.

### Fixed

- **Hotkey card:** an extra status line (*Ctrl+Alt+L opens Shelly from anywhere*) repeated the description, and stayed
  in English after switching to Russian. The line now appears only when there is a problem (the combination is taken or
  invalid), in red. *Turn off* and *Default* are icon buttons with tooltips next to the box, shown only when they would
  change something.
- **Accent color:** the swatches took 120 px cells each (the Fluent radio button minimum width), so they lay in a ragged
  grid. *Windows accent* is now its own option above a tidy row of 32 px swatches with 8 px gaps; the selected swatch has
  a ring in the text color, hover and keyboard focus have their own rings. *Windows color settings* sits at the right of
  the header row like the switches and buttons of the other cards.
- **Notification card:** it said *Windows notification*; it is a message from the Shelly tray icon, and now says so.
- **Toggle cards:** the *On* / *Off* label has a fixed width, so the switches line up whatever the text.
- **Run settings dialog:**
  - *Last run* is shown only when the script has been run.
  - Defaults that are expressions (like `(Join-Path $PSScriptRoot 'usd_rub.png')` or `$env:TEMP`) show as
    *default: set by the script*, with the expression in the tooltip; plain values are shown as before.
  - The note under the parameters is shorter, and mentions `*` only when a parameter is mandatory.
  - *Mode* is a vertical list instead of radio buttons wrapping into two rows.
  - The dialog grows with its content and scrolls only when it would not fit on the screen (it scrolled above 640 px).
- **Language switch:** texts that were cached at start now follow a switch too: the hotkey state (also on the
  Compatibility page) and the editor's toolbar tooltips, encoding, position and scan line.

## [1.0.1] - 2026-10-11

Readability fixes for the script list, found on the first Windows run of 1.0.0.

### Fixed

- **Folder groups looked pale.** Group headers (*General*, folders, *Favorites*) used the secondary text color at 13 px
  for the name and the folder icon, and the count pill used a 3.5 % fill that is invisible on the content layer. Now the
  name is SemiBold 14 in the primary text color, the folder icon is primary, the chevron is secondary (12 px), and the
  count pill has a visible fill (8 % black in light, 14 % white in dark) with secondary text (at least 5.6:1 in both themes).
  The engine badges on the rows use the same pill.
- **No separation between the page, groups and rows.** Script rows are now cards (card fill and card stroke, like
  Windows Settings) on the content layer, with a hover fill and the accent pill on the selected row; group headers are
  section titles between them. Nested folders indent the cards under their header.
- **The group tooltip** (a long hint about clicking and keys) is gone; the header already shows the name and the count.
- **The output panel** showed an empty *Output* strip before anything was run. It now appears only when there is a
  run with output to show (an inline run of the selected script, live or finished), as in 0.9.x; runs in a separate
  window don't open it.
- **Tertiary text** (the search placeholders, the last-run mark without a result, the history status icons, the
  parameter hint in Run settings, the editor's line numbers, empty-state icons) uses the secondary text color.
- **Switching the language** now also updates the output panel (title and button tooltips) and the panel's collapse
  button tooltip, which stayed in the previous language.

## [1.0.0] - 2026-10-10

Shelly is now a .NET 10 WPF application. Everything 0.9.8 does is there, in a faster native app with the Windows 11
Fluent theme. The PowerShell edition moved to `legacy/`.

### Added

- **Two builds.** A small framework-dependent `Shelly.exe` (about 2 MB; needs the .NET 10 Desktop Runtime, and
  Windows offers the download if it's missing) and a self-contained `Shelly.exe` (about 70 MB, needs nothing).
- **Fluent theme** of WPF in .NET 10: Mica backdrop on Windows 11, light, dark or system theme, Windows accent or one of nine accent
  colors; accent text keeps 4.5:1 contrast in both themes. The theme follows Windows changes live.
- **Custom icon per script** (`.ico` or `.png`, right-click → *Change icon…*), shown in the list and on the Favorites
  page and used for exe builds. Icons are copied to `assets\script-icons`.
- **New editor** based on AvalonEdit: PowerShell highlighting, line numbers, find, go to line, word wrap, zoom, run
  (F5) and completion of commands, keywords, variables and parameters. Parse errors and warnings in the status bar
  link to their lines.
- **Notification when a long run finishes** (20 s by default, configurable) while Shelly is in the background; click it
  to see the output.
- **Taskbar jump list** with your favorite scripts (`Shelly.exe --run "Folder\Script.ps1"` runs one directly).
- **Update check** against GitHub Releases (once a day, can be turned off): only shows a link to the download.
- **Command line:** `--minimized` (used by autostart), `--run "<script>"`. A second start of Shelly shows the running one.

### Changed

- Scripts are still parsed by PowerShell itself (AST), now in the background with a cache in `%TEMP%\Shelly`, so the
  list opens at once; a built-in parser fills in until the results arrive.
- The global hotkey, tray, autostart and the safety check work as in 0.9.8; the settings file, history and folder
  layout are the same, so 1.0.0 can be dropped into an existing Shelly folder.
- *Shelly runs on* (the `launcherHost` setting) is gone: the app itself no longer needs PowerShell. The setting is kept
  in the file for the legacy edition.

### Removed

- `Launcher.ps1`, `Launcher.bat` and `Build-Exe.bat` from the main folder (they are in `legacy/`).

## [0.9.8] - 2026-10-10

Dark theme contrast pass, aligned with Windows 11 design guidance.

### Fixed

- **Navigation pane:** item labels and icons were drawn in a dim system color and looked disabled; they now use the
  primary text color in both themes. Selected items keep the accent pill and the subtle fill; selected + hover uses the
  tertiary subtle fill like Windows 11.
- **Lists** (the omnibox command list, history) use the primary text color too.

### Changed

- **Dark theme colors** follow the official Windows 11 values: cards and controls use the translucent card fill
  (#0DFFFFFF) and card stroke (#19000000), so they stand out from the content layer; hover and selection are one step
  lighter; tooltips, drop-down lists and the command list use the opaque flyout color (#2C2C2C). The light theme is
  unchanged.
- **Accent as text or icon** (links, ▶ run buttons, the favorite star, the running counter and filters) is lightened in
  dark (darkened in light) just enough for 4.5:1 contrast; text on accent buttons, switches and checkboxes is white or
  black, whichever reads better. With the default Windows accent the light theme looks the same as before.
- Secondary text, engine badges, group counters, the omnibox placeholder, the status bar and tooltips were checked:
  all at least 4.5:1 in both themes. Engine badges are 11 px (were 10); the editor's line numbers are no longer faded.
- **Scripts header:** *New folder* and *New script* moved from right after the page title to the left of the search
  box, as one toolbar (like File Explorer: commands, then search); 24 px after the title.
- **Settings:** the *PowerShell 7* card uses the command-prompt icon, like *Default engine for Auto*.
- **Texts:** Russian *Вкл* / *Выкл* without the abbreviation period (switches, hotkey, safety warnings, word wrap);
  the close-to-tray note reads *When off, …* / *Если выключено, …*.

## [0.9.7] - 2026-10-10

Sizes, spacing, colors and motion aligned with Windows 11 design guidance (Fluent 2 / WinUI 3 metrics).

### Changed

- **Navigation pane:** 280 px open, 48 px icons only; items are 36 px high with 2 px between them. It switches by
  window width like Windows 11 apps: open from 1008 px, icons only from 641 px, and below 641 px it is hidden and the
  hamburger moves to the page header. On narrow windows the hamburger opens the pane over the page; a click outside,
  Esc or choosing a page closes it.
- **Background:** the accent-tinted gradient behind the pane is gone. The window uses the flat Mica fallback color
  (#F3F3F3 light, #202020 dark); the content layer and cards use the standard Windows 11 fills.
- **Less accent:** script, folder and log icons are neutral (text colors). Accent is kept for the selection pill,
  primary buttons, the running state and the favorite star.
- **Settings page** is one card per setting, like Windows Settings: icon, title and a short description on the left,
  the control on the right (it moves below the text when the window is narrow); section headers above the groups,
  4 px between cards, up to 1000 px wide.
  - *Theme* is a drop-down (*Light / Dark / Use system setting*) with a *Windows color settings* link.
  - *How many runs to keep* is a drop-down.
- **Keyboard focus:** one focus ring everywhere (2 px outer and 1 px inner stroke, Windows 11 style); controls no longer
  turn accent when focused.
- **Sizes:** buttons, drop-downs, text boxes and icon buttons are 32 px high; list rows at least 40 px; group headers
  have a 4 px radius; drop-down popups 8 px; tooltips use the Windows 11 padding and wrap at 320 px.
- **Page header:** 28 px title in a 52 px header, aligned with the hamburger button; page margins 24 px.
- **Dialogs:** 24 px padding (18 at the top), primary button first, then *Cancel*, 8 px apart; text buttons are at
  least 130 px wide. In Run settings, *Run* stays an accent ▶ icon button, now first and wider.
- **Motion:** expand/collapse and fades use 167 ms with Windows 11 easing; scroll bars appear in 167 ms and fade
  out 1 s after the pointer leaves.
- **Texts:** sentence case, no trailing periods on labels and notes (English and Russian).

## [0.9.6] - 2026-10-10

Icon buttons instead of long text buttons, and drop-downs for long choices.

### Changed

- **Icon buttons** (Windows 11 subtle style, 32–36 px, a tooltip with the action and shortcut on each) replace text
  buttons:
  - Scripts header: *Refresh* (F5) and *Folders* (a small menu: Scripts folder, build folder) replace the *…* menu.
  - Run settings: *Mode* is three icon toggles (▶ Normal, Separate window, shield As administrator) with the selected
    mode's name next to them; *Run* is an accent ▶ button (tooltip *Run (Enter)*); *Cancel* stays a text button.
  - Output / History viewer: *Stop*, *Run again*, *Copy*, *Save* and a new *Clear* (clears the view only; the history
    keeps the output).
  - Settings: hotkey *Default* / *Turn off*, PowerShell 7 *Check again* / *Install*, trusted scripts *Revoke* (✕) /
    *Revoke all*, *Clear history*, *Build Shelly.exe*; *Scripts* and *Settings* folders are icon + short label; the
    About links (README, CHANGELOG, License, GitHub) are a row of icons.
- **Pickers:** Language and *Default engine for Auto* are drop-downs; Theme is three icon toggles (Windows setting,
  Light, Dark). Accent swatches and the on/off switches are unchanged.
- **Drop-downs** have the Windows 11 look everywhere (rounded field, themed light/dark popup).
- Every icon glyph is checked against the code points that exist in both Segoe Fluent Icons and Segoe MDL2 Assets.

## [0.9.5] - 2026-10-10

The details pane is replaced by a Run settings dialog, favorites get their own page, and the Scripts header works like
File Explorer's.

### Added

- **Run settings dialog** (⚙ on a row or a favorite card, Ctrl+I, Ctrl+Enter, *Run settings…* in the right-click menu).
  It shows the script name, folder and description, the parameters (type and default; checkboxes for switches and bools;
  drop-downs for `ValidateSet`; mandatory values are checked), the engine (Auto (now: X) / PowerShell 7 / Windows
  PowerShell 5.1, with the Auto reason), the mode (Normal / Separate window / As administrator), *Remember these
  settings for this script*, the last run with *Show in history*, and the warnings in a collapsed expander. Esc closes
  it, Enter runs.
- **Remembered run settings:** with *Remember* ticked, the parameters and mode are saved per script (`runPresets` in
  `launcher-settings.json`) and a plain ▶ / Enter uses them. If a mandatory value is missing, Run settings opens.
  They follow renames and moves and are removed with the script.
- **Favorites page** (star in the navigation pane, Ctrl+2) with cards: name, description, last result, ▶ / ⏹ and ⚙.
  Empty state: *Star scripts to see them here*.
- **Omnibox** in the middle of the Scripts header (Ctrl+K or Ctrl+F): filters scripts by name, folder and description;
  `>` lists commands (new script, new folder, Favorites, History, Settings, theme, language, open the Scripts folder,
  Compatibility, refresh, build `Shelly.exe`), picked with the arrows and Enter. Esc clears it.
- **Folders:** a *New folder* button (Ctrl+Shift+N) and a right-click menu on group headers: *New script here*, *New
  folder*, *Rename folder*, *Delete folder* (empty folders are deleted, others go to the Recycle Bin after a
  confirmation), *Open in Explorer*. Names are validated (invalid characters, reserved names, trailing dot or space,
  leading dot, length, existing names). Favorites, engines, remembered settings, last results and history follow a
  renamed folder. Empty folders are shown as groups.
- **Friendly results:** *Success*, *Failed (code N)*, *Stopped*, *Window closed*, *Terminated*, *Crashed (…)*.
  `0xC000013A` (-1073741510, the console window was closed or Ctrl+C) is *Window closed*; access violation, stack
  overflow, buffer overrun, DLL init failure and illegal instruction are named; other large codes are shown in hex.
  The raw code is in the tooltip.

### Changed

- **Scripts header:** *New folder* and *New script* icon buttons on the left, the omnibox centered, *…* (Folders,
  Refresh) on the right. The *+ New script* text button and the ⓘ button are gone.
- **Rows:** ▶ Run (⏹ Stop while running) and ⚙ Run settings on hover or selection; the status mark and engine badge stay.
- **Right-click menu:** Run, Run settings…, Edit, Rename, Favorite, Build .exe, Delete. *With parameters*, *Separate
  window*, *As administrator* and *Engine ▸* moved into Run settings (Ctrl+Shift+Enter still runs in a separate window).
- **Tooltips** are short Windows 11 cards (opaque, rounded, with a shadow, up to about 360 px wide, wrapped) across the
  app. A script's tooltip has three lines: path, engine, last run.
- **Navigation:** the hamburger button is aligned with the item icons; Ctrl+1 / Ctrl+2 / Ctrl+3 = Scripts / Favorites
  / History.

### Removed

- The details pane (and its ⓘ button), the old *With parameters* dialog and the favorite tile row on the Scripts page.
  Favorites are no longer pinned to the top of the list; they keep their folder and a ★.

## [0.9.4] - 2026-10-10

A calmer main window and a shorter Settings page. All features are still there; they moved to the right-click menu,
the details pane or Settings.

### Changed

- **Scripts page header:** title, search, a details (ⓘ) button, *New script* and a *…* menu with only *Folders*
  (Scripts, build) and *Refresh*. The command bar (Edit, Rename, Delete, Favorite, Build, Folders, Details) is gone;
  those commands are in the right-click menu of a script and keep their shortcuts.
- **Script rows:** icon, name and a one-line description; on the right the last-run mark and the engine badge. On hover
  or selection the row shows only ▶ (■ while it runs). The star and the safety badge are no longer in the row; safety
  findings are in the details pane and the warning before a run is unchanged.
- **Right-click menu:** Run, With parameters, Separate window, As administrator, Edit, Rename, Favorite, Build .exe,
  Engine ▸, Delete. *Open in external editor* (Ctrl+Shift+E) and *Show runs* (Ctrl+H) are shortcuts only.
- **Details pane** is hidden by default and opened with the ⓘ button, Ctrl+I or the × on the pane (the choice is
  remembered). It slides in on the right (below the list in narrow windows) and has up to four cards: *About*
  (description, a *Run* button, the engine selector with the Auto reason, file name and folder with the full path in a
  tooltip), *Parameters* (only if the script has any), *Last run* (only if there is one, with a link to the history) and
  *Warnings* (only when there are findings). The File card and the button block are gone.
- **Favorite tiles** no longer have a header row; they are hidden when there are no favorites.
- **Output panel** on the Scripts page is hidden until there is a run to show. A run opens it; otherwise it stays a
  one-line header. Expanding or collapsing it by hand holds until Shelly is closed. The empty "select a run" box is gone.
- **History page:** the run list with search on the left, the output of the selected run on the right with *Copy*,
  *Save* and *Run again* (*Stop* while it runs). The filter and clear buttons are gone: Ctrl+H searches the history for the
  selected script, and *Clear history* is in Settings → Data (also in the right-click menu of a run). Find in the output
  (Ctrl+F, F3) still works.
- **Settings** has five cards instead of eight:
  - *General*: language, Start with Windows (the registry entry is in the tooltip), close button hides to the tray, and
    the global hotkey (record a combination, *Default*, *Turn off*; the preset buttons are gone)
  - *Appearance*: theme and accent
  - *PowerShell*: default engine for Auto scripts, PowerShell 7 status with *Check again* / *Install*
  - *Safety*: warnings on or off, trusted scripts
  - *Data*: keep 50, 100 or 200 runs, *Clear history*, *Scripts folder*, *Settings folder* and *Build Shelly.exe*
    (moved here from the main window)

  Below them, *About* shows the Shelly icon (from `assets\shelly.ico`), the version, the engine Shelly runs on and links
  to README, CHANGELOG, the license and GitHub. Full paths are tooltips now.
- Removed from Settings: *Start minimized* (an existing setting is still honored), the saved output size choices (1 MB per
  run; `historyOutputKB` in `launcher-settings.json` still works) and *Shelly runs on* (`launcherHost` in
  `launcher-settings.json` and `SHELLY_HOST` still work).
- The status bar shows the script count and the hotkey; the Scripts path is its tooltip.
- The language button left the header; the language is in Settings → General.

## [0.9.3] - 2026-10-10

New Windows 11 layout, run history, concurrent runs and a Settings page.

### Added

- **Navigation pane** on the left, in the style of a Windows 11 NavigationView: hamburger button, pages *Scripts*
  (Ctrl+1) and *History* (Ctrl+2), and *Compatibility* and *Settings* (Ctrl+,) at the bottom. The selected page has an
  accent pill. The pane collapses to icons (Ctrl+M, remembered) and does so by itself in narrow windows. Behind it is
  a soft accent-tinted gradient (a Mica-like look in light and dark themes); the content sits on a lighter layer.
- **Command bar** above the list: *New*, *Edit*, *Rename*, *Delete*, *Build .exe* (menu: selected script or
  Shelly.exe), *Open folder* (Scripts, build or Shelly folder) and *More* (refresh, PowerShell 7, safety warnings,
  settings). These replace the old Manage, Build and Folders sections.
- **Favorite tiles** at the top of the Scripts page: name, icon, status and a Run/Stop button for each favorite.
  Double-click a tile to run it; right-click it for the script menu. The tile row can be collapsed and is hidden when
  there are no favorites.
- **Details pane** with rounded cards: description and run actions; engine with a 7 / 5.1 / Auto selector and the
  reason for Auto; parameters with types, defaults and allowed values; last run with a link to it in the history;
  safety and compatibility findings, where each line number opens the editor at that line; and the file (path, size,
  modified, encoding). In wide windows (from 1100 px) it is to the right of the list; in narrower windows it is below
  the list and can be collapsed. Ctrl+I shows or hides it.
- **Run history** (History page) of all runs: inline, separate window, administrator, tray and editor. Each run shows
  its result, time, duration, engine, mode and parameters. You can search, filter (result, separate window or
  administrator, only the selected script), re-run with the same parameters, open or select the script, delete an
  entry or clear the history. The output of inline runs is saved; long output keeps its beginning and the last lines.
  The history is kept in `launcher-history\` (`history.json` plus one `.log` per run, UTF-8 with BOM). A pinned
  *Shelly log* entry collects .exe builds, folder migration and errors.
- **Output viewer**: find (Ctrl+F, F3 / Shift+F3), word wrap, copy, save to .txt. On the Scripts page it is a
  collapsible panel at the bottom that shows the live or selected run; on the History page it is next to the run list.
- **Several scripts can run inline at the same time**, each with its own output, row spinner and Stop button. The
  same script still can't run inline twice at once. The navigation pane shows how many scripts are running.
- **Settings page** with card sections:
  - General: language; start minimized to the tray; close button hides to the tray; start with Windows (HKCU Run)
  - Appearance: theme (Windows setting, light or dark) and accent color (Windows accent or a preset), with *Restart now*
  - Engines: default engine for Auto scripts; the engine Shelly itself runs on
  - Safety: safety warnings on or off; the list of trusted scripts, with *Revoke* and *Revoke all*
  - Global hotkey: record a new combination or pick a preset; it is re-registered at once, and if the combination is
    taken, the previous one stays
  - History: number of runs to keep (20 to 500) and saved output per run (256 KB to 20 MB)
  - Folders and files: Scripts folder, settings file, history folder
  - About: version, engine, Windows build, license and links
- New shortcuts: Ctrl+1, Ctrl+2, Ctrl+, , Ctrl+I, Ctrl+H (runs of the selected script), Ctrl+Shift+Enter (run in a
  separate window); Ctrl+F searches the history on the History page and finds text when the output has focus.
- Tray menu: *History* and *Settings*; it shows the current hotkey.

### Changed

- Script rows are simpler: icon, name, a short description, the status mark and the engine and safety badges, with the
  run buttons on hover or selection. Last-run details, parameters, the file date and findings are in the details pane.
- The run header and footer (`--- name · PowerShell 7 · 12:00 ---`, `--- Exit code 0 · 1.2 s ---`) and the remaining
  fixed English texts are translated.
- Exiting while scripts run asks once for all of them; their output is saved to the history.
- `Launcher.bat` now reads the *Shelly runs on* setting (`launcherHost`) from `launcher-settings.json`, so it starts
  Shelly on the same engine as `Shelly.exe`. `SHELLY_HOST` still overrides it.
- `Shelly.exe` sets `SHELLY_APP` (was not set before) so that *Start with Windows* and *Restart now* use the exe.
  Rebuild `Shelly.exe` after updating.

## [0.9.2] - 2026-10-10

PowerShell 7 from the Microsoft Store.

### Fixed

- With PowerShell 7 installed from the Microsoft Store, Auto scripts that need neither engine (for example
  *Hello with parameters*) could show and run as `5.1`. PowerShell 7 was found only by checking files, and the Store
  package folder (`C:\Program Files\WindowsApps\Microsoft.PowerShell_…`) is protected; when such a check failed,
  including the re-check every time the window became active, Shelly treated PowerShell 7 as missing. Now, when Shelly
  itself runs in PowerShell 7, its own `pwsh.exe` and version are used, and PowerShell 7 can no longer turn "missing"
  while it is running Shelly.
- Scripts on PowerShell 7 from the Store are started through the app execution alias
  `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` (or `…\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe`), not
  the package path, which can fail with "access denied". Exe icons are taken from the real `pwsh.exe`.
- `Launcher.bat`, `Build-Exe.bat`, `Shelly.exe` and script exes also find the Store alias.

### Changed

- The PowerShell 7 row and menu show *(Microsoft Store)* for Store installs. Under Windows PowerShell 5.1, the Store
  package version is read with `Get-AppxPackage` when only the alias is visible.
- The Compatibility window mentions *Default for Auto scripts* when it is not Auto.
- After an upgrade, the first start of a new version clears cached engine results: background parses, temporary parse
  files and the cached `pwsh.ico` / `powershell.ico`. This is tracked by the new `lastVersion` setting. Per-script engine
  choices and the default are kept.

## [0.9.1] - 2026-10-10

PowerShell 7 support.

### Added

- Shelly runs on Windows PowerShell 5.1 or PowerShell 7.2+ on Windows; it no longer restarts itself in 5.1 when started
  from PowerShell 7. `Launcher.bat`, `Build-Exe.bat` and `Shelly.exe` prefer PowerShell 7 when it is installed;
  `SHELLY_HOST` (`auto`, `7`, `5.1`) and the `launcherHost` setting choose the engine.
- Per-script engine: Auto (default), PowerShell 7 or Windows PowerShell 5.1. Auto decides from `#Requires`, PowerShell 7-only
  syntax (`??`, `?.`, ternary, `&&`/`||`, `ForEach-Object -Parallel`, `clean { }`, ...), Windows PowerShell-only commands
  (`Get-WmiObject`, EventLog cmdlets, PSSnapins, workflows, ...), .NET Framework-only APIs (WinForms charts,
  `JavaScriptSerializer`) and which parser accepts the code, else prefers PowerShell 7
  when installed. A global *Default for Auto scripts* setting is also available.
- Engine badge (`7` / `5.1`) in every row with the reason in its tooltip, an *Engine* submenu in the right-click menu and
  *Engine: …* in the *Run* section.
- Parse errors, parameters and the safety scan come from the parser of the script's engine; the other engine parses in
  the background (one hidden process per batch, cached by path and file time).
- Inline, separate-window, administrator, tray and editor F5 runs use the script's engine (`pwsh.exe` is found in PATH,
  Program Files, App Paths and the 7-preview folder). Inline output header shows `[7]` or `[5.1]`.
- Script .exe builds embed the engine and fall back to the other one with a message when it is missing.
- PowerShell 7 block in the *System* section and the Compatibility window: version and path or "not installed",
  *Check again*, *Install PowerShell 7…* (winget in a separate window after confirmation; without winget the MSI from the
  GitHub releases API or the download page), and automatic detection after the install, without restarting Shelly.
- One-time info bar offer when PowerShell 7 is missing and some scripts need it.
- The Compatibility window shows the PowerShell that Shelly runs on, Windows PowerShell 5.1 and PowerShell 7.

### Changed

- Inline runs: the exit code is now the script's `exit N` (or the last native command's), 1 when a terminating error
  stops it, and 0 otherwise, the same on both engines. Previously `exit N` was reported as 1, and a script that only
  wrote non-terminating errors could be reported as failed.
- The built-in editor reports syntax errors for the script's engine (PowerShell 7 syntax isn't flagged in scripts that
  run on 7, and is flagged in scripts set to 5.1).
- ANSI colour codes are removed from inline output, and PowerShell 7 renders inline output as plain text.

### Fixed

- Inline output could end up after the "Exit code" line on .NET (PowerShell 7 host); the run now completes only after
  both output streams are read.
- Delete to the Recycle Bin falls back to the Windows Shell when Microsoft.VisualBasic is not available.

## [0.9.0] - 2026-10-10

First public release of the PowerShell edition.

### Added

- Script list built from the `Scripts` folder: subfolders become collapsible folder groups (with a count badge,
  remembered state and keyboard control), plus Favorites pinned to the top and search by name, folder and description.
- Row actions on hover or selection: run, run with parameters, run in a separate window, run as administrator.
- Run status per script: a spinner with the elapsed time and a Stop button while it runs; afterwards a success, failure
  (with exit code) or stopped mark and "last run hh:mm, took 3.2 s", saved between sessions. Separate-window and
  administrator runs are tracked while their window is open.
- Output panel with a "Running: name · 00:12" header and a progress bar.
- Parameters dialog generated from the script's `param()` block, with types, defaults, `ValidateSet` values and help.
- Built-in editor with PowerShell syntax highlighting, line numbers, find, go to line, syntax errors from the
  Windows PowerShell 5.1 parser, and saving as UTF-8 with BOM.
- Safety scan that warns about dangerous commands before running a script.
- Compatibility checks for Windows, PowerShell, .NET Framework, the C# compiler and the execution policy, plus per-script
  `#Requires` and parse checks.
- Building scripts, and Shelly itself (`Shelly.exe`), into .exe files with the C# compiler built into .NET Framework 4.
- Tray icon with the running script in its tooltip, and a global hotkey (Ctrl+Alt+L).
- Shelly app icon (`assets/shelly.ico`) for the window, the taskbar, the tray and `Shelly.exe`.
- English and Russian UI, switched live; light and dark theme with the Windows accent colour; adaptive navigation pane
  that collapses to an icon rail.
- Version number in one place (`$ShellyVersion`), shown in the Compatibility window, the tray tooltip and the
  `Shelly.exe` file properties.

### Notes

- Pre-release iterations before 0.9.0 were internal and are not listed here.

[Unreleased]: https://github.com/grvelvet/shelly/compare/v1.0.11...HEAD
[1.0.11]: https://github.com/grvelvet/shelly/compare/v1.0.10...v1.0.11
[1.0.10]: https://github.com/grvelvet/shelly/compare/v1.0.9...v1.0.10
[1.0.9]: https://github.com/grvelvet/shelly/compare/v1.0.8...v1.0.9
[1.0.8]: https://github.com/grvelvet/shelly/compare/v1.0.7...v1.0.8
[1.0.7]: https://github.com/grvelvet/shelly/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/grvelvet/shelly/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/grvelvet/shelly/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/grvelvet/shelly/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/grvelvet/shelly/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/grvelvet/shelly/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/grvelvet/shelly/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/grvelvet/shelly/compare/v0.9.8...v1.0.0
[0.9.8]: https://github.com/grvelvet/shelly/compare/v0.9.7...v0.9.8
[0.9.7]: https://github.com/grvelvet/shelly/compare/v0.9.6...v0.9.7
[0.9.6]: https://github.com/grvelvet/shelly/compare/v0.9.5...v0.9.6
[0.9.5]: https://github.com/grvelvet/shelly/compare/v0.9.4...v0.9.5
[0.9.4]: https://github.com/grvelvet/shelly/compare/v0.9.3...v0.9.4
[0.9.3]: https://github.com/grvelvet/shelly/compare/v0.9.2...v0.9.3
[0.9.2]: https://github.com/grvelvet/shelly/compare/v0.9.1...v0.9.2
[0.9.1]: https://github.com/grvelvet/shelly/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/grvelvet/shelly/releases/tag/v0.9.0
