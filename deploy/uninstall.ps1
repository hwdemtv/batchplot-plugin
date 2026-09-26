# BPPlot uninstaller: remove demand-load registry keys + files.
$ErrorActionPreference = 'Continue'
Write-Output "1/2 移除按需加载注册表 ..."
$root = 'HKCU:\Software\Autodesk\AutoCAD'
if (Test-Path $root) {
  foreach ($rel in Get-ChildItem $root) {
    foreach ($prod in Get-ChildItem $rel.PSPath) {
      $appKey = $prod.PSPath + '\Applications\BPPlot'
      if (Test-Path $appKey) { Remove-Item $appKey -Recurse -Force; Write-Output ("  removed " + $prod.PSChildName) }
    }
  }
}
Write-Output "2/2 删除程序文件 ..."
Remove-Item "$env:APPDATA\BPPlot\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:APPDATA\Autodesk\ApplicationPlugins\BPPlot.bundle" -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "卸载完成。重启 AutoCAD 后生效（配置文件 %APPDATA%\BPPlot\config.json 保留）。"
