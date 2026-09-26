@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test
"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe" /s "%~dp0%1" > "%~dp0%2" 2>&1
echo EXIT=%errorlevel%
