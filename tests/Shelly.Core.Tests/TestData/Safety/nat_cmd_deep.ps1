# expect: Nested/medium
# cmd /c nested deeper than the scan reads: that is said, not skipped silently
cmd /c cmd /c cmd /c cmd /c rd /s /q C:\Windows
