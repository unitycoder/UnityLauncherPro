@echo off
pushd "%~dp0"
powershell Compress-7Zip "ULauncherPro\bin\Release\ULauncherPro.exe" -ArchiveFileName "ULauncherPro.zip" -Format Zip
:exit
popd
@echo on
