# expect: none
Invoke-WebRequest https://example.com/data.zip -OutFile "$env:TEMP\data.zip"
Expand-Archive "$env:TEMP\data.zip" "$env:TEMP\data"
