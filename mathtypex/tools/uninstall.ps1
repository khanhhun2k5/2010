# MathTypeX — gỡ cài đặt cho người dùng hiện tại.
$ErrorActionPreference = "Continue"
$ProgId = "MathTypeX.WordAddin"
$Clsid  = "{5EBC7F71-F8F9-45E5-AC8E-54FED67797E1}"

Get-Process "MathTypeX.Editor" -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($key in @(
    "HKCU:\Software\Microsoft\Office\Word\Addins\$ProgId",
    "HKCU:\Software\Classes\CLSID\$Clsid",
    "HKCU:\Software\Classes\Wow6432Node\CLSID\$Clsid",
    "HKCU:\Software\Classes\$ProgId",
    "HKCU:\Software\MathTypeX")) {
    if (Test-Path $key) { Remove-Item -Path $key -Recurse -Force }
}
$target = Join-Path $env:LOCALAPPDATA "MathTypeX"
foreach ($dir in @("addin", "editor")) {
    $path = Join-Path $target $dir
    if (Test-Path $path) { Remove-Item -Path $path -Recurse -Force }
}
Write-Host "Đã gỡ MathTypeX. (Giữ lại nhật ký và thiết lập: $target, $env:APPDATA\MathTypeX)" -ForegroundColor Green
