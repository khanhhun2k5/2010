# 08 — Cấu trúc repository

## 13. Repository structure

### 13.1 Về đặt tên

Yêu cầu gợi ý các module `Math.Core`, `Math.Parser`… Tôi đề xuất dùng tiền tố **`MathTypeX.*`**. Nếu namespace gốc là `Math`, thì mọi đoạn code nằm trong `namespace Math.Parser` mà gọi `Math.Max(…)` sẽ bị trình biên dịch hiểu `Math` là namespace chứ không phải `System.Math`, và báo lỗi. Đổi tên sau này rất tốn công, nên chốt ngay từ đầu.

Ánh xạ từ tên gợi ý sang tên đề xuất:

| Gợi ý trong yêu cầu | Đề xuất |
|---|---|
| `Math.Core` / `Math.AST` / `Math.Parser` | `MathTypeX.Core` / `MathTypeX.Ast` / `MathTypeX.Parser` |
| `Math.OMML` / `Math.TeX` | `MathTypeX.Render.Omml` / `MathTypeX.Render.Tex` + `MathTypeX.TexWorker` |
| `Math.Fonts` | `MathTypeX.Fonts` + `MathTypeX.Typography` |
| `Math.Office` / `Math.WordAddin` / `Math.PowerPointAddin` | `MathTypeX.Office.Common` / `MathTypeX.WordAddin` / `MathTypeX.PowerPointAddin` |
| `Math.UI` / `Math.Settings` / `Math.Snippets` | `MathTypeX.Editor` (WPF) / `MathTypeX.Settings` / `MathTypeX.Catalog` (gồm snippet, macro, command) |
| `Math.Tests` | Mỗi project có một project test riêng trong `tests/` |

### 13.2 Cây thư mục

Toàn bộ nằm trong `mathtypex/`, vì repo hiện còn chứa thứ khác.

```
mathtypex/
├─ README.md
├─ global.json                         # ghim .NET SDK 10.0.x
├─ Directory.Build.props               # LangVersion, Nullable, TreatWarningsAsErrors, analyzers
├─ Directory.Packages.props            # Central Package Management
├─ .editorconfig
├─ MathTypeX.sln
├─ docs/
│  ├─ 01…11-*.md                       # tài liệu thiết kế (thư mục này)
│  ├─ adr/                             # Architecture Decision Records (0001-process-model.md …)
│  └─ spikes/                          # báo cáo kết quả S1…S11
├─ assets/
│  ├─ catalog/commands.json            # nguồn duy nhất: lệnh, environment, snippet mặc định, tên vi/en
│  ├─ catalog/library.json             # Visual Formula Library (§9)
│  ├─ presets/*.json                   # Typography presets (§18)
│  ├─ keymaps/default.json
│  ├─ capabilities/word-*.json         # Native Capability Matrix (sinh từ S2 và test)
│  ├─ tex/templates/*.tex              # preamble template có version (đã whitelist)
│  └─ fonts/                           # font OFL/GFL đóng gói kèm + LICENSE từng font
├─ src/
│  ├─ MathTypeX.Core/                  # netstandard2.0;net10.0 — SourceSpan, Diagnostic, Result, hashing, ULID
│  ├─ MathTypeX.Ast/                   #   — node, visitor/rewriter, JSON, AstVersion
│  ├─ MathTypeX.Parser/                #   — Tokenizer, MacroExpander, Parser, Normalizer, LatexPrinter
│  ├─ MathTypeX.Fonts/                 #   — OpenType reader (sfnt/TTC/cmap/MATH), FontScanner, FontCache
│  ├─ MathTypeX.Typography/            #   — TypographyProfile, Presets, CharClassifier, TypographyResolver
│  ├─ MathTypeX.Render.Omml/           #   — OmmlWriter, OmmlReader (chuyển ngược), Canon/Key
│  ├─ MathTypeX.Render.MathMl/         #   — MathML Core (preview + clipboard)
│  ├─ MathTypeX.Render.UnicodeMath/    #   — AST→UnicodeMath (Phase 3: parser ngược)
│  ├─ MathTypeX.Render.Speech/         #   — câu đọc vi/en cho alt text, accessibility
│  ├─ MathTypeX.Render.Tex/            #   — TexJob, template, source map (KHÔNG chạy tiến trình)
│  ├─ MathTypeX.Backend/               #   — CapabilityMatrix, BackendSelector, RenderCache
│  ├─ MathTypeX.Catalog/               #   — commands, fuzzy search (gập dấu tiếng Việt), snippets, macros, learning
│  ├─ MathTypeX.Scanner/               #   — phát hiện LaTeX trong văn bản (quy tắc $ kiểu Pandoc, chấm điểm)
│  ├─ MathTypeX.Settings/              #   — model, lưu trữ, migrate, settings search index
│  ├─ MathTypeX.Interop.Contracts/     # netstandard2.0 — DTO và interface JSON-RPC giữa add-in và editor
│  ├─ MathTypeX.Editor/                # net10.0-windows, WPF — floating editor, palette, library, settings UI,
│  │                                   #   PreviewHost (WebView2), MathInputShield, TexBroker, IPC server
│  ├─ MathTypeX.TexWorker/             # net10.0 — sandbox (Job Object, Low IL), gọi engine, dvisvgm, SVG→EMF
│  ├─ MathTypeX.Office.Common/         # net48 — COM helpers, KeyboardHook, IPC client, dispatcher sang thread UI
│  ├─ MathTypeX.WordAddin/             # net48 VSTO — Ribbon, WordGateway, CustomXml store, convert
│  ├─ MathTypeX.PowerPointAddin/       # net48 VSTO — Phase 2
│  └─ MathTypeX.Cli/                   # net10.0 — `mtx omml|mathml|latex|docx|fonts|tex …` (test, golden, demo)
├─ tests/
│  ├─ MathTypeX.Parser.Tests/
│  ├─ MathTypeX.Render.Omml.Tests/     # Verify snapshot + kiểm tra XSD ECMA-376
│  ├─ MathTypeX.Fonts.Tests/           # font mẫu nhỏ (OFL) trong testdata
│  ├─ MathTypeX.Scanner.Tests/
│  ├─ MathTypeX.Catalog.Tests/
│  ├─ MathTypeX.Architecture.Tests/    # NetArchTest: luật phân lớp (02 §5.6)
│  ├─ MathTypeX.Benchmarks/            # BenchmarkDotNet: ngân sách hiệu năng (02 §5.7)
│  ├─ MathTypeX.Word.IntegrationTests/ # [Trait("Requires","Office")] — chỉ chạy trên runner có Office
│  ├─ corpus/                          # §50 typography.tex, §51 integrals.tex, đề thi mẫu (ẩn danh)
│  ├─ golden/omml/                     # OMML do Word thật sinh ra (S1) — dùng làm chuẩn đối chiếu
│  └─ visual/                          # ảnh tham chiếu + ngưỡng so sánh
├─ schemas/
│  ├─ ooxml/                           # XSD ECMA-376 (shared-math.xsd, wml.xsd…)
│  └─ mathtypex/*.schema.json          # EquationObject, Profile, Snippet, Keymap
├─ build/                              # script build, ký số, tạo MSI
└─ installer/                          # WiX v5+: MSI per-user, prerequisites (VSTO runtime, WebView2)
```

### 13.3 Target framework và quy tắc phụ thuộc

| Nhóm | Target framework | Được phép phụ thuộc vào |
|---|---|---|
| `Core`, `Ast` | `netstandard2.0;net10.0` | (không gì cả / chỉ `Core`) |
| `Parser`, `Fonts`, `Scanner`, `Catalog`, `Settings` | `netstandard2.0;net10.0` | `Core`, `Ast` |
| `Typography` | `netstandard2.0;net10.0` | `Fonts`, `Ast`, `Core` |
| `Render.*`, `Backend` | `netstandard2.0;net10.0` | `Ast`, `Typography`, `Core` (**không** tham chiếu Office hay UI) |
| `Interop.Contracts` | `netstandard2.0` | `Core` |
| `Editor`, `TexWorker`, `Cli` | `net10.0(-windows)` | Mọi thư viện core |
| `Office.Common`, `WordAddin`, `PowerPointAddin` | `net48` | `Interop.Contracts`, `Scanner`, `Core`, `Render.Omml` (chỉ phần đọc ngược và tính khoá) |

### 13.4 Quy ước

- `Nullable` bật, `TreatWarningsAsErrors` trong CI, dùng analyzer của .NET và StyleCop (cấu hình tối giản).
- Mọi chuỗi giao diện đặt trong `.resx` (vi, en). Tiếng Việt là ngôn ngữ mặc định.
- Mỗi spike có báo cáo tại `docs/spikes/Sx-*.md`, gồm: câu hỏi, phương pháp, kết quả, quyết định. Code của spike là **code vứt đi**, không merge vào `src/`.
- Các quyết định kiến trúc ghi thành ADR trong `docs/adr/`; ADR đầu tiên là D1–D10 trong README.
