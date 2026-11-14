@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Quillfolio (Windows Edition) - Release Build
echo   Author: B4rtiX
echo ===================================================
echo.

:: 1. Check for .NET SDK
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] 'dotnet' CLI not found!
    echo Please install the .NET 8 SDK for Windows from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo [OK] Found .NET SDK:
dotnet --version
echo.

:: 2. Restore packages
echo [1/3] Restoring NuGet packages...
dotnet restore Quillfolio.sln
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] NuGet restore failed.
    pause
    exit /b 1
)

:: 3. Build solution
echo.
echo [2/3] Building solution (Release)...
dotnet build Quillfolio.sln -c Release --no-restore -m:1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Solution build failed.
    pause
    exit /b 1
)

:: 4. Publish single-file executable
echo.
echo [3/3] Publishing single-file Quillfolio.exe to .\dist...
if not exist dist mkdir dist

dotnet publish Quillfolio.WPF\Quillfolio.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist --no-restore -m:1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publish failed.
    pause
    exit /b 1
)

echo.
echo ===================================================
echo   SUCCESS! Executable created at:
echo   .\dist\Quillfolio.exe
echo ===================================================
echo.
pause
