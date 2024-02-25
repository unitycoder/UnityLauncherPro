@echo off
pushd "%~dp0"
powershell Compress-7Zip "UnityLauncherPro\bin\Release\UnityLauncherPro.exe" -ArchiveFileName "UnityLauncherPro.zip" -Format Zip
:exit
popd
@echo on
