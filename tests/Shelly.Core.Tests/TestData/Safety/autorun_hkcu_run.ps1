# expect: Autorun/medium
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Tool -Value "$env:APPDATA\tool.exe"
