# expect: none
powershell -NoProfile -Command Get-ChildItem -Recurse C:\Temp
pwsh -NoProfile -File "$env:TEMP\tool.ps1" -Force
