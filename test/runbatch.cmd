@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test
set BPPLOT_MERGE=1
"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe" /i "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\real.dwg" /s "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\run_test.scr" > "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\real_plot.log" 2>&1
echo CONSOLE_EXIT=%errorlevel%
