<p align="center"><img src="docs/logo.png" width="160" alt="Shelly logo: a coral snail whose shell is a terminal window"></p>

# Shelly

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

Details, shortcuts and how to write scripts for Shelly: [English guide](docs/USER_GUIDE.md) · [Русский](docs/USER_GUIDE_RU.md).  
What's new: [CHANGELOG.md](CHANGELOG.md). What's next: [ROADMAP.md](ROADMAP.md).

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
