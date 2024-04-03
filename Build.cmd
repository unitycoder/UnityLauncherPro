@echo off

REM Default VS paths to check if no Paths.cmd file exists
set VISUAL_STUDIO_PATH_0="%programfiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\msbuild.exe"
set VISUAL_STUDIO_PATH_1="%programfiles(x86)%\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\msbuild.exe"
set VISUAL_STUDIO_PATH_2="%programfiles(x86)%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\msbuild.exe"
set VISUAL_STUDIO_PATH_3="%programfiles(x86)%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\msbuild.exe"

pushd "%~dp0"
if exist Debug rd /s /q Debug
if exist Release rd /s /q Release
if exist x64 rd /s /q x64

if exist "Paths.cmd" (
REM Prefer Paths.cmd as Visual Studio path source if it exists.
call Paths.cmd
goto build
) else (
REM Otherwise try to auto-detect the Visual Studio path.
if exist %VISUAL_STUDIO_PATH_0% (
set VISUAL_STUDIO_PATH=%VISUAL_STUDIO_PATH_0%
goto build
)

if exist %VISUAL_STUDIO_PATH_1% (
set VISUAL_STUDIO_PATH=%VISUAL_STUDIO_PATH_1%
goto build
)

if exist %VISUAL_STUDIO_PATH_2% (
set VISUAL_STUDIO_PATH=%VISUAL_STUDIO_PATH_2%
goto build
)

if exist %VISUAL_STUDIO_PATH_3% (
set VISUAL_STUDIO_PATH=%VISUAL_STUDIO_PATH_3%
goto build
)

REM No default path found. Let the user know what to do.
echo No Visual Studio installation found. Please configure it manually.
echo  1. Copy 'Paths.cmd.template'.
echo  2. Rename it to 'Paths.cmd'.
echo  3. Enter your Visual Studio path in there.
echo  4. Restart the build.
REM Allow disabling pause to support non-interacting build chains.
if NOT "%~1"=="-no-pause" pause
goto end
)

:build
REM Log the used Vistual Studio version.
@echo on
%VISUAL_STUDIO_PATH% /p:Configuration=Release
@echo off

:end
popd
@echo on
