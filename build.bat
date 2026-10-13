@echo off
setlocal
cd /d "%~dp0"

echo.
echo Shelly build
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK not found. Install .NET 10 SDK:
    echo   winget install Microsoft.DotNet.SDK.10
    exit /b 1
)

set MODE=%1
if "%MODE%"=="" set MODE=fd

if /I "%MODE%"=="fd" goto fd
if /I "%MODE%"=="sc" goto sc
if /I "%MODE%"=="all" goto all
if /I "%MODE%"=="test" goto test

echo Usage: build.bat [fd ^| sc ^| all ^| test]
echo   fd    small exe, needs .NET 10 Desktop Runtime  (default)
echo   sc    self-contained exe
echo   all   both
echo   test  run tests only
exit /b 1

:test
echo Running tests...
dotnet test tests\Shelly.Core.Tests -c Release
exit /b %ERRORLEVEL%

:fd
call :publish fd false
if errorlevel 1 exit /b 1
call :copyexe publish\fd\Shelly.exe
exit /b 0

:sc
call :publish sc true
if errorlevel 1 exit /b 1
echo.
echo Self-contained exe: publish\sc\Shelly.exe
exit /b 0

:all
call :publish fd false
if errorlevel 1 exit /b 1
call :publish sc true
if errorlevel 1 exit /b 1
call :copyexe publish\fd\Shelly.exe
exit /b 0

:publish
echo.
echo Publishing %~1 ...
dotnet publish src\Shelly -c Release -r win-x64 -p:SelfContained=%~2 -o publish\%~1
exit /b %ERRORLEVEL%

:copyexe
echo.
copy /Y "%~1" "Shelly.exe" >nul
echo Shelly.exe updated next to Scripts.
exit /b 0
