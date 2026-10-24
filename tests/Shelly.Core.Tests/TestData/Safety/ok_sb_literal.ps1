# expect: none
& { Write-Host hi }
$sb = { Get-Date }
& $sb
$sb.Invoke()
Invoke-Command -ScriptBlock { Get-Date }
