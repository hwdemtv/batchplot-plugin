param([int]$Stage)
$ErrorActionPreference = 'Continue'
$acad = 'D:\Program Files\AutoCAD 2023\AutoCAD 2023\acad.exe'
$dwg  = 'C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg'
$log  = "$env:APPDATA\BPPlot\preview_test.log"

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

  # 1) fresh session: find persisted menu WITHOUT loading the dll
  $target = $null
  foreach ($m in $app.MenuBar) { if ($m.Name -eq 'BP-批量打印') { $target = $m; break } }
  if (-not $target) { "MENU_NOT_PERSISTED"; exit 1 }
  "MENU_PERSISTED items=" + $target.Count

  # 2) inspect macros
  $previewMacro = $null
  for ($i = 0; $i -lt $target.Count; $i++) {
    $it = $target.Item($i)
    "  [" + $i + "] " + $it.Label + "  MACRO=" + $it.Macro
    if ($it.Label -match '打印预览') { $previewMacro = $it.Macro }
  }
  if (-not $previewMacro) { "NO_PREVIEW_ITEM"; exit 1 }

  # 3) simulate the click: strip control chars (^C=0x03, ^P=0x10) and send through command line
  $cmdpart = $previewMacro -replace "[\x03\x10]", ""
  "SENDING: " + $cmdpart
  $doc.SendCommand($cmdpart + [char]13)
  Start-Sleep -s 1
  $doc.SendCommand([char]13)   # Enter at GetEntity prompt -> auto-detect biggest frame
  "CLICK_SIMULATED"
}

if ($Stage -eq 3) {
  $deadline = (Get-Date).AddSeconds(60)
  $escTried = 0
  $result = 'LOG_TIMEOUT'
  while ((Get-Date) -lt $deadline) {
    $t = if (Test-Path $log) { Get-Content $log -Raw -Encoding UTF8 } else { '' }
    if ($t -match '预览流程全部成功') { $result = 'SUCCESS'; break }
    if ($t -match '预览失败') { $result = 'FAILED'; break }
    if ($t -match '未识别到图框') { $result = 'NOFRAME'; break }
    if ($t -match 'BPPREVIEW 开始' -and $t -match '预览引擎创建成功') {
      # preview window is up (auto-loaded dll ran the command) -> dismiss it
      if ($escTried -lt 3) {
        $p = Get-Process acad -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($p -and $p.MainWindowHandle -ne 0) {
          Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class W5 { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }'
          [W5]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
          Start-Sleep -Milliseconds 600
          Add-Type -AssemblyName System.Windows.Forms
          [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
          $escTried++
        }
      }
    }
    Start-Sleep -s 2
  }
  "RESULT=$result ESC_TRIED=$escTried"
  "--- LOG ---"
  if (Test-Path $log) { Get-Content $log -Encoding UTF8 }
}

if ($Stage -eq 4) {
  try {
    $app = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
    $app.ActiveDocument.SendCommand('_.quit _y' + [char]13)
    Start-Sleep -s 8
  } catch {}
  Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force
  "CLOSED"
}
