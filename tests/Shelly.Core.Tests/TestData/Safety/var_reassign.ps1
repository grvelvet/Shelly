# expect: Dynamic/high
$x = { Get-Date }
$x = "Remove-Item"
& $x C:\Windows -Recurse
