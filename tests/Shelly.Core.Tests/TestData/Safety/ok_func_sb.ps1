# expect: none
function Invoke-Section([string]$title, [scriptblock]$body) { try { & $body } catch { } }
Invoke-Section "OS" { Get-CimInstance Win32_OperatingSystem }
