# expect: RegWrite/medium
$k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\Contoso', $true)
$k.SetValue('A', 1)
