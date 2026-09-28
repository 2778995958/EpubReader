@echo off
cd /d "%~dp0"
dotnet publish src\EpubReader.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
if errorlevel 1 (
  echo.
  echo Release publish failed.
  exit /b 1
)
echo.
echo Output: dist\EpubReader.exe
