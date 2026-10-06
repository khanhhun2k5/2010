# MathTypeX — kiểm tra gói đã đóng: mọi tệp trong tools/package-manifest.txt phải có và khác rỗng,
# không có thư mục rỗng. Thoát với lỗi (throw) nếu thiếu, để CI không bao giờ tải lên một gói hỏng.
param(
    [Parameter(Mandatory = $true)][string]$PackageDir
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $PackageDir -PathType Container)) { throw "Không có thư mục gói: $PackageDir" }
$PackageDir = (Resolve-Path $PackageDir).Path
$manifest = Join-Path $PSScriptRoot "package-manifest.txt"
$required = Get-Content $manifest -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith("#") }

$problems = @()
foreach ($relative in $required) {
    $path = Join-Path $PackageDir ($relative -replace "[\\/]", [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $path -PathType Leaf)) { $problems += "thiếu $relative"; continue }
    if ((Get-Item $path).Length -eq 0) { $problems += "rỗng $relative" }
}
Get-ChildItem $PackageDir -Recurse -Directory | Where-Object { -not (Get-ChildItem $_.FullName -Recurse -File) } |
    ForEach-Object { $problems += "thư mục rỗng $($_.FullName)" }

Get-ChildItem $PackageDir -Recurse -File | Sort-Object FullName |
    ForEach-Object { "{0,12:N0}  {1}" -f $_.Length, $_.FullName.Substring($PackageDir.TrimEnd('\', '/').Length + 1) } | Write-Host

if ($problems.Count -gt 0) { throw "Gói không hợp lệ:`n  $($problems -join "`n  ")" }
Write-Host "Gói hợp lệ: $($required.Count) tệp bắt buộc có đủ." -ForegroundColor Green
