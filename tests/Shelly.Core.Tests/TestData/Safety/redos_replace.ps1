# expect: none
# a catastrophic pattern in -replace must not hang the analysis (it runs with a time limit)
$x = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!'
$y = $x -replace '(a+)+$', 'b'
Write-Host $y
