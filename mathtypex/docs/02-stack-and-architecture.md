# 02 — Stack công nghệ · Kiến trúc tổng thể

## 4. Đề xuất stack công nghệ

### 4.1 Tích hợp Office: VSTO add-in (C#, .NET Framework 4.8)

**Vì sao không chọn Office.js cho sản phẩm Windows-first:**

| Nhu cầu | Office.js | VSTO / COM |
|---|---|---|
| Phím tắt toàn cục khi đang gõ trong tài liệu | Chỉ có custom shortcut ở một số host/phiên bản, và add-in runtime phải đang chạy (⚠ khác nhau tuỳ host) | Hook bàn phím theo thread ✅ |
| Biết toạ độ con trỏ để đặt editor nổi ngay đó | ❌ không có API | `Window.GetPoint` ✅ |
| Can thiệp bộ gõ tiếng Việt | ❌ | ✅ (thực hiện trong tiến trình editor) |
| Chạy LuaLaTeX/pdfLaTeX, đọc font đã cài | ❌ (sandbox của trình duyệt) | ✅ |
| Chèn OMML | `insertOoxml` ✅ | `InsertXML` ✅ |
| Gộp undo | ❌ | `UndoRecord` ✅ |
| Chạy trên Mac/Web | ✅ | ❌ |

Kết luận: MVP dùng **VSTO**. Nếu sau này cần bản Mac/Web, có thể viết thêm một client Office.js dùng lại core (ví dụ biên dịch core sang WASM). Ghi nhận điều này như một hướng phát triển, không làm trong roadmap hiện tại.

**Vì sao không nạp .NET 8/10 in-proc qua COM hosting?** Microsoft không hỗ trợ cách này cho Office add-in. Mỗi tiến trình chỉ nạp được **một** runtime .NET (Core); add-in nào nạp trước sẽ quyết định phiên bản, nên add-in của bên thứ ba có thể làm add-in của mình hỏng. Vì vậy phần chạy trong tiến trình Office phải là **net48**, và phải thật mỏng.

### 4.2 Bảng stack

| Lớp | Lựa chọn | Lý do |
|---|---|---|
| Add-in Word / PowerPoint | VSTO, C#, **.NET Framework 4.8** | Chỉ framework này chạy được in-proc trong Office |
| Editor, UI, preview, core | `MathTypeX.Editor.exe`: **.NET 10 (LTS, hỗ trợ đến 11/2028)**, **WPF**, build ReadyToRun | .NET 8 hết hỗ trợ ngày 10/11/2026. WPF đã chín muồi, khởi động nhanh, có UIA tốt, hỗ trợ Per-Monitor DPI v2, có theme Fluent (Light/Dark/System) từ .NET 9 |
| Ô soạn LaTeX | **AvalonEdit** (MIT) | Có syntax highlight, completion window, snippet kèm điều hướng Tab |
| Preview | **WebView2** chạy MathML Core | Chromium (từ bản 109) dựng MathML từ bảng OpenType MATH của **font đã cài** (qua HarfBuzz) và tôn trọng `font-family` của từng phần tử. Office 365 vốn đã kèm WebView2 Runtime |
| Thư viện core | C#, target **`netstandard2.0` + `net10.0`** (dùng PolySharp để có cú pháp C# mới trên netstandard2.0) | Dùng chung cho add-in (net48), editor, CLI và test |
| Font | Bộ đọc OpenType tự viết, chỉ đọc: sfnt/TTC, `cmap`, `MATH`, `name`, `OS/2`, `head` | Nhỏ (khoảng 2k LOC), kiểm soát được hoàn toàn, không phụ thuộc thư viện ngoài |
| OOXML | `DocumentFormat.OpenXml` (Open XML SDK) | Sinh docx/pptx tạm và tài liệu test |
| IPC | Named pipe theo từng người dùng (có ACL), giao thức **StreamJsonRpc** | Có thư viện cho cả net48 lẫn net10; độ trễ dưới 1 ms |
| TeX | TeX Live 2025+ hoặc MiKTeX; **LuaLaTeX** + `unicode-math` (font OTF), **pdfLaTeX** (package cũ), tuỳ chọn XeLaTeX; **dvisvgm** | Xem [04](04-fonts-and-integrals.md) |
| SVG → EMF | Tự chuyển hình học path sang EMF+ (GDI+ `Metafile`) | Chủ động hỗ trợ Office 2016, không phụ thuộc Inkscape |
| Lưu trữ | `%APPDATA%\MathTypeX` (profile, snippet, macro, keymap: JSON, đi theo roaming); `%LOCALAPPDATA%\MathTypeX` (font cache, render cache, thống kê, kho equation: SQLite qua `Microsoft.Data.Sqlite`) | |
| Bộ cài | **WiX** tạo MSI per-user (không cần admin), kiểm tra VSTO Runtime và WebView2, tuỳ chọn cài font OFL/GFL cho người dùng hiện tại | |
| Ký số | Authenticode cho DLL, EXE, manifest VSTO và MSI | VSTO trust, SmartScreen, EDR |
| Test | xUnit, **Verify** (snapshot), kiểm tra OMML bằng XSD ECMA-376, FsCheck (property-based), SharpFuzz | |
| CI | GitHub Actions `windows-latest` (build và unit test); runner Windows **tự host có Office** (integration và visual test) | Microsoft không hỗ trợ tự động hoá Office trên server, nên cần một VM tương tác riêng cho việc test |

### 4.3 Vì sao chọn WPF thay vì WinUI 3

- WinUI 3 không chạy in-proc trên .NET Framework, và đóng gói unpackaged phức tạp hơn.
- WinUI 3 không có trình soạn thảo tương đương AvalonEdit.
- API cửa sổ dạng tool window (popup không viền, định vị theo pixel, không hiện trên taskbar) của WinUI 3 kém linh hoạt hơn WPF.
- WPF với theme Fluent (.NET 9+) đã cho giao diện hiện đại và theo được Light/Dark của hệ thống.

---

## 5. Kiến trúc tổng thể

### 5.1 Mô hình tiến trình

```
┌──────────────────── WINWORD.EXE / POWERPNT.EXE (Office STA) ────────────────────┐
│ MathTypeX.WordAddin / PowerPointAddin  (VSTO, net48 — MỎNG)                       │
│  ├─ Ribbon + context menu                                                         │
│  ├─ KeyboardHook (WH_KEYBOARD, chỉ thread UI của Office)                          │
│  ├─ OfficeGateway: insert/replace/read OMML, ảnh vector, field, bookmark          │
│  ├─ DocumentEquationStore (CustomXMLPart / Shape.Tags)                            │
│  ├─ DocumentScanner (dùng thư viện MathTypeX.Scanner – netstandard2.0)            │
│  └─ EditorClient (StreamJsonRpc)                                                  │
└───────────────────────────────┬──────────────────────────────────────────────────┘
                                │ named pipe \\.\pipe\MathTypeX-<SID> (ACL: chỉ user)
┌───────────────────────────────▼──────────────────────────────────────────────────┐
│ MathTypeX.Editor.exe (.NET 10, WPF) — 1 tiến trình/người dùng, nạp sẵn, ẩn        │
│  ├─ UI: Floating editor · Command palette · Library · Matrix editor · Settings    │
│  ├─ PreviewHost (WebView2, MathML Core; tạm dừng khi ẩn)                          │
│  ├─ Pipeline: Tokenizer → MacroExpander → Parser → AST → Normalizer               │
│  │            → TypographyResolver → BackendSelector → Renderers                  │
│  ├─ FontService (scan + cache + diagnostics)                                      │
│  ├─ Catalog / Snippets / Macros / Learning (thống kê cục bộ)                      │
│  ├─ MathInputShield (bảo vệ khỏi bộ gõ, xem 06)                                   │
│  └─ TexBroker ─────────────────┐                                                  │
└────────────────────────────────┼─────────────────────────────────────────────────┘
                                 │ CreateProcessAsUser (token Low IL) + Job Object
                ┌────────────────▼─────────────────┐
                │ MathTypeX.TexWorker.exe (net10)  │──► lualatex / pdflatex ──► dvisvgm
                │ thư mục tạm riêng · timeout · RAM│      (không shell-escape, xem §5.9)
                └──────────────────────────────────┘
```

**Vì sao chạy editor ngoài tiến trình Office (quyết định D2):**

1. **Dùng được .NET 10** cho phần lớn mã nguồn, như yêu cầu "`.NET 8+`".
2. **Office tự vô hiệu hoá add-in khởi động chậm hoặc hay crash** (cơ chế resiliency). Add-in chỉ còn vài trăm KB và không nạp WPF hay WebView2, nên khởi động dưới 50 ms.
3. **Bộ gõ tiếng Việt (EVKey, OpenKey…) loại trừ được theo tên tiến trình.** Nếu editor nằm chung trong `WINWORD.EXE` thì loại trừ editor đồng nghĩa với loại trừ luôn Word.
4. **Một editor phục vụ cả Word lẫn PowerPoint**, dùng chung font cache và render cache.
5. **Crash cô lập:** lỗi ở WebView2 hay TeX không kéo Word sập theo.

**Cái giá phải trả** (được kiểm chứng bằng spike S3): phải chuyển focus qua lại giữa hai tiến trình (`AllowSetForegroundWindow`), phải tự quản lý vị trí cửa sổ, và tốn thêm khoảng 150 MB RAM cho tiến trình nền (giảm được bằng `CoreWebView2.TrySuspendAsync()` khi editor ẩn).

**Phương án dự phòng:** thư viện core target `netstandard2.0`, nên nếu S3 thất bại có thể chạy toàn bộ editor WPF **in-proc trên net48**, trên một thread STA riêng có `Dispatcher.Run()`. Khi đó chỉ mất ưu điểm 1 và 3.

### 5.2 Luồng A: chèn công thức mới

```
Word (thread UI)                         Editor.exe                          TexWorker
─────────────────                        ──────────                          ─────────
Alt+M (hook) ─► chụp context:
  selection, font/size tại caret,
  rect caret (GetPoint), story,
  trong bảng?, CompatibilityMode
AllowSetForegroundWindow(editorPid)
RPC OpenEditor(ctx) ───────────────────► hiện cửa sổ tại caret (<100 ms khi đã warm)
(không chờ – trả lại UI cho Word)        gõ ─► parse ─► AST ─► MathML ─► preview (≤50 ms)
                                         [Exact] ─────────────────────────► compile (nền)
                                                                    ◄──────── SVG + metrics
                                         Enter ─► EquationObject + RenderResult
◄─ RPC InsertEquation(req) ───────────── (Editor gọi ngược vào add-in)
post sang thread UI (SynchronizationContext)
UndoRecord.Start ─► InsertXML ─► ghi store ─► UndoRecord.End
trả kết quả ──────────────────────────► đóng cửa sổ, SetForegroundWindow(Word)
```

Quy tắc luồng trong add-in:

- **Mọi lệnh gọi Office Object Model chỉ chạy trên thread UI của Office**, được post qua `SynchronizationContext` chụp lúc khởi động.
- **Không bao giờ chặn thread UI để chờ RPC.**
- Mọi thao tác `Start/EndCustomRecord` được bọc trong `try/finally`.

### 5.3 Luồng B: sửa công thức đã có

Đặt con trỏ trong equation rồi nhấn `Alt+M`:

1. Add-in lấy `Selection.OMaths(1)` và đọc OMML.
2. Tính khoá chuẩn hoá (canonical key) và tra trong store.
3. Gửi `EquationObject` sang editor. Nếu không tìm thấy, gửi OMML để editor chuyển ngược thành LaTeX.
4. Người dùng sửa xong, add-in thay thế equation bằng `OMath.Range.InsertXML(…)` trong một `UndoRecord`.

Chi tiết xem [07](07-equation-metadata.md).

### 5.4 Luồng C: Convert LaTeX trong vùng chọn hoặc cả tài liệu

1. Add-in đọc text một lần (`TextRetrievalMode` gồm cả hidden text và field code), chạy `Scanner` in-proc (thư viện netstandard2.0), thu được danh sách ứng viên kèm độ tin cậy.
2. Với lệnh *Scan Document*, editor hiện danh sách để người dùng chọn: Convert selected / Convert all / Ignore / Thêm vào ignore list.
3. Add-in gửi một lần RPC `RenderBatch(latex[])` và nhận lại `RenderResult[]`.
4. Add-in tắt `ScreenUpdating`, mở `UndoRecord`, xử lý **từ cuối lên đầu**, với mỗi ứng viên thì kiểm tra `doc.Range(s,e).Text` có đúng chuỗi mong đợi không rồi mới thay. Cuối cùng đóng `UndoRecord`.

### 5.5 Pipeline render bên trong editor

```
source ─► Tokenizer ─► MacroExpander ─► Parser ─► AST + Diagnostics
                                                  │
                                                  ▼
                                            Normalizer (các pass, xem 03)
                                                  │
                         ┌────────────────────────┼─────────────────────────┐
                         ▼                        ▼                         ▼
                 TypographyResolver        BackendSelector          LatexPrinter (normalized)
                 (StyleMap: NodeId→Font)   (CapabilityMatrix)
                         │                        │
        ┌────────────────┼──────────────┬─────────┴─────────┬──────────────────┐
        ▼                ▼              ▼                   ▼                  ▼
  MathMlRenderer   OmmlRenderer   TexRenderer(job)   UnicodeMathRenderer   SpeechRenderer
  (preview,        (Native)       (Exact: .tex →     (clipboard)           (alt text,
   clipboard)                      worker → SVG/EMF)                        a11y)
```

Ở chế độ Exact, preview MathML được hiển thị ngay (gắn nhãn "nháp"). Khi SVG từ TeX về thì thay vào; nếu cache trúng thì có ngay.

### 5.6 Quy tắc phân lớp

Các quy tắc sau được kiểm tra tự động bằng architecture test (NetArchTest):

- `Ast` không phụ thuộc vào bất kỳ project nào khác.
- `Parser` chỉ phụ thuộc `Ast` và `Core`.
- Các `Render.*` chỉ phụ thuộc `Ast`, `Typography` và `Core`; **không renderer nào tham chiếu Office**.
- Các add-in chỉ phụ thuộc `Interop.Contracts`, `Scanner`, `Core` (và `Render.Omml` cho đường chuyển ngược).
- `TexWorker` không phụ thuộc UI.

### 5.7 Ngân sách hiệu năng (đo trong CI bằng BenchmarkDotNet và test tích hợp)

| Thao tác | Mục tiêu p95 |
|---|---|
| Add-in khởi động cùng Office | < 50 ms (không nạp WPF/WebView2) |
| `Alt+M` → editor hiện (editor đã warm) | < 100 ms |
| Parse + MathML + cập nhật preview (công thức dài 500 ký tự) | < 30 ms (debounce 30–80 ms tuỳ độ dài) |
| Autocomplete | < 20 ms |
| Chèn equation native | < 150 ms |
| Exact khi cache trúng | < 20 ms; khi chạy lạnh 0,3–1,5 s, chạy nền |
| Scan tài liệu 300 trang | < 3 s cho bước scan; bước convert tuỳ số lượng, có thanh tiến trình và nút huỷ |

### 5.8 Tính bền vững

- Editor chết giữa chừng: add-in phát hiện qua pipe, khởi động lại editor và báo nhẹ cho người dùng. Văn bản đang soạn được autosave mỗi 2 giây vào `%LOCALAPPDATA%`.
- Word đang mở hộp thoại (modal): lệnh insert được xếp hàng và chạy khi Word rảnh; editor hiển thị trạng thái "Đang chờ Word…".
- Thêm workaround nào trong add-in cũng phải đo lại thời gian khởi động, để Office không xếp add-in vào diện "chậm".

### 5.9 Kiến trúc TeX backend và mô hình bảo mật

#### 5.9.1 Phát hiện bản cài TeX

- Tìm theo thứ tự: biến `PATH` → registry của MiKTeX → `C:\texlive\<năm>\bin\windows` (từ TeX Live 2023 trở đi; các bản trước dùng `bin\win32`) → đường dẫn người dùng chỉ định.
- Ghi lại phiên bản engine, và kiểm tra package có sẵn bằng `kpsewhich` (ví dụ `unicode-math.sty`, `newtxmath.sty`, `mathdesign.sty`).
- Kết quả được lưu vào cache, chỉ phát hiện lại khi người dùng yêu cầu hoặc khi đường dẫn thay đổi.

#### 5.9.2 Mô hình job

```
TexJob {
  Engine        : LuaLaTeX | PdfLaTeX | XeLaTeX
  TemplateId    : "otf-unicode-math@3" | "legacy-newtx@2" | ...   // preamble do app sinh, có version
  Body          : LatexPrinter(ast)       // KHÔNG phải source thô của người dùng
  FontSizePt, DisplayMode, TypographySnapshot
  Outputs       : Svg | Emf | Png(fallback) | Metrics
}
CacheKey = SHA-256(Body ‖ TemplateId ‖ TypographySnapshot ‖ EngineVersion ‖ FontSizePt ‖ DisplayMode)
```

#### 5.9.3 Preamble

Preamble được sinh từ typography profile, ví dụ:

```latex
\documentclass{article}
\usepackage{amsmath,amssymb}
\usepackage{unicode-math}
\setmathfont{XITS Math}
\setmathfont{Latin Modern Math}[range={"222B-"2233}]   % ∫ ∬ ∭ ∮ ∯ ∰ ∱ ∲ ∳
\begin{document}
\setbox0=\hbox{$\displaystyle <BODY>$}
% ghi \wd0, \ht0, \dp0 ra file metrics trong thư mục job (openout_any=p cho phép)
\shipout\box0
\end{document}
```

- **Metrics** (chiều rộng, chiều cao, độ sâu) dùng để đặt baseline trong Word.
- **Lưu ý đơn vị:** 1 pt của TeX bằng 72/72,27 bp. Word dùng bp (PostScript point). Phải quy đổi, nếu không sẽ lệch tỉ lệ khoảng 0,37%.

#### 5.9.4 Chuyển đổi đầu ra

| Engine | Đường đi | Ghi chú |
|---|---|---|
| pdfLaTeX (package cũ) | Chế độ DVI → `dvisvgm --no-fonts --exact-bbox` | Glyph được chuyển thành path. ⚠ Có cần biến thể không dùng `<use>` cho Office hay không: S7 |
| XeLaTeX | `-no-pdf` → XDV → `dvisvgm` | dvisvgm đọc được XDV |
| LuaLaTeX | PDF → `dvisvgm --pdf`, hoặc DVI có font OTF | ⚠ Chọn đường nào sau S7 |
| Tất cả | SVG → EMF+ (bộ chuyển của app) → PNG fallback (raster từ EMF bằng GDI+) | PNG chỉ là fallback mà OOXML bắt buộc phải có |

#### 5.9.5 Tốc độ

- Format dựng sẵn cho từng template pdfLaTeX (`mylatexformat`).
- Cache tên font của `luaotfload` được dựng sẵn từ lúc cài đặt.
- Nghiên cứu ở Phase 2: worker LuaTeX chạy lâu dài, nhận snippet qua stdin và trích node bằng Lua.

#### 5.9.6 Báo lỗi

1. Đọc log TeX (`! Undefined control sequence.` / `l.12 …`).
2. Ánh xạ ngược qua *source map* theo chuỗi: dòng trong file `.tex` sinh ra → node AST → span trên source của người dùng.
3. Hiện thông báo thân thiện, ví dụ "Dòng 1: thiếu `}` sau `\frac`". Log đầy đủ nằm sau nút *Technical details*.

#### 5.9.7 Mô hình bảo mật (§33)

| Lớp | Biện pháp |
|---|---|
| 1. Đầu vào | TeX **chỉ** nhận đầu ra của `LatexPrinter` sinh từ AST, mà AST chỉ chứa các construct nằm trong whitelist. Macro của người dùng đã được app tự mở rộng trước đó, nên TeX không bao giờ thấy `\input`, `\write18`, `\directlua`, `\catcode`, `\csname`, `\def`, `\openout`… Chế độ "raw TeX" (dành cho người dùng nâng cao, **mặc định tắt**) có thêm bộ kiểm tra token: denylist, **cấm ký pháp `^^`** (vì `^^5c` chính là `\` và qua mặt được denylist ngây thơ), và chỉ cho phép package trong allowlist. |
| 2. Cờ engine | `-no-shell-escape`, `-interaction=nonstopmode`, `-halt-on-error`, `-file-line-error`. LuaTeX thêm `--safer --nosocket` (⚠ S7: kiểm tra tương thích với `luaotfload` khi cache đã dựng sẵn). Biến kpathsea: `openin_any=p`, `openout_any=p`, `shell_escape=f`. MiKTeX: `--disable-installer` và `[MPM]AutoInstall=0` (không tự tải package từ mạng). |
| 3. Tiến trình | `TexWorker` chạy với **token Low Integrity** (restricted token), nên không ghi được vào file Medium IL của người dùng. Thư mục làm việc: `%LOCALAPPDATA%Low\MathTypeX\jobs\<id>`. **Job Object**: kill-on-close, giới hạn RAM 512 MB, CPU 10 giây, giới hạn số tiến trình con, giới hạn UI (không đọc/ghi clipboard, không đổi desktop, không dùng global atom). |
| 4. Dọn dẹp | Xoá thư mục job ngay khi xong; định kỳ dọn những gì còn sót lại. |
| 5. Tài liệu là dữ liệu không tin cậy | **Mở tài liệu không bao giờ kích hoạt TeX.** Chỉ render lại khi người dùng chủ động (Edit / Re-render). Metadata đọc từ tài liệu luôn được parse lại; trường `packages` và `template` được kiểm tra theo allowlist; không bao giờ dùng nguyên văn preamble lưu trong file. |
| 6. Phase 3 | AppContainer không có capability mạng (cần cấp ACL đọc cho thư mục TeX). |
