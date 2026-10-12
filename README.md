<p align="center"><img src="docs/logo.png" width="160" alt="Shelly"></p>

<h1 align="center">Shelly</h1>

<p align="center">
  <a href="https://github.com/grvelvet/Shelly/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/grvelvet/Shelly"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4">
  <img alt="MIT" src="https://img.shields.io/badge/license-MIT-green">
</p>

<p align="center"><b>A portable Windows 11-style launcher for PowerShell scripts.</b><br>
Find, run, edit, check and build a .ps1 to .exe in one window.</p>
<p align="center"><img src="docs/screenshot.png" alt="Shelly"></p>

## Download

[Latest release](https://github.com/grvelvet/Shelly/releases/latest). Unzip anywhere and run `Shelly.exe`. No installer.

| File | Size | Needs |
|---|---|---|
| `shelly-<version>.zip` | about 2 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) |
| `shelly-<version>-selfcontained.zip` | about 70 MB | nothing |

Use the small build. If the runtime is missing, Windows shows a download link, or install it with `winget install Microsoft.DotNet.DesktopRuntime.10`.

**Check the file.** Each release has `SHA256SUMS.txt`.

```powershell
Get-FileHash .\shelly-1.0.11.zip -Algorithm SHA256
```

`Shelly.exe` is not code-signed. SmartScreen may warn. After the checksum matches: **More info → Run anyway**. If the browser marked the zip, unblock it first: `Unblock-File .\shelly-1.0.11.zip`.

**From 0.9.x:** put the new `Shelly.exe` next to your existing `Scripts` folder.

## What it does

- Searchable list of every `.ps1` in `Scripts`, with folders, favorites and filters.
- Windows PowerShell 5.1 or PowerShell 7. Auto picks the engine, or you pin it.
- Reads `param()` and shows a dialog.
- Safety check on PowerShell's AST. A guardrail, not a sandbox.
- Blocks scripts marked as downloaded until you unblock them.
- Output, history, tray, hotkey (Ctrl+Alt+L), jump list.
- Editor with highlighting and F5 to save and run.
- Build a script to a small `.exe`.
- English and Russian.

Details: [English guide](docs/USER_GUIDE.md) · [Русский](docs/USER_GUIDE_RU.md) · [Changelog](CHANGELOG.md)

## Folder layout

```
Shelly\
  Shelly.exe
  Scripts\
  build\
  launcher-settings.json
  launcher-history\
```

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bat
dotnet test tests\Shelly.Core.Tests -c Release
dotnet publish src\Shelly -c Release -r win-x64 -o publish\fd
```

Copy `publish\fd\Shelly.exe` next to `Scripts`. A tag `v*` builds both zips on GitHub Actions.

## Security

Scripts run with your rights. Shelly is not a sandbox. See [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE). The editor is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (MIT).
