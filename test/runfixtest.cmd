@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test
set BPPLOT_KEEP_ORIG=1
"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe" /i "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg" /s "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\fixtest.scr" > "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\fixtest.log" 2>&1
echo EXIT=%errorlevel%
