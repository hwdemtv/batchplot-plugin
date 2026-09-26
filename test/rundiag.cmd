@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ACAD=D:\Program Files\AutoCAD 2023\AutoCAD 2023
"%CSC%" /nologo /codepage:65001 /target:library /optimize+ /warn:0 /out:"%~dp0..\BpDiag.dll" /r:"%ACAD%\acmgd.dll" /r:"%ACAD%\acdbmgd.dll" /r:"%ACAD%\accoremgd.dll" "%~dp0..\src\BpDiag.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK
"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe" /i "C:\Users\hwdem\Desktop\汉阳科研大楼\汉阳科研大楼弱电平面图9.23V1(1).dwg" /s "%~dp0diag_real.scr" > "%~dp0diag_real.log" 2>&1
echo CONSOLE_EXIT=%errorlevel%
