@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test
"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe" /i "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\real.dwg" /readonly /s "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\real_fix.scr" > "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\real_fix.log" 2>&1
echo EXIT=%errorlevel%
