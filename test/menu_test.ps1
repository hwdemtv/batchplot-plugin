param([int]$Stage)
$ErrorActionPreference = 'Continue'
$acad = 'D:\Program Files\AutoCAD 2023\AutoCAD 2023\acad.exe'
$dwg  = 'C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg'
$dll  = 'C:/Users/hwdem/.zcode/workspace/default/batchplot-plugin/BPPlot.dll'

if ($Stage -eq 1) {
  $p = Start-Process -FilePath $acad -ArgumentList ('"' + $dwg + '"') -PassThru
  "LAUNCHED PID=$($p.Id)"
}

if ($Stage -eq 2) {
  $app = $null
  for ($i = 0; $i -lt 60; $i++) {
    try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application'); break }
    catch { Start-Sleep -s 2 }
  }
  if (-not $app) { "COM_TIMEOUT"; exit 1 }
  $doc = $app.ActiveDocument
  $doc.SendCommand('(setvar "filedia" 0)' + [char]13)
  Start-Sleep -s 1
  $doc.SendCommand('(command "_.netload" "' + $dll + '")' + [char]13)
  Start-Sleep -s 5
  # 验证菜单栏
  "MENUBAR COUNT=" + $app.MenuBar.Count
  $found = $false
  foreach ($m in $app.MenuBar) {
    "  ITEM: " + $m.Name
    if ($m.Name -eq 'BP-批量打印') { $found = $true }
  }
  "FOUND_BP_MENU=$found"
  if ($found) {
    # 展开方式验证：找到我们的菜单在菜单栏中的位置
    foreach ($m in $app.MenuBar) {
      if ($m.Name -eq 'BP-批量打印') { "POSITION_OK PopupCount=" + $m.Count }
    }
    # 截取窗口顶部（菜单栏区域）
    $p = Get-Process acad | Select-Object -First 1
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class W4 { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L, T, R, B; } }'
    [W4]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800
    $r = New-Object W4+RECT
    [W4]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    Add-Type -AssemblyName System.Drawing
    $w = $r.R - $r.L; $h2 = 130
    $bmp = New-Object System.Drawing.Bitmap($w, $h2)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save('C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\menu_proof.png', [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "SCREENSHOT SAVED"
  }
}
