@echo off
setlocal
cd /d "%~dp0"

dotnet publish NexusRemotePC.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true

if errorlevel 1 (
  echo Build failed.
  exit /b 1
)

echo.
echo Built:
echo %~dp0bin\Release\net8.0-windows\win-x64\publish\NexusRemotePC.exe

copy /Y "%~dp0NexusRemote.ico" "%~dp0bin\Release\net8.0-windows\win-x64\publish\NexusRemote.ico" >nul

dotnet wix build installer\Product.wxs ^
  -d PublishDir="%~dp0bin\Release\net8.0-windows\win-x64\publish" ^
  -o "%~dp0bin\Release\NexusRemotePC-Setup.msi"

if errorlevel 1 (
  echo Installer build failed.
  exit /b 1
)

echo.
echo Installer:
echo %~dp0bin\Release\NexusRemotePC-Setup.msi
