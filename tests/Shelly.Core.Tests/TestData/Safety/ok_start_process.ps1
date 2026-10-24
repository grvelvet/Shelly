# expect: none
Start-Process notepad.exe
Start-Process explorer.exe -ArgumentList $env:USERPROFILE
