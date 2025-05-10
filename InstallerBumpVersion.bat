@echo off
setlocal EnableDelayedExpansion

:: Config
set VDPATH=UnityLauncherProInstaller\UnityLauncherProInstaller.vdproj
set VERSIONFILE=installer-version.txt
set TMPFILE=%TEMP%\vdproj_patch.tmp

:: Read current version
set /p CURVER=<%VERSIONFILE%
for /f "tokens=1-3 delims=." %%a in ("%CURVER%") do (
    set /a major=%%a
    set /a minor=%%b
    set /a build=%%c
)

:: Increment build
set /a build+=1
if !build! gtr 65535 (
    set /a build=0
    set /a minor+=1
)
if !minor! gtr 255 (
    set /a minor=0
    set /a major+=1
)

:: New version
set NEWVER=!major!.!minor!.!build!

:: Save back to file
echo !NEWVER! > %VERSIONFILE%

:: Prepare patch
if exist "%TMPFILE%" del "%TMPFILE%"

:: Read and patch
for /f "usebackq delims=" %%L in ("%VDPATH%") do (
    set "line=%%L"
    echo !line! | findstr /C:"ProductVersion" >nul
    if !errorlevel! == 0 (
        echo        "ProductVersion" = "8:!NEWVER!" >> "%TMPFILE%"
    ) else (
        echo !line! | findstr /C:"ProductCode" >nul
        if !errorlevel! == 0 (
            for /f %%G in ('"cscript //nologo InstallerGUIDGen.vbs"') do set "guid=%%G"
            echo        "ProductCode" = "8:{!guid!}" >> "%TMPFILE%"
        ) else (
            echo !line! >> "%TMPFILE%"
        )
    )
)

:: Apply changes
copy /y "%TMPFILE%" "%VDPATH%" >nul
del "%TMPFILE%"

echo ✅ Patched version to !NEWVER! and new ProductCode
pause
