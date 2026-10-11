# expect: Shadows/high
Get-WmiObject Win32_ShadowCopy | ForEach-Object { $_.Delete() }
