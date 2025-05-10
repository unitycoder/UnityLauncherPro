@echo off
setlocal
pushd "%~dp0"

:: -----------------------------
:: Generate date-based version
:: -----------------------------
for /f %%a in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"') do set DATE=%%a

:: -----------------------------
:: Configuration
:: -----------------------------
set APPNAME=UnityLauncherPro
set OUTPUT=%APPNAME%_%DATE%.msix
set BUILD_DIR=_AppxTemp
set MANIFEST=AppxManifest.xml

:: -----------------------------
:: Clean previous build
:: -----------------------------
if exist "%OUTPUT%" del /f /q "%OUTPUT%"
if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
mkdir "%BUILD_DIR%"

:: -----------------------------
:: Copy required files
:: -----------------------------
echo Copying files to %BUILD_DIR%...

xcopy /y "UnityLauncherPro\\bin\\Release\\UnityLauncherPro.exe" "%BUILD_DIR%\\"
xcopy /y "UnityLauncherPro\\Images\\icon.ico" "%BUILD_DIR%\\"
copy /y "AppxManifest.xml" "%BUILD_DIR%\AppxManifest.xml"
xcopy /y /s /e "Installer" "%BUILD_DIR%\Installer\" >nul

:: if exist "UnityLauncherPro\\Scripts" xcopy /y /s /e "UnityLauncherPro\\Scripts" "%BUILD_DIR%\\Scripts\\"
:: if exist "UnityLauncherPro\\Images" xcopy /y /s /e "UnityLauncherPro\\Images" "%BUILD_DIR%\\Images\\"

:: -----------------------------
:: Validate AppxManifest.xml
:: -----------------------------
if not exist "%BUILD_DIR%\\AppxManifest.xml" (
    echo ❌ ERROR: AppxManifest.xml not found in %BUILD_DIR%.
    pause
    goto :end
)

:: -----------------------------
:: Build MSIX
:: -----------------------------
echo Building MSIX package...
makeappx pack /d "%BUILD_DIR%" /p "%OUTPUT%"
if %errorlevel% neq 0 (
    echo ❌ MSIX build failed.
    pause
    goto :end
)

:: -----------------------------
:: Done
:: -----------------------------
echo MSIX created: %OUTPUT%
echo Run this to install:
echo powershell -Command "Add-AppxPackage '%OUTPUT%'"
echo.
pause

:end
popd
endlocal
