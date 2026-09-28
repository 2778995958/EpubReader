@echo off
cd /d "%~dp0"
dotnet build EpubReader.sln -c Debug
if errorlevel 1 (
  echo.
  echo Debug compile failed.
  exit /b 1
)
echo.
echo Output: src\EpubReader.App\bin\Debug\net8.0-windows\EpubReader.exe
