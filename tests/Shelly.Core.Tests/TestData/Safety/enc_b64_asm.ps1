# expect: Encoded/high, Dynamic/high
$bytes = [Convert]::FromBase64String($blob)
[Reflection.Assembly]::Load($bytes)
