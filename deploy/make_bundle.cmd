@echo off
rem Assemble the release zip: BPPlot.bundle (standard-build auto-load) +
rem bin/ + install.ps1/uninstall.ps1 + cmd wrappers + readme.
rem Run AFTER build.cmd. Output: BPPlot-v0.7.zip next to this script.
setlocal
cd /d "%~dp0"
set VER=0.7
set STAGE=%TEMP%\bpplot_bundle_stage

rd /s /q "%STAGE%" 2>nul
mkdir "%STAGE%\BPPlot.bundle\Contents" || goto :fail
mkdir "%STAGE%\bin" || goto :fail
copy /y "PackageContents.xml"  "%STAGE%\BPPlot.bundle\"           >nul || goto :fail
copy /y "..\BPPlot.dll"        "%STAGE%\BPPlot.bundle\Contents\"  >nul || goto :fail
copy /y "..\PdfSharp.dll"      "%STAGE%\BPPlot.bundle\Contents\"  >nul || goto :fail
copy /y "..\BPPlot.dll"        "%STAGE%\bin\"                     >nul || goto :fail
copy /y "..\PdfSharp.dll"      "%STAGE%\bin\"                     >nul || goto :fail
copy /y "install.ps1"          "%STAGE%\"                          >nul || goto :fail
copy /y "uninstall.ps1"        "%STAGE%\"                          >nul || goto :fail
copy /y "安装到本机.cmd"         "%STAGE%\"                          >nul || goto :fail
copy /y "卸载本机.cmd"           "%STAGE%\"                          >nul || goto :fail
copy /y "使用说明.txt"           "%STAGE%\"                          >nul || goto :fail

del "BPPlot-v%VER%.zip" 2>nul
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath 'BPPlot-v%VER%.zip'"
if errorlevel 1 goto :fail
echo BUNDLE ZIP: %~dp0BPPlot-v%VER%.zip
echo STAGE: %STAGE%
exit /b 0

:fail
echo BUNDLE BUILD FAILED
exit /b 1
