# expect: none
# low: Encoded
$bytes = [Convert]::FromBase64String($png)
[IO.File]::WriteAllBytes("$env:TEMP\a.png", $bytes)
