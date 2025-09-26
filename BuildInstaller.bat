@ECHO OFF
SETLOCAL ENABLEEXTENSIONS ENABLEDELAYEDEXPANSION

ECHO:
ECHO === Starting Installer Build Workaround ===

REM Store current directory
SET "current_path=%CD%"

REM Try all known editions of Visual Studio 2022
SET "vs_base_path=%ProgramFiles%\Microsoft Visual Studio\2022"
FOR %%E IN (Community Professional Enterprise) DO (
    IF EXIST "%vs_base_path%\%%E\Common7\IDE\CommonExtensions\Microsoft\VSI\DisableOutOfProcBuild\DisableOutOfProcBuild.exe" (
        SET "buildfix_path=%vs_base_path%\%%E\Common7\IDE\CommonExtensions\Microsoft\VSI\DisableOutOfProcBuild"
        SET "devenv_path=%vs_base_path%\%%E\Common7\IDE\devenv.exe"
        SET "vs_edition=%%E"
        GOTO :FoundEdition
    )
)

ECHO [ERROR] Could not find DisableOutOfProcBuild.exe in any known VS2022 edition.
EXIT /B 1

:FoundEdition
ECHO Found Visual Studio 2022 Edition: %vs_edition%
CD /D "%buildfix_path%"
CALL DisableOutOfProcBuild.exe

REM Restore previous directory
CD /D "%current_path%"

REM === Validate required files ===
ECHO:
ECHO === Checking required files ===

SET "error=0"

IF NOT EXIST "ULauncherPro\bin\Release\ULauncherPro.exe" (
    ECHO [ERROR] Missing file: ULauncherPro\bin\Release\ULauncherPro.exe
    SET "error=1"
)
IF NOT EXIST "ULauncherPro\Images\icon.ico" (
    ECHO [ERROR] Missing file: ULauncherPro\Images\icon.ico
    SET "error=1"
)

IF %error% NEQ 0 (
    ECHO [ERROR] Required files are missing. Aborting installer build.
    EXIT /B 1
)


ECHO:
ECHO === Building Installer ===
"%devenv_path%" ULauncherPro.sln /Rebuild Release /Project ULauncherProInstaller > build_output.log 2>&1
SET "exitCode=%ERRORLEVEL%"

TYPE build_output.log
ECHO:
ECHO === devenv.exe exit code: %exitCode% ===

IF NOT "%exitCode%"=="0" (
    ECHO [ERROR] Installer build failed. Check build_output.log for details.
    EXIT /B %exitCode%
)


ECHO:
ECHO === Build Complete ===

REM Optional cleanup: disable workaround
REG DELETE "HKCU\Software\Microsoft\VisualStudio\Setup" /v VSDisableOutOfProcBuild /f >NUL 2>&1

ENDLOCAL
EXIT /B %exitCode%
