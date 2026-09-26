@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ACAD=D:\Program Files\AutoCAD 2023\AutoCAD 2023
"%CSC%" /nologo /codepage:65001 /target:library /optimize+ /warn:4 /out:"%~dp0BPPlot.dll" /r:"%ACAD%\acmgd.dll" /r:"%ACAD%\acdbmgd.dll" /r:"%ACAD%\accoremgd.dll" /r:"%ACAD%\AcCui.dll" /r:"%ACAD%\Autodesk.AutoCAD.Interop.dll" /r:"%ACAD%\Autodesk.AutoCAD.Interop.Common.dll" /r:"%~dp0libs\PdfSharp.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0src\BPPlot.cs" "%~dp0src\BpForm.cs" "%~dp0src\BpMenu.cs"
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %~dp0BPPlot.dll
