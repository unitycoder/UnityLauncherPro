@echo off
pushd "%~dp0"
if exist Debug rd /s /q Debug
if exist Release rd /s /q Release
if exist x64 rd /s /q x64

IF NOT EXIST "Paths.cmd" (
ECHO Please copy "Paths.cmd.template", enter your Visual Studio path and rename it to "Paths.cmd".
PAUSE
GOTO exit
)

call Paths.cmd
"%VISUAL_STUDIO_PATH%" /p:Configuration=Release

:exit
popd
@echo on
