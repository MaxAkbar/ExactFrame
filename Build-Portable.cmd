@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 10 SDK, then run this file again.
  echo https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)

rem Single self-contained exe: .NET and the Windows App SDK runtime are bundled.
dotnet publish src\ExactFrame\ExactFrame.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o artifacts\win-x64
if errorlevel 1 (
  echo Build failed. Review the output above.
  pause
  exit /b 1
)
echo.
echo Ready: artifacts\win-x64\ExactFrame.exe
pause
