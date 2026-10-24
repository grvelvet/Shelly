# expect: Power/high
Invoke-CimMethod -ClassName Win32_OperatingSystem -MethodName Win32Shutdown -Arguments @{ Flags = 5 }
