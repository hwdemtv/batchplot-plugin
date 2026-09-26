# BPPlot installer: copy DLLs to a stable per-user dir + write HKCU demand-load
# registry keys for every installed AutoCAD version (same mechanism as MSteel).
$ErrorActionPreference = 'Continue'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path     # zip root
$binDest = "$env:APPDATA\BPPlot\bin"

Write-Output "1/3 复制程序到 $binDest ..."
New-Item -ItemType Directory -Force -Path $binDest | Out-Null
Copy-Item "$src\bin\BPPlot.dll"   $binDest -Force
Copy-Item "$src\bin\PdfSharp.dll" $binDest -Force

Write-Output "2/3 写入按需加载注册表（HKCU，覆盖本机全部 AutoCAD 版本）..."
$loader = "$binDest\BPPlot.dll"
$commands = @('BPLOT','BPLOTAUTO','BP1','BPPREVIEW','BPLOTMERGE','BPL','BPSPLIT','BPTEACH','BPREV','BPSELFTEST','BPHELP','BPABOUT')
$root = 'HKCU:\Software\Autodesk\AutoCAD'
$written = 0
if (Test-Path $root) {
  foreach ($rel in Get-ChildItem $root) {                       # R24.0 / R24.1 / ...
    foreach ($prod in Get-ChildItem $rel.PSPath) {              # ACAD-xxxx:lang
      $appKey = $prod.PSPath + '\Applications\BPPlot'
      New-Item -Path $appKey -Force | Out-Null
      New-ItemProperty -Path $appKey -Name 'DESCRIPTION' -Value 'BPPlot batch plot' -PropertyType String -Force | Out-Null
      New-ItemProperty -Path $appKey -Name 'LOADCTRLS'  -Value 0x2 -PropertyType DWord -Force | Out-Null
      New-ItemProperty -Path $appKey -Name 'MANAGED'    -Value 1    -PropertyType DWord -Force | Out-Null
      New-ItemProperty -Path $appKey -Name 'LOADER'     -Value $loader -PropertyType String -Force | Out-Null
      $cmdsKey = $appKey + '\COMMANDS'
      New-Item -Path $cmdsKey -Force | Out-Null
      foreach ($c in $commands) {
        New-ItemProperty -Path $cmdsKey -Name $c -Value $c -PropertyType String -Force | Out-Null
      }
      $written++
      Write-Output ("  " + $prod.PSChildName + " of " + $rel.PSChildName)
    }
  }
}
if ($written -eq 0) { Write-Output "  未找到 AutoCAD 注册表项（本机未装或非标准布局）" }

# bundle 也一并放置：标准 AutoCAD 构建上可双保险自动加载
$bundleDest = "$env:APPDATA\Autodesk\ApplicationPlugins\BPPlot.bundle"
if (Test-Path "$src\BPPlot.bundle") {
  Write-Output "  另放置 ApplicationPlugins bundle（标准构建备用）..."
  Copy-Item "$src\BPPlot.bundle" $bundleDest -Recurse -Force
}

Write-Output "3/3 完成。请完全退出并重新启动 AutoCAD："
Write-Output "  - 启动时自动加载插件并挂出「BP-批量打印」菜单"
Write-Output "  - 命令行输入 BPLOT / BPLOTAUTO / BPPREVIEW 等可直接执行"
