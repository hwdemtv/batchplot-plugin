@echo off
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library /out:loadprobe.dll /r:"D:\Program Files\AutoCAD 2023\AutoCAD 2023\acmgd.dll" /r:"D:\Program Files\AutoCAD 2023\AutoCAD 2023\acdbmgd.dll" /r:"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoremgd.dll" loadprobe.cs
