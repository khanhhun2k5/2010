# 07 — Lưu source LaTeX để sửa lại · Data model

## 12. Cách lưu source LaTeX để edit lại

### 12.1 Yêu cầu

| # | Yêu cầu | Mức đáp ứng |
|---|---|---|
| R1 | Nhấn `Alt+M` trên equation thì mở lại **đúng** source đã gõ (§20, §54 bước 8–13) | Bắt buộc |
| R2 | Vẫn đúng sau khi lưu, đóng, mở lại tài liệu | Bắt buộc |
| R3 | Vẫn đúng khi copy/paste trong cùng tài liệu | Bắt buộc |
| R4 | Copy sang tài liệu khác hoặc máy khác | Best-effort, luôn có phương án cuối là chuyển ngược |
| R5 | Không thêm "hack" nhìn thấy được, và không làm hỏng accessibility | Bắt buộc |
| R6 | Equation do Word hoặc công cụ khác tạo ra cũng sửa được bằng LaTeX | Rất nên có |

### 12.2 Data model: `EquationObject` (schema 1)

```jsonc
{
  "schema": 1,
  "id": "01JABCDXYZ7Q2M3N4P5R6S7T8V",          // ULID
  "originalLatex": "\\int_0^1 \\frac{x^2}{1+x^2}\\,dx",
  "normalizedLatex": "\\int_{0}^{1}\\frac{x^{2}}{1+x^{2}}\\,dx",
  "astVersion": 1,
  "astHash": "sha256:…",                        // hash của AST chuẩn hoá (không gồm font)
  "renderMode": { "requested": "Auto", "effective": "Native", "reasons": [] },
  "displayMode": "Inline",                      // Inline | Display
  "numbering": { "mode": "None", "tag": null, "label": null },
  "typography": {
    "presetId": "academic-vn",
    "snapshot": { "mainMath": "XITS Math", "slots": { "integral": "Latin Modern Math" }, "integralSizing": "TeX" }
  },                                            // lưu snapshot để đổi preset sau này không làm đổi công thức cũ
  "fontSize": { "mode": "FollowText", "resolvedPt": 13 },
  "macros": { "\\R": "\\mathbb{R}" },           // chỉ những macro đã dùng ⇒ mở được trên máy khác
  "tex": null,                                  // khi Exact: { engine, templateId, templateVersion, packages[] }
  "outputs": { "ommlKey": "k1:…", "svgHash": null },
  "locationHint": { "before": "h:…", "after": "h:…", "storyType": "Main" }, // hash ~32 ký tự trước/sau lúc chèn
  "origin": {                                   // khi được convert từ text trong tài liệu
    "kind": "Converted", "delimiter": "$", "originalText": "$\\int_0^1 …$"
  },
  "createdAt": "2026-10-06T08:00:00Z",
  "updatedAt": "2026-10-06T08:00:00Z",
  "appVersion": "0.1.0",
  "engineVersion": { "parser": "0.1.0", "omml": "0.1.0" }
}
```

Mọi trường đều serialize sang JSON (`System.Text.Json`, có source generator). Mỗi lần đổi schema phải có hàm migrate `vN → vN+1` và test đi kèm.

### 12.3 Bốn tầng lưu trữ

| Tầng | Nơi lưu | Vai trò |
|---|---|---|
| **L1: Kho trong tài liệu** | Word: một `CustomXMLPart` với namespace `urn:mathtypex:equations:v1`, chứa `<store schema="1"><eq id="…" key="…">{JSON, gzip+base64 nếu dài}</eq>…</store>`. PowerPoint: `Shape.Tags` | Nguồn chính. Đi theo file `.docx`/`.pptx` |
| **L2: Neo** (gắn object trong tài liệu với bản ghi trong kho) | **Native (OMML):** *khoá chuẩn hoá* `k1:SHA-256(Canon(OMML → AST))`, rút gọn 128 bit. **Không chèn gì thêm** vào công thức. **Vector:** `wp:docPr/@descr` (alt text) là LaTeX hoặc câu đọc; `wp:docPr/@name` = `MTX:<id>` ⚠ cần kiểm tra Word có giữ lại không; nếu không thì bọc trong Content Control ẩn có `Tag = "MTX:<id>"` | Tìm lại bản ghi |
| **L3: Kho cục bộ** | `%LOCALAPPDATA%\MathTypeX\equations.db` (SQLite), cùng khoá như L1. Người dùng tắt hoặc xoá được | Cứu trường hợp copy công thức sang tài liệu khác trên cùng máy |
| **L4: Chuyển ngược** | OMML → AST → LaTeX (`OmmlReader` + `LatexPrinter`) | Lưới an toàn cuối cùng. Đồng thời đáp ứng R6 |

**Vì sao không dùng Content Control cho mọi equation native?**

- Hàng nghìn Content Control làm chậm và rườm rà tài liệu.
- Chúng ảnh hưởng tới việc di chuyển con trỏ và gõ chữ sát công thức.
- Khi copy/paste, Content Control và CustomXMLPart không đi cùng nhau.

**Vì sao khoá được tính từ AST mà không băm thẳng XML?** Word **ghi lại OMML theo cách của nó**: thêm `w:rPr`, `m:ctrlPr`, rsid, đổi thứ tự thuộc tính. Băm XML sẽ hỏng ngay sau lần lưu đầu tiên. Ngược lại, `Canon(OMML→AST)` ổn định trước mọi cách ghi lại đó. Font của từng run được đưa vào khoá, vì hai equation giống nội dung nhưng khác font là hai equation khác nhau.

Hai equation có OMML giống hệt nhau thì dùng chung một khoá và cùng trỏ tới một source. Kết quả vẫn đúng, vì chúng tương đương nhau. Nếu có nhiều bản ghi trùng khoá thì lấy bản `updatedAt` mới nhất.

### 12.4 Luồng sửa lại và các trường hợp

```
Alt+M trên equation:
  omml = oMath.Range.WordOpenXML ⇒ trích m:oMath
  ast' = OmmlReader.Read(omml); key = Canon(ast')
  eq   = L1.Find(key) ?? L3.Find(key)
  if eq != null:                     mở editor với eq.originalLatex          (trường hợp ①)
  else if L1 có bản ghi với locationHint khớp ngữ cảnh hiện tại (text trước/sau) nhưng khác key:
                                     hỏi người dùng                          (trường hợp ②)
  else:                              latex = LatexPrinter(ast'); mở editor, kèm nhãn "chuyển ngược"   (③)
```

| Trường hợp | Tình huống | Hành vi |
|---|---|---|
| ① | Khớp | Mở **đúng** source gốc, kể cả khoảng trắng và macro người dùng đã gõ |
| ② | Người dùng đã sửa trực tiếp equation trong Word sau khi chèn | *"Công thức đã bị sửa trực tiếp trong Word. [Dùng bản hiện tại (chuyển ngược)] [Khôi phục source gốc]"* |
| ③ | Equation do Word hoặc công cụ khác tạo, hoặc dán từ máy khác | Chuyển ngược thành LaTeX chuẩn hoá. Sau khi người dùng lưu, equation trở thành equation của MathTypeX |
| Vector (SVG/EMF) không còn metadata | — | Đọc LaTeX từ alt text nếu có. Nếu không có: báo "Không có source; ảnh không thể chuyển ngược", và **không bao giờ** đoán ngược từ hình ảnh |

Sau khi người dùng sửa: thay equation trong `UndoRecord`, cập nhật bản ghi L1 (key mới, `updatedAt`), cập nhật L3.

### 12.5 Bảo đảm round-trip (test)

- **Property test:** với mọi AST sinh ngẫu nhiên trong tập con hỗ trợ native: `OmmlReader.Read(OmmlWriter.Write(ast)) ≅ ast`, và `Canon` là bất biến.
- **Golden test:** OMML do **Word thật** ghi lại (lấy trong S1, sau khi lưu và mở lại) vẫn cho ra **cùng khoá** với OMML mà app đã chèn.
- **Bộ equation tạo bằng Word UI** (gõ UnicodeMath, gõ LaTeX trong Word 365, chèn từ Equation gallery) chuyển ngược ra LaTeX hợp lệ, dùng để đo độ phủ của R6.

### 12.6 Định dạng và môi trường làm mất metadata

| Tình huống | Hậu quả | Biện pháp |
|---|---|---|
| Lưu thành `.doc` (97–2003) | Equation thành ảnh; CustomXMLPart mất | Cảnh báo trước khi lưu. Kho L3 vẫn còn, nhưng ảnh không chuyển ngược được |
| Mở bằng Google Docs hoặc LibreOffice rồi lưu lại | ⚠ CustomXMLPart có thể mất; OMML có thể được giữ hoặc bị chuyển đổi | L4 (chuyển ngược) |
| Copy sang tài liệu khác | L1 không đi theo | L3 (cùng máy) hoặc L4 |
| Gửi file cho người khác | L1 đi cùng file | Mở lại được đầy đủ trên máy người nhận nếu họ cũng cài MathTypeX |

### 12.7 Quyền riêng tư

- L1 nằm **trong tài liệu của người dùng**, tức là người dùng kiểm soát.
- L3 nằm cục bộ, có nút "Xoá lịch sử công thức", và tắt được trong Settings → Privacy.
- Không tầng nào gửi dữ liệu ra mạng (§46).
- Metadata đọc từ tài liệu là **dữ liệu không tin cậy**: luôn parse lại; `tex.packages` được kiểm theo allowlist ([02 §5.9.7](02-stack-and-architecture.md#597-mô-hình-bảo-mật-33)).
