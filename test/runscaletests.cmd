@echo off
cd /d C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test
set ACC="D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe"
set BPPLOT_KEEP_ORIG=0
%ACC% /i "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg" /s "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\scaletest150.scr" > s150.log 2>&1
echo S150 EXIT=%errorlevel%
%ACC% /i "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg" /s "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\scaletest1000.scr" > s1000.log 2>&1
echo S1000 EXIT=%errorlevel%
set BPPLOT_MERGE=1
%ACC% /i "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg" /s "C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\fixtest_merge.scr" > merge.log 2>&1
echo MERGE EXIT=%errorlevel%
