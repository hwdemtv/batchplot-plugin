$ErrorActionPreference = 'Continue'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class WB { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L, T, R, B; } }'

Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -s 3
Start-Process -FilePath 'D:\Program Files\AutoCAD 2023\AutoCAD 2023\acad.exe' -ArgumentList '"C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg"'

$app = $null
for ($i = 0; $i -lt 60; $i++) {
  try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application'); break } catch { Start-Sleep -s 2 }
}
if (-not $app) { 'COM_TIMEOUT'; exit 1 }

# readiness: ActiveDocument non-null AND menu present
$ready = $false
for ($i = 0; $i -lt 45; $i++) {
  $docOk = $false; $menuOk = $false
  try { if ($app.ActiveDocument -ne $null) { $docOk = $true } } catch {}
  foreach ($m in $app.MenuBar) { if ($m.Name -eq 'BP-批量打印') { $menuOk = $true; break } }
  if ($docOk -and $menuOk) { $ready = $true; break }
  Start-Sleep -s 2
}
'DOC+MENU READY=' + $ready
Start-Sleep -s 4

$doc = $app.ActiveDocument
$doc.SendCommand('BPSELFTEST' + [char]13)
Start-Sleep -s 10

$log = "$env:APPDATA\BPPlot\selftest.log"
if (Test-Path $log) {
  '--- SELFTEST LOG ---'
  Get-Content $log -Encoding UTF8
} else {
  'NO LOG - capture command line'
  $p = Get-Process acad | Select-Object -First 1
  [WB]::ShowWindow($p.MainWindowHandle, 9) | Out-Null   # SW_RESTORE
  [WB]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 900
  $r = New-Object WB+RECT
  [WB]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  'RECT: ' + $r.L + ',' + $r.T + ' ' + ($r.R - $r.L) + 'x' + ($r.B - $r.T)
  Add-Type -AssemblyName System.Drawing
  $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), 300)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, ($r.B - 300), 0, 0, (New-Object System.Drawing.Size(($r.R - $r.L), 300)))
  $g.Dispose()
  $bmp.Save('C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\_cli3.png', [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  'shot saved'
}
