# expect: none
$t = [IO.File]::ReadAllText($p)
$k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Contoso')
$k.GetValue('A')
