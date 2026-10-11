# expect: NetDelete/medium
Add-Type -AssemblyName Microsoft.VisualBasic
[Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($d, 'DeleteAllContents')
