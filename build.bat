@echo off
rem Kept for double-click and old habits: build.ps1 does the work (see README, "Build from source").
rem   build.bat            Shelly.exe next to Scripts (build.ps1 local), as before
rem   build.bat test       the tests
rem   build.bat sc / all   publish (self-contained / both) to artifacts\publish
rem   build.bat <tasks>    anything build.ps1 takes: build.bat pack, build.bat clean all ...
setlocal
set "TASK=%~1"
if "%TASK%"=="" set "TASK=local"
if /I "%TASK%"=="fd" set "TASK=local"
if /I "%TASK%"=="sc" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" publish -Flavor sc
    exit /b %ERRORLEVEL%
)
if /I "%TASK%"=="all" if "%~2"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" publish local
    exit /b %ERRORLEVEL%
)
shift
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %TASK% %1 %2 %3 %4 %5 %6 %7 %8 %9
exit /b %ERRORLEVEL%
