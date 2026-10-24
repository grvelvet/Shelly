# expect: none
msiexec /i "$env:TEMP\setup.msi" /qn
mshta "$env:TEMP\page.hta"
