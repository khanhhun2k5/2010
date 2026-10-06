#!/usr/bin/env bash
# Đóng gói bản Windows trên Linux/macOS (CI chính thức đóng gói trên Windows bằng tools/package.ps1 — cùng bố cục):
#   out/package/addin/    COM add-in .NET Framework 4.8 (Word nạp in-proc)
#   out/package/editor/   MathTypeX.Editor.exe (.NET 10, self-contained, một tệp)
#   out/package/          install/uninstall (.cmd + .ps1), README.txt, LICENSE
#   out/MathTypeX-win-x64.zip
# Thiếu tệp nào trong tools/package-manifest.txt (hoặc tệp rỗng) là lỗi.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/out"
pkg="$out/package"
rm -rf "$pkg" && mkdir -p "$pkg"

dotnet publish "$root/src/MathTypeX.WordAddin" -c Release -o "$pkg/addin" -p:DebugType=none
dotnet publish "$root/src/MathTypeX.Editor" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -o "$pkg/editor"
cp "$root"/tools/install.ps1 "$root"/tools/uninstall.ps1 "$root"/tools/install.cmd "$root"/tools/uninstall.cmd "$pkg/"
cp "$root/tools/package-readme.txt" "$pkg/README.txt"
cp "$root/LICENSE" "$pkg/"

missing=0
while IFS= read -r line; do
  entry="${line%$'\r'}"
  [[ -z "$entry" || "$entry" == \#* ]] && continue
  if [[ ! -s "$pkg/$entry" ]]; then echo "Thiếu hoặc rỗng: $entry" >&2; missing=1; fi
done < "$root/tools/package-manifest.txt"
[[ $missing -eq 0 ]] || { echo "Gói không hợp lệ." >&2; exit 1; }

rm -f "$out/MathTypeX-win-x64.zip"
python3 - "$pkg" "$out/MathTypeX-win-x64.zip" <<'PY'
import os, sys, zipfile
pkg, target = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
    for d, _, files in os.walk(pkg):
        for f in sorted(files):
            p = os.path.join(d, f)
            z.write(p, os.path.relpath(p, pkg))
PY
ls -la "$out/MathTypeX-win-x64.zip"
