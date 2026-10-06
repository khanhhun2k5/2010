#!/usr/bin/env bash
# Đóng gói bản Windows (chạy được trên Linux/CI): out/MathTypeX-win-x64.zip
#   addin/   COM add-in .NET Framework 4.8 (Word nạp in-proc)
#   editor/  MathTypeX.Editor.exe (.NET 10, self-contained, một file)
#   install.cmd / uninstall.cmd
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
pkg="$root/out/package"
rm -rf "$pkg" && mkdir -p "$pkg"

dotnet publish "$root/src/MathTypeX.WordAddin" -c Release -o "$pkg/addin" -p:DebugType=none
dotnet publish "$root/src/MathTypeX.Editor" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -o "$pkg/editor"
cp "$root"/tools/install.ps1 "$root"/tools/uninstall.ps1 "$root"/tools/install.cmd "$root"/tools/uninstall.cmd "$pkg/"
cp "$root/LICENSE" "$pkg/"

(cd "$pkg" && rm -f "$root/out/MathTypeX-win-x64.zip" && python3 -c "
import os, zipfile
with zipfile.ZipFile('$root/out/MathTypeX-win-x64.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for d, _, files in os.walk('.'):
        for f in files:
            p = os.path.join(d, f)
            z.write(p, os.path.relpath(p, '.'))
")
ls -la "$root/out/MathTypeX-win-x64.zip"
