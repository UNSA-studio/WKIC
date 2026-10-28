@echo off
setlocal enabledelayedexpansion

rem ==========================================================
rem  Windows Keyboard Integrity Check - one-click builder
rem  Uses the C# compiler that ships with .NET Framework 4.x
rem  (csc.exe is present on every Windows 8 / 10 / 11 box).
rem  No SDK, no Visual Studio, no NuGet required.
rem ==========================================================

set "ROOT=%~dp0"
set "SRC=%ROOT%src"
set "OUT=%ROOT%build"
set "EXENAME=WindowsKeyboardIntegrityCheck.exe"

echo ==========================================================
echo   Windows Keyboard Integrity Check - Builder
echo ==========================================================
echo.

set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not defined CSC (
    echo [ERROR] csc.exe not found.
    echo         .NET Framework 4.x is required ^(built into Windows 8/10/11^).
    pause
    exit /b 1
)

echo [1/3] Compiler : %CSC%
if not exist "%OUT%" mkdir "%OUT%"
echo [2/3] Output   : %OUT%\%EXENAME%
echo [3/3] Compiling sources in %SRC% ...
echo.

set "SOURCES="
for %%F in ("%SRC%\*.cs") do set SOURCES=!SOURCES! "%%F"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
    /out:"%OUT%\%EXENAME%" ^
    /win32icon:"%ROOT%assets\app.ico" ^
    /resource:"%ROOT%assets\app.ico",WinKbdCheck.assets.app.ico ^
    /win32manifest:"%ROOT%app.manifest" ^
    /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Management.dll ^
    !SOURCES!

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo.
echo ==========================================================
echo   Build OK  --^>  %OUT%\%EXENAME%
echo ==========================================================
echo.
echo Tip: right-click the exe and choose "Run as administrator"
echo      for the most reliable full keyboard capture.
echo.
pause
