# expect: none
Set-Alias -Name ll -Value Get-ChildItem
ll C:\Windows
gci | % { $_.Name }
