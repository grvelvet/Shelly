# expect: Autorun/medium
reg add HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Tool /d "%APPDATA%\tool.exe" /f
