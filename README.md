<p align="center"><img src="docs/logo.png" width="160" alt="Shelly logo: a coral snail whose shell is a terminal window"></p>

<<<<<<< HEAD
# Shelly
=======
# Shelly: a Win11 style launcher for PowerShell scripts
>>>>>>> d5cf8105cd3be6462df5e7838dbad41da1f3bcfc

<p align="center">
  <a href="https://github.com/grvelvet/shelly/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/grvelvet/shelly"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4">
  <img alt="MIT license" src="https://img.shields.io/badge/license-MIT-green">
</p>

**A portable Windows 11-style launcher for PowerShell scripts.**  
Find, run, edit, check and build a `.ps1` to `.exe` in one window.

Created by nah and Vika · [github.com/grvelvet/shelly](https://github.com/grvelvet/shelly)

![Shelly](docs/screenshot.png)

## Download

[Latest release](https://github.com/grvelvet/shelly/releases/latest). Unzip anywhere and run `Shelly.exe`. No installer.

| File | Size | Needs |
|---|---|---|
| `shelly-<version>.zip` | ≈ 2 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) |
| `shelly-<version>-selfcontained.zip` | ≈ 70 MB | nothing |

The small build is the one to use. If the runtime is missing, Windows shows a download link on first start, or install it with `winget install Microsoft.DotNet.DesktopRuntime.10`.

**Check the file.** Each release includes `SHA256SUMS.txt`.

```powershell
Get-FileHash .\shelly-1.0.11.zip -Algorithm SHA256
```

`Shelly.exe` is not code-signed. SmartScreen may warn on the first start. After the checksum matches: **More info → Run anyway**. If the browser marked the zip, unblock it before unzipping: Properties → **Unblock**, or `Unblock-File .\shelly-1.0.11.zip`.

**From 0.9.x:** drop the new `Shelly.exe` next to your existing `Scripts` folder. Settings and history are read as they are.

## What it does

- Searchable list of every `.ps1` in `Scripts`, with folders, favorites and filters.
- Runs on Windows PowerShell 5.1 or PowerShell 7. Auto picks the engine, or you pin it.
- Reads `param()` and shows a dialog: types, defaults, required fields, `ValidateSet`.
- Safety check on PowerShell's AST (aliases, dynamic calls, download-and-run). A guardrail, not a sandbox.
- Blocks scripts marked as downloaded from the internet until you unblock them.
- Live output, history, tray, hotkey (Ctrl+Alt+L), jump list.
- Editor with highlighting, find and F5 to save and run.
- Build a script to a small `.exe`.
- English and Russian.

<<<<<<< HEAD
Details, shortcuts and how to write scripts for Shelly: [English guide](docs/USER_GUIDE.md) · [Русский](docs/USER_GUIDE_RU.md).  
What's new: [CHANGELOG.md](CHANGELOG.md). What's next: [ROADMAP.md](ROADMAP.md).
=======
![Shelly](docs/screenshot.png)

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
>>>>>>> d5cf8105cd3be6462df5e7838dbad41da1f3bcfc

## Folder layout

```
Shelly\
  Shelly.exe
  Scripts\                 your scripts
  build\                   built .exe files
  launcher-settings.json   created on first start
  launcher-history\        run history
```

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bat
dotnet test tests\Shelly.Core.Tests -c Release
dotnet publish src\Shelly -c Release -r win-x64 -o publish\fd
```

Copy `publish\fd\Shelly.exe` next to `Scripts`. Self-contained: add `-p:SelfContained=true -o publish\sc`.

`src/Shelly.Core` is the logic (no UI). `src/Shelly` is the WPF app. A tag `v*` builds both zips via GitHub Actions.

## Security

Scripts run with your rights. Shelly is not a sandbox. See [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE). The editor is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (MIT).
