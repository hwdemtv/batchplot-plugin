param([int]$Stage)
$ErrorActionPreference = 'Continue'
$log = "$env:APPDATA\BPPlot\preview_test.log"
$dll = 'C:/Users/hwdem/.zcode/workspace/default/batchplot-plugin/BPPlot.dll'
$acad = 'D:\Program Files\AutoCAD 2023\AutoCAD 2023\acad.exe'
$dwg  = 'C:\Users\hwdem\.zcode\workspace\default\batchplot-plugin\test\test_dwg.dwg'

if ($Stage -eq 1) {
  $p = Start-Process -FilePath $acad -ArgumentList ('"' + $dwg + '"') -PassThru
  "LAUNCHED PID=$($p.Id)"
}

if ($Stage -eq 2) {
  $app = $null
  for ($i = 0; $i -lt 90; $i++) {
    try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application'); break }
    catch { Start-Sleep -s 2 }
  }
  if (-not $app) { "COM_TIMEOUT"; exit 1 }
  "COM_OK Version=$($app.Version)"
  $doc = $app.ActiveDocument
  if (-not $doc) { "NO_ACTIVE_DOC"; exit 1 }
  $sl = $doc.GetVariable('SECURELOAD')
  "SECURELOAD=$sl"
  $doc.SendCommand('(setvar "filedia" 0)' + [char]13)
  if ($sl -ne 0) { $doc.SendCommand('(setvar "secureload" 0)' + [char]13) }
  Start-Sleep -s 1
  $doc.SendCommand('(command "_.netload" "' + $dll + '")' + [char]13)
  Start-Sleep -s 4
  $doc.SendCommand('(setvar "secureload" ' + $sl + ')' + [char]13)
  $doc.SendCommand('BPPREVIEW' + [char]13 + [char]13)
  "SENT"
}

if ($Stage -eq 3) {
  $deadline = (Get-Date).AddSeconds(150)
  $escTried = 0
  $result = 'LOG_TIMEOUT'
  while ((Get-Date) -lt $deadline) {
    $t = if (Test-Path $log) { Get-Content $log -Raw -Encoding UTF8 } else { '' }
    if ($t -match '预览流程全部成功') { $result = 'SUCCESS'; break }
    if ($t -match '预览失败') { $result = 'FAILED'; break }
    if ($t -match '未识别到图框') { $result = 'NOFRAME'; break }
    if ($t -match '页面渲染完成' -and $escTried -lt 4) {
      $p = Get-Process acad -ErrorAction SilentlyContinue | Select-Object -First 1
      if ($p -and $p.MainWindowHandle -ne 0) {
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class W { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }'
        [W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 600
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        $escTried++
        "ESC_SENT #$escTried"
      }
    }
    Start-Sleep -s 2
  }
  "RESULT=$result ESC_TRIED=$escTried"
  "--- LOG ---"
  if (Test-Path $log) { Get-Content $log -Encoding UTF8 } else { "(no log)" }
}

if ($Stage -eq 4) {
  try {
    $app = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
    $app.ActiveDocument.SendCommand('_.quit _y' + [char]13)
    Start-Sleep -s 10
  } catch { "QUIT_VIA_COM_FAILED" }
  Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -s 2
  if (Get-Process acad -ErrorAction SilentlyContinue) { "STILL_RUNNING" } else { "ACAD_CLOSED" }
}
