# expect: none
$zip = "$env:ProgramFiles\7-Zip\7z.exe"
& $zip x archive.zip
$q = { param($t) "[$t]" }
& $q 1
