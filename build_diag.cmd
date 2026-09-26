@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:library /optimize+ /warn:0 /out:BPDiag.dll /r:"D:\Program Files\AutoCAD 2023\AutoCAD 2023\acmgd.dll" /r:"D:\Program Files\AutoCAD 2023\AutoCAD 2023\acdbmgd.dll" /r:"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoremgd.dll" src\BpDiag.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK
