@echo off
pushd "%~dp0"
powershell Compress-7Zip "Bin\Release" -ArchiveFileName "UnityLauncherPro.zip" -Format Zip
:exit
popd
@echo on
