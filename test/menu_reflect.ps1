$ErrorActionPreference = 'Continue'
$d = 'D:\Program Files\AutoCAD 2023\AutoCAD 2023'
foreach ($f in @('Autodesk.AutoCAD.Interop.dll', 'Autodesk.AutoCAD.Interop.Common.dll')) {
  $asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $d $f))
  Write-Output ('== ' + $f)
  $asm.GetTypes() | Where-Object { $_.Name -match 'Menu' } | ForEach-Object { Write-Output ('   ' + $_.FullName) }
}
Write-Output '===== members ====='
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $d 'Autodesk.AutoCAD.Interop.Common.dll'))
foreach ($tn in @('AcadMenuGroups', 'AcadMenuGroup', 'AcadPopupMenu', 'AcadMenuBar', 'AcadPopupMenus')) {
  $t = $asm.GetTypes() | Where-Object { $_.Name -eq $tn } | Select-Object -First 1
  if (-not $t) { Write-Output ("MISSING: " + $tn); continue }
  Write-Output ('== ' + $t.FullName)
  $t.GetMethods() | ForEach-Object { Write-Output ('   ' + $_.ToString()) }
}
$asm2 = [System.Reflection.Assembly]::LoadFrom((Join-Path $d 'Autodesk.AutoCAD.Interop.dll'))
$app = $asm2.GetTypes() | Where-Object { $_.Name -eq 'AcadApplication' } | Select-Object -First 1
if ($app) {
  Write-Output '== AcadApplication menu props'
  $app.GetProperties() | Where-Object { $_.Name -match 'Menu' } | ForEach-Object { Write-Output ('   ' + $_.PropertyType.Name + ' ' + $_.Name) }
}
