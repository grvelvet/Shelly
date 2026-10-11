<p align="center"><img src="docs/logo.png" width="160" alt="Shelly logo: a coral snail whose shell is a terminal window"></p>

# Shelly: a Windows 11-style launcher for PowerShell scripts

<p align="center">
  <img alt="Version 1.0.11" src="https://img.shields.io/badge/version-1.0.11-ff7a5e">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="Windows PowerShell 5.1 or PowerShell 7" src="https://img.shields.io/badge/PowerShell-5.1%20%7C%207-4b8df0">
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4">
  <a href="https://github.com/grvelvet/shelly/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/grvelvet/shelly"></a>
  <img alt="MIT license" src="https://img.shields.io/badge/license-MIT-green">
</p>

**One window to find, run, edit, check and package your PowerShell scripts.**

Created by nah and Vika · [github.com/grvelvet/shelly](https://github.com/grvelvet/shelly)

Shelly 1.0 is a .NET 10 WPF application with the Windows 11 (Fluent) look. It is portable: copy the folder, start
`Shelly.exe`, and every `.ps1` file in the `Scripts` folder appears in a searchable list. Each script runs on the
engine it needs, Windows PowerShell 5.1 or PowerShell 7. The 0.9.x PowerShell edition is in [`legacy/`](legacy/).
See [CHANGELOG.md](CHANGELOG.md) for what's new.

**User guide:** [English](docs/USER_GUIDE.md) · [Русский](docs/USER_GUIDE_RU.md): install, running scripts,
writing scripts for Shelly, safety checks, shortcuts, troubleshooting.

## Download

Two builds of each release (see [Releases](https://github.com/grvelvet/shelly/releases)):

| File | Size | Needs |
|---|---|---|
| `shelly-<version>.zip` | `Shelly.exe` ≈ 2 MB | the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) |
| `shelly-<version>-selfcontained.zip` | `Shelly.exe` ≈ 70 MB | nothing |

The small build is recommended. If the .NET 10 Desktop Runtime isn't installed, Windows shows a dialog with a
download link on the first start (one ≈ 55 MB installer, shared by all .NET apps; it can also be installed with
`winget install Microsoft.DotNet.DesktopRuntime.10`). The self-contained build carries the runtime inside the exe
and starts a bit slower.

**Verify the download.** Each release has `SHA256SUMS.txt`. In PowerShell, compare its line with:

```powershell
Get-FileHash .\shelly-1.0.11.zip -Algorithm SHA256
```

**SmartScreen.** `Shelly.exe` isn't code-signed, so Windows SmartScreen may say "Windows protected your PC" /
"Unknown publisher" on the first start. After the checksum matches, click **More info → Run anyway**. If you
downloaded the zip with a browser, you can also clear the mark first: file Properties → **Unblock**, or
`Unblock-File .\shelly-1.0.11.zip`, before you unzip.

Unzip anywhere (for example `C:\Tools\Shelly`) and run `Shelly.exe`. There is no installer and nothing is written
outside the folder except the autostart entry (if you turn it on) and a cache in `%TEMP%\Shelly`.

**Upgrading from 0.9.x:** put `Shelly.exe` into your existing Shelly folder (next to `Scripts` and
`launcher-settings.json`). Favorites, engines, presets, trusted scripts, history and the rest are read as they are.

## Screenshots

![Shelly](docs/screenshot.png)

> Screenshot not added yet. Save one as `docs/screenshot.png` (see [docs/README.md](docs/README.md)).

## Features

- **Windows 11 look.** Fluent theme (with the Mica backdrop on Windows 11), light, dark or system theme, the Windows accent or one of nine accent
  colors (accent text is adjusted for 4.5:1 contrast). A navigation pane with **Scripts** (Ctrl+1), **Favorites**
  (Ctrl+2), **History** (Ctrl+3), **Compatibility** and **Settings** (Ctrl+,); it shrinks to icons with Ctrl+M, and on
  narrow windows (under 800 px) it stays as icons and opens over the page. The search box turns into a search button
  when the window gets very narrow. Windows 11 style menus (also in the tray). Segoe Fluent Icons, settings as cards.
- **Scripts page.** A File Explorer-style toolbar left of the **omnibox** (Ctrl+K / Ctrl+F: type to filter by name,
  folder and description; type `>` for commands): *New script*, *New folder* | *Sort* (name, date modified, last run or
  run count, either direction, inside each group), *Collapse / Expand all* (Ctrl+Shift+[ / ]), *View* (cards or a
  compact one-line list) and *Filter* (ran before, never run, needs PowerShell 5.1, has safety warnings, runs as admin,
  favorites only; a *Filters: N · Clear* chip shows when one is on). *Refresh* (F5) and *Open folder* sit right of the
  search; in a narrow window the view buttons fold into a *…* menu. Subfolders are collapsible groups; starred scripts are in a *Favorites* group on top. Hover a row for ▶ (Run)
  and ⚙ (Run settings); while a script runs, the row shows a spinner, the elapsed time and ⏹. Quiet marks in fixed
  columns left of the buttons: *PS 5.1* when a script runs on Windows PowerShell 5.1 or its engine was set by hand
  (nothing for the usual Auto → 7), the safety shield, and the result of the last run.
- **Descriptions.** The line under a script's name comes from its first `#` comment (a `Description:` prefix is
  dropped) or from the `.SYNOPSIS` of a `<# … #>` help block at the top, whichever comes first.
- **Custom icons.** Right-click a script → *Change icon…* to pick an `.ico` or
  `.png`. The icon is shown in the list, on the Favorites page and is used when you build the script to `.exe`. A script
  can also choose its default glyph with a line in its leading comments, a Segoe Fluent Icons code point:

  ```powershell
  # Shelly-Icon: E9D2
  ```
- **Run settings dialog** (⚙, Ctrl+I or Ctrl+Enter), a compact Windows 11 dialog: the script's icon, name and
  description; a field for each parameter of the `param()` block with its default as the placeholder (checkboxes for
  switches and bools, drop-downs for `ValidateSet`, a red asterisk on required ones, type and help in the tooltip); the
  engine (Auto, 7 or 5.1; why Auto picks one is in the tooltip) and the mode (normal, separate window, as
  administrator) side by side; the last run in one line; safety and compatibility warnings with links into the editor;
  *Remember for this script* next to Run and Cancel.
- **Parameters are read exactly.** Shelly asks PowerShell itself to parse each script (AST) in the background and
  caches the result; until then a quick built-in parser fills in.
- **Engines.** Auto picks 7 or 5.1 per script (`#Requires -Version`, `-PSEdition`, modules and commands that exist only
  in one of them); you can pin an engine per script or set the default. PowerShell 7 is found in Program Files, on PATH,
  or as the Microsoft Store app (App Execution Alias). If it's missing, Shelly offers to install it with winget (or the MSI from GitHub).
- **Run several scripts at once.** Inline runs each have their own output, spinner and Stop. Runs in a separate window
  and as administrator (UAC) are tracked too.
- **Safety check (a guardrail, not a sandbox).** Before a run and before building an exe, Shelly reads the script with
  PowerShell's own parser (AST): aliases (all built-in ones and the script's own), shortened parameters, splatting,
  constant variables and string tricks like `& ('Rem'+'ove-Item')` are resolved, and .NET calls and native tools
  (`[IO.File]::Delete`, the registry, WMI/CIM, `vssadmin`, `bcdedit`, `diskpart`, `schtasks`, `wevtutil`…) are covered.
  Findings are **danger** (red), **warning** (amber) or **info**. Code built at run time (`iex`, `& $var`,
  `[scriptblock]::Create`, `-EncodedCommand`, reflection) and **download-and-run one-liners** (`irm … | iex`) are
  danger, with the URL shown. Danger asks before every run (tick *I understand*, 3-second pause) and can't be trusted;
  warnings can be trusted per file version (SHA-256). **Trusted sources**: a built-in list of well-known installers
  (WinUtil, Scoop, Chocolatey, PowerShell, .NET, uv, Deno, Bun, pnpm, Oh My Posh), HTTPS only, each one can be turned
  off, plus your own. **Downloaded from the internet** (Mark of the Web): such scripts are blocked until you unblock
  them. Building an exe from a flagged script needs typing its name or `BUILD`. Administrator runs always ask. A
  one-time disclaimer on the first start says it plainly: scripts run with your rights. See the
  [guide](docs/USER_GUIDE.md#6-safety-checks) and [SECURITY.md](SECURITY.md).
- **Output and history.** Live output with find, copy, save to `.txt`, run again and open the script. The History page
  keeps every run (inline, window, admin, tray, editor) with result, time, duration, engine, mode, parameters and
  output; filter and search it.
- **Editor** (Ctrl+E): PowerShell syntax highlighting, line numbers, find (Ctrl+F), go to line (Ctrl+G), word wrap,
  zoom, run (F5), and completion (Ctrl+Space, `$` for variables, `-` for parameters, or three letters of a command or
  keyword). The status bar shows parse errors and warnings; click to jump to the line.
- **Build to .exe** (Ctrl+B): a small exe that runs the script with the right engine, using the C# compiler of .NET
  Framework 4 that every Windows has. The script's custom icon (picked with *Change icon…*) becomes the exe icon.
- **Tray, hotkey, toasts and jump list.** Close to tray, a global hotkey to show Shelly (Ctrl+Alt+L by default), a
  tray notification when a long run (20 s by default; 10, 20, 30 or 60 s in Settings) finishes while Shelly is in the background, and starred scripts in the
  taskbar jump list (right-click Shelly on the taskbar).
- **Compatibility page.** Windows, .NET Framework 4 (for exe builds), PowerShell 5.1 and 7, the execution policy, the
  hotkey and the tray, each with a status and a hint.
- **Update check.** About once a day (at most every 20 hours) Shelly looks at the latest GitHub release and, if there is a newer one, shows a link
  to download it. Nothing is downloaded or installed automatically; turn it off in Settings.
- **English and Russian**, switchable in Settings.

## Keyboard

| Keys | Action |
|---|---|
| Ctrl+1 / 2 / 3, Ctrl+, | Scripts, Favorites, History, Settings |
| Ctrl+K, Ctrl+F | Omnibox (type `>` for commands) |
| Enter | Run the selected script |
| Ctrl+Enter, Ctrl+I | Run settings |
| Ctrl+E / Ctrl+Shift+E | Edit in Shelly / in the external editor |
| Ctrl+D | Add to or remove from Favorites |
| Ctrl+B | Build .exe |
| F2, Delete | Rename, delete (to the Recycle Bin) |
| Ctrl+N, Ctrl+Shift+N | New script, new folder |
| F5 | Refresh the list |
| Ctrl+Shift+[ / Ctrl+Shift+] | Collapse / expand all groups |
| Ctrl+M | Compact navigation |

## Folder layout

```
Shelly\
  Shelly.exe
  Scripts\                 your scripts; subfolders become groups
  build\                   built .exe files
  assets\shelly.ico        icon for built .exe files
  assets\script-icons\     icons you picked for scripts
  launcher-settings.json   settings (created on first start)
  launcher-history\        run history
```

The examples: `Scripts\usd_rub.ps1` (USD/RUB rate chart from the Bank of Russia), `Scripts\Examples\Hello with
parameters.ps1`, `Scripts\Examples\Chris Titus WinUtil.ps1` (runs `irm https://christitus.com/win | iex` as
administrator; christitus.com is a built-in trusted source) and `Scripts\System\System summary.ps1` (a report about
this PC, as text or HTML).

## Building from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). On Windows:

```bat
dotnet test tests\Shelly.Core.Tests -c Release
dotnet publish src\Shelly -c Release -r win-x64 -o publish\fd
dotnet publish src\Shelly -c Release -r win-x64 -p:SelfContained=true -o publish\sc
```

The first `publish` makes the small framework-dependent `Shelly.exe`, the second the self-contained one. Copy the exe
next to the `Scripts` folder. On Linux or macOS the project builds too (`EnableWindowsTargeting` is on), and the tests of
`Shelly.Core` run there if PowerShell 7 (`pwsh`) is installed.

Projects:

- `src/Shelly.Core`: settings, scripts, analysis, engines, safety, history, runs, exe builds, updates; no UI, plain `net10.0`.
- `src/Shelly`: the WPF application (Fluent theme, AvalonEdit editor).
- `tests/Shelly.Core.Tests`: xUnit tests.

Start-up timings: run `Shelly.exe --perf` (or set `SHELLY_PERF=1`, or add `"perfLog": true` to `launcher-settings.json`) and
read `perf.log` next to `shelly.log` in `%TEMP%\Shelly`. ReadyToRun is off to keep the exe small; to compare, publish
with `-p:PublishReadyToRun=true`.

`ShellyGitHubRepo` in `Directory.Build.props` (`grvelvet/shelly`) is the repository used by the update check and the
GitHub link in About. A fork should change it and rebuild.

## Security

Shelly runs scripts with your user rights and is not a sandbox. To report a vulnerability, see [SECURITY.md](SECURITY.md).

## Authors

Created by **nah** and **Vika** ([github.com/grvelvet](https://github.com/grvelvet)).

## License

[MIT](LICENSE). The editor is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (MIT).
