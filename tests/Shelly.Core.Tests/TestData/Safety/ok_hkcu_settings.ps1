# expect: none
Set-ItemProperty -Path 'HKCU:\Software\MyApp' -Name Theme -Value dark
reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
