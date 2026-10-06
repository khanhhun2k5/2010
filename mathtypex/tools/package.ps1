# MathTypeX — đóng gói bản Windows (chạy trên Windows: Windows PowerShell 5.1 hoặc PowerShell 7).
# Kết quả: out\package\  (addin\, editor\, install/uninstall, README.txt, LICENSE)
#          out\MathTypeX-win-x64.zip  (khi có -Zip)
# Cùng bố cục với tools/package.sh (bản cho Linux); cả hai kiểm tra theo tools/package-manifest.txt.
param(
    [string]$Configuration = "Release",
    [switch]$Zip
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "out"
$pkg = Join-Path $out "package"

function Invoke-Dotnet([string[]]$Arguments) {
    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) thất bại (mã thoát $LASTEXITCODE)" }
}

if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pkg | Out-Null

# Add-in COM (.NET Framework 4.8, Word nạp trong tiến trình) — không phụ thuộc NuGet lúc chạy.
Invoke-Dotnet @("publish", (Join-Path $root "src/MathTypeX.WordAddin"), "-c", $Configuration,
    "-o", (Join-Path $pkg "addin"), "-p:DebugType=none")

# Editor (.NET 10 WPF, self-contained, một tệp .exe kèm WebView2Loader.dll và thư viện gốc của WPF).
Invoke-Dotnet @("publish", (Join-Path $root "src/MathTypeX.Editor"), "-c", $Configuration,
    "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none", "-o", (Join-Path $pkg "editor"))

foreach ($name in @("install.ps1", "uninstall.ps1", "install.cmd", "uninstall.cmd")) {
    Copy-Item (Join-Path $PSScriptRoot $name) $pkg
}
Copy-Item (Join-Path $PSScriptRoot "package-readme.txt") (Join-Path $pkg "README.txt")
Copy-Item (Join-Path $root "LICENSE") $pkg

& (Join-Path $PSScriptRoot "verify-package.ps1") -PackageDir $pkg

if ($Zip) {
    $zipPath = Join-Path $out "MathTypeX-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $pkg "*") -DestinationPath $zipPath
    Get-Item $zipPath | Format-Table Name, Length -AutoSize
}
