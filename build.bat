@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 8 SDK and Visual Studio 2022 desktop build tools first.
  echo Or download the compiled installer / portable ZIP from GitHub Releases.
  pause
  exit /b 1
)
dotnet publish WindowsKeyboardIntegrityCheck.csproj -c Release -p:Platform=x64 -o artifacts\publish
if errorlevel 1 (
  pause
  exit /b 1
)
echo WinUI 3 self-contained output: artifacts\publish
 echo Keep ALL files together. This is not a single-file application.
pause
