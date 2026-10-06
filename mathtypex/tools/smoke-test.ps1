# MathTypeX — kiểm tra khói (smoke test) gói Windows trên một máy Windows, không cần Word.
# Chạy bằng Windows PowerShell 5.1 (powershell.exe) để dùng đúng .NET Framework 4.8 như WINWORD.EXE:
#   1. nạp mọi DLL của add-in và giải mọi kiểu (thiếu phụ thuộc → lỗi);
#   2. ribbon XML hợp lệ, mọi callback onAction có thật trên lớp Connect;
#   3. lõi chạy đúng trên .NET Framework: LaTeX → OMML → Flat OPC, OMML → LaTeX, khoá, scanner, planner,
#      kho metadata, settings.json, giao thức pipe;
#   4. editor\MathTypeX.Editor.exe --self-test (bản một tệp, self-contained);
#   5. install.ps1 → tạo add-in qua COM như Word làm (tiến trình 64-bit và 32-bit) → uninstall.ps1 gỡ sạch.
# Thoát với mã 1 nếu có bước thất bại.
# -PortableOnly: chỉ chạy các bước thư viện (không add-in/ribbon/editor/cài đặt) — để thử script trên PowerShell 7 ngoài Windows.
param(
    [string]$PackageDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "out/package"),
    [switch]$SkipInstall,
    [switch]$PortableOnly
)
$ErrorActionPreference = "Stop"
if ($PortableOnly) { $SkipInstall = $true }
elseif ($PSVersionTable.PSEdition -ne "Desktop") {
    throw "Hãy chạy bằng Windows PowerShell 5.1 (powershell.exe): add-in phải được kiểm tra trên .NET Framework như trong Word."
}
$PackageDir = (Resolve-Path $PackageDir).Path
$addinDir = Join-Path $PackageDir "addin"
$failures = New-Object System.Collections.Generic.List[string]

function Step([string]$Name, [scriptblock]$Body) {
    try {
        $result = & $Body
        Write-Host "PASS $Name  $result" -ForegroundColor Green
    }
    catch {
        $failures.Add("$Name — $($_.Exception.Message)")
        Write-Host "FAIL $Name — $($_.Exception.ToString())" -ForegroundColor Red
    }
}

function Invoke-PowerShell([string]$Exe, [string]$Script) {
    # -EncodedCommand: truyền script nhiều dòng sang tiến trình con mà không vướng quy tắc dấu ngoặc của Windows.
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Script))
    $output = & $Exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "mã thoát $LASTEXITCODE`n$output" }
    return $output.Trim()
}

Step "clr" { "CLR $([Environment]::Version), tiến trình $([IntPtr]::Size * 8)-bit" }

Step "addin-assemblies" {
    $names = @()
    foreach ($dll in Get-ChildItem $addinDir -Filter *.dll) {
        if ($PortableOnly -and $dll.Name -eq "MathTypeX.WordAddin.dll") { continue }
        $assembly = [Reflection.Assembly]::LoadFrom($dll.FullName)
        try {
            [void]$assembly.GetTypes()
        }
        catch {
            # PowerShell bọc ngoại lệ của phương thức .NET (MethodInvocationException): tìm ReflectionTypeLoadException bên trong.
            $inner = $_.Exception
            while ($inner -and -not ($inner -is [Reflection.ReflectionTypeLoadException])) { $inner = $inner.InnerException }
            if (-not $inner) { throw }
            throw "$($dll.Name): " + (($inner.LoaderExceptions | ForEach-Object { $_.Message } | Select-Object -Unique) -join "; ")
        }
        $names += $assembly.GetName().Name
    }
    if (-not $PortableOnly -and $names -notcontains "MathTypeX.WordAddin") { throw "không có MathTypeX.WordAddin.dll" }
    "$($names.Count) assembly: $($names -join ', ')"
}

if (-not $PortableOnly) { Step "ribbon" {
    $connect = New-Object MathTypeX.WordAddin.Connect
    [xml]$ribbon = $connect.GetCustomUI("Microsoft.Word.Document")
    $ns = New-Object Xml.XmlNamespaceManager $ribbon.NameTable
    $ns.AddNamespace("c", "http://schemas.microsoft.com/office/2009/07/customui")
    $buttons = @($ribbon.SelectNodes("//c:button", $ns))
    if ($buttons.Count -lt 1) { throw "ribbon không có nút nào" }
    $connectType = [MathTypeX.WordAddin.Connect]
    foreach ($button in $buttons) {
        $method = $connectType.GetMethod($button.onAction)
        if ($null -eq $method -or $method.GetParameters().Count -ne 1) {
            throw "nút $($button.id): callback $($button.onAction)(object) không có trên Connect"
        }
    }
    "$($buttons.Count) nút: $(($buttons | ForEach-Object { $_.id }) -join ', ')"
} }

Step "core-netfx" {
    $options = New-Object MathTypeX.Editing.ComposeOptions
    $outcome = [MathTypeX.Editing.EquationComposer]::Compose('\int_0^1 \frac{x^2}{1+x^2}\,dx', $options, [MathTypeX.UiLanguage]::Vi, $false, $true)
    if ($null -eq $outcome.Result) { throw "Compose bị chặn: $($outcome.BlockingMessage)" }
    if ($outcome.Result.FlatOpc -notmatch "m:nary") { throw "OMML không có m:nary" }

    $element = [MathTypeX.Documents.OmmlExtractor]::FirstEquation($outcome.Result.FlatOpc)
    $reverse = [MathTypeX.Documents.OmmlToLatex]::Convert($element)
    $key = [MathTypeX.Documents.EquationIdentity]::KeyOfNormalized($reverse.NormalizedLatex)
    if (-not $key.StartsWith("k1:")) { throw "khoá sai: $key" }

    $text = 'Với $x^2$, ta có $$E(X)=\mu.$$ Do đó, giá $20 và $30.' + "`r"
    $candidates = [MathTypeX.Scanning.LatexScanner]::Scan($text, $null)
    if ($candidates.Count -ne 2) { throw "scanner tìm thấy $($candidates.Count) công thức, cần 2" }
    $plan = [MathTypeX.Editing.Conversion.ConversionPlanner]::Plan($text, 0, $text.Length, $null, $null)
    if ($plan.Items.Count -ne 2 -or -not $plan.Items[1].Display -or -not $plan.Items[1].BreakAfter) {
        throw "planner sai: $($plan.Items.Count) mục"
    }
    "OMML → LaTeX '$($reverse.NormalizedLatex)', $key, scanner/planner OK"
}

Step "metadata-netfx" {
    $record = [MathTypeX.Documents.EquationRecord]::Create("k1:abc", '\frac{a}{b}', '\frac{a}{b}', $true, "Cambria Math")
    $record.SourceText = '$$\frac{a}{b}$$'
    $store = New-Object MathTypeX.Documents.EquationStore
    $store.Upsert($record)
    $back = [MathTypeX.Documents.EquationStore]::Parse($store.ToXml()).FindByKey("k1:abc")
    if ($null -eq $back -or $back.OriginalLatex -ne '\frac{a}{b}' -or $back.SourceText -ne '$$\frac{a}{b}$$') {
        throw "kho metadata (CustomXMLPart) không đọc lại được"
    }

    $folder = Join-Path ([IO.Path]::GetTempPath()) ("mtx-settings-" + [Guid]::NewGuid().ToString("N"))
    $path = Join-Path $folder "settings.json"
    $settings = New-Object MathTypeX.Editing.UserSettings
    $settings.MathFont = "XITS Math"
    $settings.Ignore([string[]]@('$HOME$'))
    $settings.Save($path)
    $settings.Save($path)
    $loaded = [MathTypeX.Editing.UserSettings]::Load($path)
    Remove-Item $folder -Recurse -Force
    if ($loaded.MathFont -ne "XITS Math" -or -not $loaded.IsIgnored('$HOME$') -or -not $loaded.BeginnerMode) {
        throw "settings.json không đọc lại đúng"
    }
    "CustomXMLPart + settings.json OK"
}

Step "protocol-netfx" {
    $item = New-Object MathTypeX.Interop.ScanItem
    $item.Id = 7
    $item.Source = '$$E=mc^2$$'
    $item.Context = 'ta có ⟦$$E=mc^2$$⟧ nên'
    $request = New-Object MathTypeX.Interop.ScanRequest
    $request.Items = [MathTypeX.Interop.ScanItem[]]@($item)
    $protocol = [MathTypeX.Interop.EditorProtocol]
    # Invoke cần đối tượng .NET gốc, không phải PSObject mà New-Object bọc ngoài.
    $json = $protocol.GetMethod("Serialize").MakeGenericMethod([MathTypeX.Interop.ScanRequest]).Invoke($null, [object[]]@($request.PSObject.BaseObject))
    if ($json.Contains("`n")) { throw "một thông điệp JSON phải nằm trên một dòng" }
    $back = $protocol.GetMethod("Deserialize").MakeGenericMethod([MathTypeX.Interop.ScanRequest]).Invoke($null, [object[]]@([string]$json))
    if ($back.Items[0].Source -ne '$$E=mc^2$$' -or $back.Items[0].Context -ne $item.Context) { throw "giao thức pipe hỏng" }
    "JSON $($json.Length) ký tự"
}

if (-not $PortableOnly) { Step "editor-self-test" {
    $exe = Join-Path $PackageDir "editor\MathTypeX.Editor.exe"
    $report = Join-Path ([IO.Path]::GetTempPath()) ("mtx-self-test-" + [Guid]::NewGuid().ToString("N") + ".txt")
    $process = Start-Process -FilePath $exe -ArgumentList @("--self-test", "`"$report`"") -PassThru -WindowStyle Hidden
    $null = $process.Handle  # giữ handle để đọc được ExitCode sau khi tiến trình thoát
    if (-not $process.WaitForExit(180000)) {
        $process.Kill()
        throw "self-test chạy quá 3 phút"
    }
    if (Test-Path $report) { Get-Content $report -Encoding UTF8 | ForEach-Object { Write-Host "    $_" } }
    if ($process.ExitCode -ne 0) { throw "self-test thoát với mã $($process.ExitCode)" }
    if (-not (Select-String -Path $report -Pattern "RESULT PASS" -Quiet)) { throw "báo cáo không có RESULT PASS" }
    "MathTypeX.Editor.exe chạy được"
} }

if (-not $SkipInstall) {
    $clsid = "{5EBC7F71-F8F9-45E5-AC8E-54FED67797E1}"
    $windowsPowerShell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"

    Step "install" {
        & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PackageDir "install.ps1") -PackageDir $PackageDir | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "install.ps1 thoát với mã $LASTEXITCODE" }
        foreach ($view in @("Software\Classes", "Software\Classes\Wow6432Node")) {
            $codeBase = (Get-ItemProperty "HKCU:\$view\CLSID\$clsid\InprocServer32").CodeBase
            $dll = ([Uri]$codeBase).LocalPath
            if (-not (Test-Path $dll)) { throw "$view : CodeBase trỏ tới tệp không tồn tại: $dll" }
        }
        $loadBehavior = (Get-ItemProperty "HKCU:\Software\Microsoft\Office\Word\Addins\MathTypeX.WordAddin").LoadBehavior
        if ($loadBehavior -ne 3) { throw "LoadBehavior = $loadBehavior, cần 3" }
        $editorPath = (Get-ItemProperty "HKCU:\Software\MathTypeX").EditorPath
        if (-not (Test-Path $editorPath)) { throw "EditorPath không tồn tại: $editorPath" }
        "đã cài vào $(Split-Path (Split-Path $editorPath))"
    }

    # Word tạo add-in qua COM: ProgId → CLSID → mscoree.dll → CLR 4 → MathTypeX.WordAddin.Connect.
    # Làm y như vậy trong tiến trình 64-bit (Office 64-bit) và 32-bit (Office 32-bit, qua Wow6432Node).
    $activation = @'
$ErrorActionPreference = "Stop"
$addin = New-Object -ComObject MathTypeX.WordAddin
$xml = $addin.GetCustomUI("Microsoft.Word.Document")
if (-not $xml -or $xml.Length -lt 100) { throw "GetCustomUI trả về rỗng" }
"$([IntPtr]::Size * 8)-bit: tạo được add-in qua COM, ribbon $($xml.Length) ký tự"
'@
    $shells = @(
        @{ Name = "com-64bit"; Exe = $windowsPowerShell },
        @{ Name = "com-32bit"; Exe = (Join-Path $env:WINDIR "SysWOW64\WindowsPowerShell\v1.0\powershell.exe") }
    )
    foreach ($shell in $shells) {
        if (-not (Test-Path $shell.Exe)) {
            Write-Host "INFO $($shell.Name): máy không có $($shell.Exe), bỏ qua"
            continue
        }
        $exe = $shell.Exe
        Step $shell.Name { Invoke-PowerShell $exe $activation }
    }

    Step "uninstall" {
        & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PackageDir "uninstall.ps1") | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "uninstall.ps1 thoát với mã $LASTEXITCODE" }
        foreach ($key in @(
                "HKCU:\Software\Classes\CLSID\$clsid",
                "HKCU:\Software\Classes\Wow6432Node\CLSID\$clsid",
                "HKCU:\Software\Classes\MathTypeX.WordAddin",
                "HKCU:\Software\Microsoft\Office\Word\Addins\MathTypeX.WordAddin",
                "HKCU:\Software\MathTypeX")) {
            if (Test-Path $key) { throw "còn sót khoá $key" }
        }
        foreach ($dir in @("addin", "editor")) {
            if (Test-Path (Join-Path $env:LOCALAPPDATA "MathTypeX\$dir")) { throw "còn sót thư mục $dir" }
        }
        "gỡ sạch registry và tệp chương trình"
    }
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "$($failures.Count) bước thất bại:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host ""
Write-Host "Smoke test: tất cả các bước đều đạt." -ForegroundColor Green
