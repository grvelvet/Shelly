# expect: Encoded/high, Dynamic/high
$c = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($b))
iex $c
