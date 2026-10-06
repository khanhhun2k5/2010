# 09 — Roadmap MVP theo tuần (kèm danh sách spike)

**Giả định:** 2 developer (Dev-O phụ trách Office/Windows, Dev-C phụ trách core/typography), cộng một người bán thời gian lo QA và UX. Nếu chỉ có 1 developer thì nhân thời gian với khoảng 1,7.

**Tuần nào cũng phải kết thúc bằng một vertical slice** build được, có test và có kịch bản demo (theo §55).

## Spikes

Spike là thí nghiệm có thời hạn, cho ra **câu trả lời** chứ không cho ra code để giữ lại. Mỗi spike có báo cáo tại `docs/spikes/`.

| ID | Câu hỏi | Phương pháp | Tiêu chí ra quyết định | Chặn việc gì |
|---|---|---|---|---|
| **S1** | `InsertXML` với OMML: có gộp vào đoạn văn hiện tại khi inline không, có giữ định dạng xung quanh không? Hoạt động ra sao trong bảng, footnote, header, text box, khi bật Track Changes? Đọc lại OMML, `UndoRecord`, cách Word biểu diễn `&` trong `m:eqArr`, và Word ghi lại OMML thế nào sau khi lưu/mở lại? | Add-in thử nghiệm, kèm bộ tài liệu mẫu | Chèn inline không làm vỡ đoạn văn; một thao tác chỉ sinh một mục Undo; thu được bộ golden OMML | Toàn bộ backend Native |
| **S2** | Word có tôn trọng `w:rFonts` của từng `m:r` khi dùng font toán khác Cambria không? Font của ký tự n-ary và delimiter (`m:ctrlPr`) có được giữ không? `m:grow` hoạt động thế nào với từng font? `Document.OMathFontName` làm gì? Font cài per-user có được thấy không? Font toán có nhúng được vào file không? | `mtx docx` sinh tài liệu ma trận (font × cấu trúc), mở trong Word thật, xuất PDF, đo đạc | Sinh được `capabilities/word-*.json` đầu tiên | Font selector, `Auto`, preset |
| **S3** | Mô hình hai tiến trình: độ trễ từ `Alt+M` tới khi editor hiện, chuyển focus, vị trí lấy từ `GetPoint`, hook theo thread, KeyTips khi nhả Alt, class của cửa sổ tài liệu (`_WwG`, `paneClassDC`) | Nguyên mẫu tối thiểu | Editor hiện dưới 100 ms khi đã warm; focus trả về đúng; không bật KeyTips | Quyết định D2 (nếu thất bại thì chuyển sang in-proc net48) |
| **S4** | Preview bằng WebView2 + MathML Core với XITS, LM, STIX, Cambria: độ trễ, bộ nhớ, prewarm, `TrySuspendAsync`, độ giống so với Word | Nguyên mẫu, cộng so sánh trực quan | Cập nhật preview dưới 30 ms; sai khác so với Word ở mức chấp nhận được | Quyết định D4 |
| **S5** | PowerPoint: các phương án P1, P2, P3 để chèn equation native; `TextRange2.MathZones`; Tags có đi theo khi copy shape không; giữ animation khi thay ảnh | Nguyên mẫu | Có ít nhất một cách chèn native ổn định | Phase 2 PowerPoint |
| **S6** | Bộ gõ tiếng Việt: L0, L1, L2 với UniKey, EVKey, OpenKey, Windows Telex | Ma trận test ở [06 §11.8](06-keyboard-and-ime.md) | MATH nhận đúng 100% ký tự; VI gõ được bình thường | Cấu hình mặc định của Shield |
| **S7** | Pipeline TeX: LuaLaTeX/pdfLaTeX → dvisvgm. Office hỗ trợ tập con SVG nào (`<use>`, path)? Chuyển sang EMF; đo metrics và baseline; `--safer` có chạy được với `luaotfload` không; thời gian chạy | Script cộng tài liệu thử | SVG hiển thị đúng trong Word và PowerPoint; baseline lệch không quá 0,5 pt | Phase 2 Exact |
| **S8** | Chế độ `Grow` cho ∫ trong LuaTeX (`\Udelimiter`); `unicode-math` dùng `range=` cho từng slot | `.tex` thử nghiệm | Ra được ∫ co giãn đúng kiểu | Typography nâng cao |
| **S9** | Đánh số: cơ chế `#` của Word 365 so với bảng 3 cột; field SEQ/REF; đánh số theo chương | Tài liệu thử | Chọn được phương án mặc định | Phase 3 |
| **S10** | Clipboard: RTF có math, MathML, `image/svg+xml`. Word và PowerPoint nhận định dạng nào? | Nguyên mẫu | Có bảng định dạng dùng được | Copy/Paste |
| **S11** | Dark mode của Word: equation OMML và ảnh SVG/EMF hiển thị thế nào? | Thử thủ công | Ghi lại hành vi để làm UX cảnh báo | Thông điệp trong UI |

---

## Phase 1: MVP chạy được (tuần 1 → 12)

| Tuần | Slice / công việc | Demo cuối tuần | Test |
|---|---|---|---|
| **1** | Khung repo và CI (build, unit test trên `windows-latest`); viết ADR D1–D10. **Dev-O:** S1, S3. **Dev-C:** S4, chuẩn bị S2 | Báo cáo S1, S3, S4 | — |
| **2** | **VS-1: core headless** ([11](11-first-vertical-slice.md)): tokenizer, tập con parser, AST, OmmlWriter, `mtx docx`. Chạy S2 bằng `mtx docx` | Mở `integrals-demo.docx` trong Word | Unit test parser; golden OMML; kiểm tra XSD; fuzz smoke |
| **3** | **VS-2: chèn vào Word.** Add-in VSTO, hook `Alt+M`, editor tối giản (`TextBox`), IPC, `InsertXML` + `UndoRecord`, font Cambria | §54 bước 1–3, 6–7 (chưa có preview, chưa chọn font) | Integration test (runner có Office) |
| **4** | **VS-3: preview và font.** WebView2 MathML, debounce, FontScanner + cache, font selector (Cambria / XITS / LM), cài font đóng gói kèm per-user | §54 bước 1–7 đầy đủ | Test font reader; benchmark preview |
| **5** | **VS-4: sửa lại.** EquationObject, kho CustomXMLPart, `Canon`/khoá, OmmlReader cho tập con, thay thế equation | **§54 bước 1–13 hoàn chỉnh** | Round-trip property test; golden OMML sau khi lưu/mở lại |
| **6** | **VS-5: mở rộng parser.** Delimiter, accent, hàm, `\lim`, ma trận, `cases`, `aligned` (không đánh số), `\text` tiếng Việt, spacing, error recovery, thông điệp vi | Bộ §50 render đúng ở Native | Hơn 400 ca parser; golden OMML |
| **7** | **VS-6: autocomplete và placeholder.** Catalog v1 (khoảng 400 lệnh), fuzzy search, điều hướng Tab/Shift+Tab, `\int_{□}^{□} □\,d□` | Gõ `\fra` → `Tab` → điền tử → `Tab` → mẫu → `Tab` | Đo độ trễ autocomplete; test thứ tự tab-stop |
| **8** | **VS-7: command palette và Beginner.** Tìm vi/en có gập dấu; panel Beginner (cú pháp, ví dụ, preview); lỗi theo ArgRole | Gõ "phân số", "tích phân" → chèn | Test fuzzy vi/en |
| **9** | **VS-8: Convert Selection.** Scanner (quy tắc `$` kiểu Pandoc, chấm điểm), tách đoạn khi có display, `\(\)`, `\[\]`, `equation*`, `align*` | §54 phần 2 trên vùng chọn | Hơn 200 ca scanner (`$100`, `\$`, code, đường dẫn) |
| **10** | **VS-9: Convert Document.** Duyệt mọi story, hộp Scan (chọn / bỏ qua / ignore list), một UndoRecord, thanh tiến trình và Huỷ, Revert | Tài liệu 300 trang | Benchmark; test Undo |
| **11** | **VS-10: Settings và bàn phím.** Settings cơ bản có tìm kiếm, keymap, phát hiện xung đột (`FindKey` + bảng tĩnh), phím chuyển inline/display, IME L0 + L0' + L1, theme Light/Dark/System | Đổi phím; gõ `\cos` khi đang bật UniKey | Ma trận S6 (một phần) |
| **12** | **Củng cố:** ngân sách hiệu năng, fuzz dài, pass accessibility (UIA, bàn phím, phóng chữ), bộ cài WiX per-user, ký số, tài liệu người dùng (vi). **Beta với 5–10 giáo viên/sinh viên** | Cài từ MSI trên máy sạch, chạy toàn bộ §54 | Regression ảnh §50/§51 (Native) |

**Tiêu chí kết thúc Phase 1:**

- §54 đạt trên M365 x64 và x86.
- Không có crash nào trong 8 giờ dùng beta.
- Đạt mọi ngân sách hiệu năng ở [02 §5.7](02-stack-and-architecture.md#57-ngân-sách-hiệu-năng-đo-trong-ci-bằng-benchmarkdotnet-và-test-tích-hợp).

## Phase 2 (tuần 13 → 26)

| Tuần | Nội dung |
|---|---|
| 13–14 | S5, S7, S8 → quyết định cho PowerPoint và Exact |
| 15–17 | **Exact backend:** TexWorker sandbox, template LuaLaTeX/pdfLaTeX, dvisvgm, SVG→EMF, metrics và baseline, cache, ánh xạ lỗi, `Auto` chạy theo CapabilityMatrix |
| 18–19 | **Advanced Typography:** font theo từng slot, preset legacy (`newtx`, `mathptmx`, `mathdesign`, `fourier`, `kpfonts`…), tham số ∫ (TeX/Grow/Scale), Font Diagnostics, Compare mode |
| 20–22 | **PowerPoint:** add-in, chèn native (theo S5) và SVG, Tags, sửa lại, vị trí "gần như inline" |
| 23 | **Matrix editor GUI**, Visual Formula Library đầy đủ 22 nhóm |
| 24 | **Snippet engine** (import/export, shortcut), Smart replacements (bật/tắt từng mục), IME L2 (Shield) |
| 25 | **Copy/Paste** đủ định dạng (theo S10), Paste as Math |
| 26 | Củng cố, beta mở rộng |

## Phase 3 (tuần 27 → 38)

| Nội dung |
|---|
| **Đánh số công thức** (theo S9), đánh số theo chương, `\label`/`\ref`/`\eqref` → bookmark và field REF, đánh số `align` theo từng dòng |
| **Batch conversion nâng cao:** Revert hàng loạt, quy tắc ignore theo style, báo cáo kết quả |
| **Macro Manager:** import/export file `.tex` |
| **Profiles:** import/export, đồng bộ qua thư mục do người dùng chọn |
| **Plugin architecture:** renderer và command plugin qua các contract trong `Interop.Contracts` (dùng `AssemblyLoadContext`, có ký số) |
| UnicodeMath → AST; Learning mode (Recently/Frequently/Favorites); telemetry **opt-in** |
| Gia cố: chạy TeX trong AppContainer; sơ đồ (`tikz-cd`) có whitelist |
