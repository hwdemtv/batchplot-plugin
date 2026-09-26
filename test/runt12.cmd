@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test
"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe" /i "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg" /s "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\t12b.scr" > "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\t12.log" 2>&1
echo EXIT=%errorlevel%
