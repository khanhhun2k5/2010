# MathTypeX — cài đặt cho người dùng hiện tại (không cần quyền admin).
# Chạy bằng install.cmd (tự bỏ qua ExecutionPolicy), hoặc:
#   powershell -ExecutionPolicy Bypass -File install.ps1
param(
    # Thư mục chứa addin\ và editor\ (mặc định: cạnh script này)
    [string]$PackageDir = $PSScriptRoot
)
$ErrorActionPreference = "Stop"

$ProgId = "MathTypeX.WordAddin"
$Clsid  = "{5EBC7F71-F8F9-45E5-AC8E-54FED67797E1}"
$Target = Join-Path $env:LOCALAPPDATA "MathTypeX"

$addinSrc  = Join-Path $PackageDir "addin"
$editorSrc = Join-Path $PackageDir "editor"
if (-not (Test-Path (Join-Path $addinSrc "MathTypeX.WordAddin.dll"))) { throw "Không thấy addin\MathTypeX.WordAddin.dll trong $PackageDir" }
if (-not (Test-Path (Join-Path $editorSrc "MathTypeX.Editor.exe"))) { throw "Không thấy editor\MathTypeX.Editor.exe trong $PackageDir" }

if (Get-Process WINWORD -ErrorAction SilentlyContinue) {
    Write-Warning "Word đang mở. Hãy đóng Word rồi chạy lại để add-in được nạp mới."
}
Get-Process "MathTypeX.Editor" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Sao chép vào $Target ..."
New-Item -ItemType Directory -Force -Path (Join-Path $Target "addin"), (Join-Path $Target "editor") | Out-Null
Copy-Item (Join-Path $addinSrc "*")  (Join-Path $Target "addin")  -Recurse -Force
Copy-Item (Join-Path $editorSrc "*") (Join-Path $Target "editor") -Recurse -Force
# File giải nén từ zip tải về mang "Mark of the Web"; .NET Framework sẽ từ chối nạp DLL nếu không gỡ.
Get-ChildItem $Target -Recurse -File | Unblock-File

$dll = Join-Path $Target "addin\MathTypeX.WordAddin.dll"
$codeBase = ([System.Uri]$dll).AbsoluteUri
$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($dll).FullName

Write-Host "Đăng ký COM add-in ($ProgId) cho người dùng hiện tại ..."
# HKCU\Software\Classes\CLSID cho Office 64-bit; Wow6432Node cho Office 32-bit trên Windows 64-bit.
foreach ($view in @("Software\Classes", "Software\Classes\Wow6432Node")) {
    $clsidKey = "HKCU:\$view\CLSID\$Clsid"
    New-Item -Path "$clsidKey\InprocServer32" -Force | Out-Null
    New-Item -Path "$clsidKey\ProgId" -Force | Out-Null
    Set-Item -Path $clsidKey -Value $ProgId
    Set-Item -Path "$clsidKey\ProgId" -Value $ProgId
    Set-Item -Path "$clsidKey\InprocServer32" -Value "mscoree.dll"
    Set-ItemProperty -Path "$clsidKey\InprocServer32" -Name "ThreadingModel" -Value "Both"
    Set-ItemProperty -Path "$clsidKey\InprocServer32" -Name "Class" -Value "MathTypeX.WordAddin.Connect"
    Set-ItemProperty -Path "$clsidKey\InprocServer32" -Name "Assembly" -Value $assemblyName
    Set-ItemProperty -Path "$clsidKey\InprocServer32" -Name "RuntimeVersion" -Value "v4.0.30319"
    Set-ItemProperty -Path "$clsidKey\InprocServer32" -Name "CodeBase" -Value $codeBase
}
New-Item -Path "HKCU:\Software\Classes\$ProgId\CLSID" -Force | Out-Null
Set-Item -Path "HKCU:\Software\Classes\$ProgId" -Value "MathTypeX Word Add-in"
Set-Item -Path "HKCU:\Software\Classes\$ProgId\CLSID" -Value $Clsid

$addinKey = "HKCU:\Software\Microsoft\Office\Word\Addins\$ProgId"
New-Item -Path $addinKey -Force | Out-Null
Set-ItemProperty -Path $addinKey -Name "FriendlyName" -Value "MathTypeX"
Set-ItemProperty -Path $addinKey -Name "Description" -Value "Gõ LaTeX thành Word Equation (Alt+M)"
New-ItemProperty -Path $addinKey -Name "LoadBehavior" -Value 3 -PropertyType DWord -Force | Out-Null

New-Item -Path "HKCU:\Software\MathTypeX" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\MathTypeX" -Name "EditorPath" -Value (Join-Path $Target "editor\MathTypeX.Editor.exe")

Write-Host ""
Write-Host "Xong. Mở Word: có tab 'MathTypeX' trên ribbon; đặt con trỏ trong văn bản và nhấn Alt+M." -ForegroundColor Green
Write-Host "Nhật ký: $env:LOCALAPPDATA\MathTypeX\logs\word-addin.log"
