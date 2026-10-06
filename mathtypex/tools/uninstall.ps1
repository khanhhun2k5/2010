# MathTypeX — gỡ cài đặt. Mặc định cho người dùng hiện tại; -AllUsers gỡ bản cài cho mọi người dùng (cần quyền admin).
param(
    [switch]$AllUsers
)
$ErrorActionPreference = "Continue"
$ProgId = "MathTypeX.WordAddin"
$Clsid  = "{5EBC7F71-F8F9-45E5-AC8E-54FED67797E1}"

if ($AllUsers) {
    $Hive = "HKLM:\Software"
    $target = Join-Path $env:ProgramFiles "MathTypeX"
}
else {
    $Hive = "HKCU:\Software"
    $target = Join-Path $env:LOCALAPPDATA "MathTypeX"
}

Get-Process "MathTypeX.Editor" -ErrorAction SilentlyContinue | Stop-Process -Force
$keys = @(
    "$Hive\Microsoft\Office\Word\Addins\$ProgId",
    "$Hive\Classes\CLSID\$Clsid",
    "$Hive\Classes\Wow6432Node\CLSID\$Clsid",
    "$Hive\Classes\$ProgId",
    "$Hive\MathTypeX")
if ($AllUsers) { $keys += "HKLM:\Software\WOW6432Node\Microsoft\Office\Word\Addins\$ProgId" }
foreach ($key in $keys) {
    if (Test-Path $key) { Remove-Item -Path $key -Recurse -Force }
}
foreach ($dir in @("addin", "editor")) {
    $path = Join-Path $target $dir
    if (Test-Path $path) { Remove-Item -Path $path -Recurse -Force }
}
if ($AllUsers -and (Test-Path $target) -and -not (Get-ChildItem $target -Force)) { Remove-Item $target -Force }
Write-Host "Đã gỡ MathTypeX. (Giữ lại nhật ký và thiết lập: $env:LOCALAPPDATA\MathTypeX, $env:APPDATA\MathTypeX)" -ForegroundColor Green
