# expect: Dynamic/high
$t = [Type]::GetType($name)
$t.GetMethod('Run').Invoke($null, @())
