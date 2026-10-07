@echo off
rem Builds a single portable QuickCal.exe into the "publish" folder.
rem Requires the .NET 10 SDK (installed together with Visual Studio).
cd /d "%~dp0"
dotnet publish QuickCal.csproj -c Release -p:PublishProfile=Portable
if errorlevel 1 (
  echo.
  echo Build failed. See the messages above.
  pause
  exit /b 1
)
echo.
echo Done! Your portable app is here:
echo   %~dp0publish\QuickCal.exe
explorer "%~dp0publish"
pause
