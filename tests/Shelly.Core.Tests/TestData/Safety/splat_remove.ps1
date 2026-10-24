# expect: DelSys/high
$p = @{ Path = 'C:\Windows'; Recurse = $true }
Remove-Item @p
