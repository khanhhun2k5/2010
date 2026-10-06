# 04 — Chiến lược font · Chiến lược dấu tích phân

## 7. Chiến lược font

### 7.1 Hai "thế giới" font

| | Font Office nhìn thấy | Font TeX nhìn thấy |
|---|---|---|
| Nguồn | Font đã cài vào Windows: `C:\Windows\Fonts` và registry `HKLM\…\Fonts`; font cài riêng cho người dùng ở `%LOCALAPPDATA%\Microsoft\Windows\Fonts` và `HKCU\…\Fonts` | Cây TeX (`texmf-dist/fonts/opentype`, `type1`, TFM/VF) cộng font hệ thống (qua `luaotfload` hoặc fontconfig) |
| Native (OMML) dùng được không | **Chỉ font có bảng `MATH`** | Không liên quan |
| Exact (TeX) dùng được không | Có (với LuaLaTeX/XeLaTeX) | Có |

Ý nghĩa thực tế: một font như Latin Modern Math có thể **có trong TeX Live nhưng chưa cài vào Windows**, nên Word không thấy. App phải biết điều đó và đề xuất *"Cài Latin Modern Math cho Word"*. Việc cài được thực hiện per-user, không cần quyền admin; font có license OFL/GFL nên được phép phân phối lại.

### 7.2 Font scanner và cache

**Quét ở đâu:** hai thư mục font của hệ thống và của người dùng, hai nhánh registry (HKLM, HKCU), và thư mục font của các bản TeX đã phát hiện.

**Đọc gì (bộ đọc OpenType tự viết, chỉ đọc):**

| Bảng | Dữ liệu lấy ra |
|---|---|
| Header | sfnt và **TTC**; Cambria Math nằm trong `cambria.ttc` (face index 1) |
| `MATH` | Có hay không. Một phần `MathConstants` (`DisplayOperatorMinHeight`, `AxisHeight`, `SuperscriptShiftUp`, `UpperLimitGapMin`…). `MathVariants`: glyph nào có biến thể theo chiều đứng hoặc có **GlyphAssembly**, ví dụ ∫ ∑ ∏ ( [ { √ |
| `cmap` (format 4/12) | **Bitset coverage** cho các khối liên quan tới toán: Latin, Hy Lạp, Letterlike, Arrows, Math Operators, Misc Technical, Misc Math A/B, Supplemental Operators, Mathematical Alphanumeric (U+1D400–1D7FF) |
| `name`, `head`, `OS/2` | Family, version, `unitsPerEm`, `fsType` (quyền nhúng font) |
| Định dạng outline | TrueType hay CFF. Ảnh hưởng tới khả năng nhúng vào `.docx` (§7.7) |

**Cache:** lưu ở `%LOCALAPPDATA%\MathTypeX\fontcache.db`, khoá theo `(path, faceIndex, size, lastWriteTime)`.

**Quét lại khi nào:**

- khi nhận `WM_FONTCHANGE`;
- khi `FileSystemWatcher` báo thay đổi trong thư mục font;
- khi `RegNotifyChangeKeyValue` báo thay đổi ở các khoá registry font.

Mỗi lần chỉ quét lại phần thay đổi. Lần quét đầu chạy nền khi editor khởi động; danh sách font mặc định có sẵn ngay, không phải chờ.

### 7.3 Bảng tương đương: package TeX truyền thống ↔ font OpenType MATH

Dùng để dựng preset, và để cảnh báo khi người dùng chọn phong cách "legacy" nhưng đang ở chế độ Native.

| Phong cách | Package legacy (pdfLaTeX) | OpenType MATH gần nhất (cho Native và LuaLaTeX) | Ghi chú |
|---|---|---|---|
| Computer Modern | mặc định, `lmodern` | Latin Modern Math, New Computer Modern Math | Gần như giống hệt |
| Times | `mathptmx`, `newtxtext`+`newtxmath`, `stix2` | STIX Two Math, XITS Math, TeX Gyre Termes Math | Phần toán của `mathptmx` là Times + Symbol + CM, khác STIX khá rõ, nên **phải cảnh báo** |
| Palatino | `mathpazo`, `newpxtext`+`newpxmath` | TeX Gyre Pagella Math, Asana Math | |
| Utopia | `fourier`, `mathdesign[utopia]` | Erewhon Math ⚠ | Cần xác minh có trong TeX Live đang dùng |
| Charter | `mathdesign[charter]`, `XCharter` | XCharter Math ⚠ | Cần xác minh. Nếu không có thì không có tương đương native |
| Garamond | `mathdesign[garamond]`, `ebgaramond-maths` | Garamond-Math | |
| Kepler | `kpfonts` | KpMath (`kpfonts-otf`) | |
| Libertine | `newtxmath[libertine]`, `libertinust1math` | Libertinus Math | |
| Century Schoolbook | `fouriernc` | TeX Gyre Schola Math | |
| Bookman | — | TeX Gyre Bonum Math | |
| Fira | — | Fira Math | Chỉ có bản OTF |
| Cambria | — | Cambria Math | Font độc quyền: có sẵn trên Windows/Office, **không được đóng gói phân phối lại** |

**Chính sách:** người dùng chọn một package legacy trong khi đang ở Native, hoặc ở `Auto` nhưng không có TeX, thì app hiển thị:

> *"`mathdesign` (Charter) không dùng được trong Word Equation. Gần nhất: … Khác biệt: …"*

kèm hai nút **[Dùng tương đương]** và **[Dùng Exact LaTeX]**. App không bao giờ tự đổi font mà không báo.

### 7.4 Typography profile: font theo từng lớp ký hiệu

```jsonc
// Ví dụ một profile (rút gọn)
{
  "id": "academic-vn", "name": "Academic Vietnamese", "schema": 1,
  "text":      { "family": "Times New Roman" },
  "mainMath":  { "family": "XITS Math" },
  "slots": {
    "greekLower":    { "family": "XITS Math" },
    "integral":      { "family": "Latin Modern Math" },
    "calligraphic":  { "family": "XITS Math", "features": ["ss01"] },   // ⚠ ss01 tuỳ font
    "blackboard":    { "family": "STIX Two Math" }
  },
  "differentialD": "italic",          // "upright" (ISO 80000-2) | "italic" (thường gặp trong SGK VN)
  "constants":     "italic",          // e, i, π
  "decimalComma":  true,
  "integralSizing": "TeX",            // TeX | Grow | Scale
  "fallback": ["STIX Two Math", "Cambria Math"],
  "size": { "mode": "FollowText", "displayScale": 1.0 },
  "texEngine": "LuaLaTeX"             // hoặc "PdfLaTeX" + "legacyPackage": "newtxmath"
}
```

**Các slot** (đúng theo §3): Latin hoa, Latin thường, Chữ số, Hy Lạp hoa, Hy Lạp thường, Toán tử, Large operator, Tích phân, Tổng/Tích, Delimiter, Calligraphic, Script, Fraktur, Blackboard bold, Sans-serif, Monospace, Vector, Vi phân `d`, Hằng số `e` `i`, Text trong công thức, Số.

**Xử lý (`TypographyResolver`):**

1. **Phân loại từng atom vào slot.** Phân loại dựa trên code point sau chuẩn hoá Unicode math, ví dụ `\mathbb{R}` thành U+211D, `\mathcal{F}` thành U+2131, cộng thêm `MathVariant` và Role của node.
2. **Chọn font cho slot:** dùng font của slot nếu có, nếu không thì dùng `mainMath`.
3. **Kiểm tra coverage** (§7.5).
4. Kết quả là `StyleMap: NodeId → (font, features, scale)`.

**Mỗi backend dùng StyleMap theo cách riêng:**

| Backend | Cách dùng StyleMap |
|---|---|
| OMML | Gán `w:rFonts` cho từng `m:r`. Riêng ký tự n-ary/delimiter: định dạng nằm trong `m:ctrlPr`. ⚠ Word có tôn trọng hay không: S2, quyết định bởi Capability Matrix |
| LuaLaTeX | `\setmathfont{Main}` một lần, rồi `\setmathfont{X}[range=…]` cho từng slot (unicode-math hỗ trợ `range=` theo code point, theo kiểu `up/Greek`, `\mathcal`, `bb`, …) |
| pdfLaTeX legacy | Chỉ trộn được trong khả năng của package, ví dụ `\DeclareMathAlphabet`, `\DeclareSymbolFont`. Phần còn lại hiện cảnh báo "không hỗ trợ" |
| MathML preview | CSS `font-family` gán cho từng phần tử `<mi>`/`<mo>` theo slot |

**`\mathcal` và `\mathscr`:** Unicode dùng chung code point (Unicode 14 có thêm variation selector VS1/VS2 cho chancery/roundhand, nhưng mức hỗ trợ của font và Office ⚠). Cách chắc chắn: gán **hai slot với hai font khác nhau**, hoặc dùng stylistic set ở backend Exact.

### 7.5 Fallback và Font Diagnostics

```
cho mỗi code point cp của atom thuộc slot S:
  f = font(S)
  if covers(f, cp): dùng f
  else:
    for g in profile.fallback ++ preset.fallback ++ (font toán đã cài, ưu tiên cùng phong cách):
      if covers(g, cp): dùng g; ghi Diagnostic(Warning, "thiếu glyph", cp, f, g); break
    nếu không font nào có: Diagnostic(Error); preview tô đỏ ký hiệu, kèm đề xuất cài STIX Two Math (đóng gói sẵn)
```

- Ở Native, run cần fallback **được gán `w:rFonts` tường minh** bằng font fallback. App không để Word tự font-link, vì cơ chế đó có thể cho ra ô vuông hoặc một font bất kỳ.
- Bảng **Font Diagnostics** có dạng: *Selected Greek Font: Font ABC — Missing: `\vartheta`, `\varpi`, `\digamma` — Recommended fallback: STIX Two Math*.
- STIX Two Math (OFL) được **đóng gói sẵn**, nên trong thực tế hầu như không có ký hiệu nào bị thiếu hoàn toàn.

### 7.6 Font preview và Compare mode

- Mỗi mục trong font selector hiển thị câu mẫu `∫₀¹ x² dx α β Γ Δ Σ Π ℝ 𝓕` được render bằng **chính font đó**, qua cùng pipeline MathML/WebView2. Ảnh preview được cache theo `(font, sample)`.
- Compare mode xếp từ 2 tới 4 cột (ví dụ XITS / STIX Two / Latin Modern / Cambria), dùng công thức người dùng đang soạn hoặc bộ mẫu §50/§51.

### 7.7 Đóng gói font, license, và tính di động của tài liệu

- **Đóng gói kèm (tuỳ chọn khi cài):**
  - OFL: STIX Two Math, XITS Math, Libertinus Math, Fira Math.
  - GUST Font License: Latin Modern Math, TeX Gyre Termes/Pagella/Schola/Bonum Math.
  - Asana Math ⚠ (kiểm tra license của bản phát hành).

  Font được cài per-user (copy vào `%LOCALAPPDATA%\Microsoft\Windows\Fonts` và ghi HKCU). ⚠ Mọi phiên bản Office mục tiêu có thấy font cài per-user không: kiểm tra trong S2.
- **Gửi tài liệu sang máy khác:** đây là vấn đề lớn với giáo viên gửi đề thi `.docx`.
  - Nếu máy người nhận không có XITS Math, Word sẽ thay font, và equation hiển thị sai.
  - Tuỳ chọn *Embed fonts* của Word **nhiều khả năng chỉ nhúng được font outline TrueType**, mà phần lớn font toán (XITS, Latin Modern Math, STIX Two bản OTF) là CFF. ⚠ S2 sẽ xác minh.
  - Biện pháp: (1) cảnh báo khi lưu nếu tài liệu dùng font toán không phải Cambria Math; (2) preset "**An toàn khi chia sẻ**" dùng Cambria Math; (3) lệnh "**Chuyển sang vector để chia sẻ**", tức là chuyển các equation sang Exact SVG/EMF mà vẫn giữ source để chuyển ngược lại; (4) dùng bản TTF của font nếu font có phát hành.
- `OS/2.fsType` được đọc để cảnh báo về quyền nhúng font, kể cả khi font bị chuyển thành path trong SVG.

---

## 8. Chiến lược dấu tích phân đúng kiểu LaTeX

### 8.1 Hiểu đúng typography trước khi thiết kế

| Hệ thống | Hành vi của ∫ |
|---|---|
| **LaTeX chuẩn** (Computer Modern, pdfLaTeX) | **Đúng hai cỡ**: text style (glyph nhỏ) và display style (glyph lớn trong `cmex10`). **Không kéo dãn theo integrand.** Giới hạn mặc định đặt bên cạnh (`\nolimits`) |
| **OpenType MATH** (LuaTeX/XeTeX với `unicode-math`, Word, MathML Core) | `MathVariants` của font chứa một **danh sách biến thể theo kích thước**, và có thể có **GlyphAssembly** (⌠ ⎮ ⌡). Display style chọn biến thể có chiều cao ≥ `DisplayOperatorMinHeight`. Chỉ kéo dãn khi được yêu cầu tường minh: Word dùng `m:grow`, MathML dùng `stretchy="true"` |
| **Ký tự ∫ dạng text** (font văn bản, không có bảng MATH) | Không có biến thể nào, nên **luôn ngắn**. Đây chính là hiện tượng "∫ thấp, mất cân đối" |

Vì vậy, yêu cầu "∫ phải stretch theo chiều cao nội dung, *tương tự TeX*" thực ra chứa hai mong muốn khác nhau. Thiết kế tách chúng thành hai chế độ:

### 8.2 Ba chế độ cỡ (`NarySizing`)

| Chế độ | Ý nghĩa | Native (OMML) | Exact (TeX) | MathML preview |
|---|---|---|---|---|
| **`TeX`** (mặc định) | Đúng như LaTeX: text/display | `m:nary`, `m:grow = 0`; display equation thì Word dùng biến thể display | `\int` bình thường | `<mo largeop>` cộng `displaystyle` |
| **`Grow`** | Kéo dãn theo chiều cao của Operand | `m:nary`, **`m:grow = 1`**; Word chọn biến thể hoặc assembly của font. Chất lượng tuỳ font (S2 đo cho từng font) | LuaTeX: dùng ∫ như delimiter qua `\left\Udelimiter …"222B … \right.` ⚠ S8 | `<mo stretchy="true">` trong `mrow` chứa Operand |
| **`Scale(k)`** / **`VariantStep(n)`** | Phóng theo hệ số / chọn biến thể lớn hơn n bậc | 🔴, sang Exact | `\Umathoperatorsize\displaystyle = k·DisplayOperatorMinHeight` (LuaTeX), hoặc chọn biến thể thứ n | `minsize` |

**Bất biến bắt buộc** (§4 và §53): ∫ **luôn** được sinh dưới dạng cấu trúc n-ary (`m:nary` / `<mo largeop>` / `\int`), **không bao giờ** là một ký tự trong run văn bản. Thông qua Normalizer, ∫ dán vào từ chỗ khác cũng được nâng lên thành `LargeOperator`.

### 8.3 Inline và display

- Inline: ∫ ở text size, giới hạn bên cạnh. Đây là hành vi đúng của cả TeX lẫn Word.
- `\displaystyle\int` nằm trong inline: Native không ép được (§01 2.2), nên Auto chuyển sang Exact nếu có TeX, nếu không thì cảnh báo.
- Preset "Word Default" dùng đúng hành vi mặc định của Word.

### 8.4 Các tham số người dùng chỉnh được (§4)

| Tham số | Native | Exact (LuaLaTeX) | pdfLaTeX legacy |
|---|---|---|---|
| Font tích phân | Slot `integral` (⚠ S2) | `\setmathfont{…}[range="222B-"2233]` | Theo package |
| Hệ số cỡ ∫ | 🔴 | `\Umathoperatorsize` | Hạn chế (chỉ đổi glyph hoặc `\scalebox`, có cảnh báo vì nét chữ bị méo) |
| Khoảng cách sub/sup | 🔴 (do MathConstants của font) | `\Umathsupshiftup`, `\Umathsubshiftdown`, `\Umathsubsupvgap`… | `\fontdimen` của family 2/3 (rất hạn chế) |
| Vị trí giới hạn | `m:limLoc` theo từng nary; mặc định của tài liệu qua `Document.OMathIntSubSupLim` | `\limits` / `\nolimits`, tuỳ chọn `intlimits` của amsmath | như Exact |
| Spacing quanh toán tử | 🔴 (Word tự quyết) | `\Umathopordspacing`, `\Umathopbinspacing`… | `\thinmuskip`… |
| Display/Text style | Inline/Display của equation | `\displaystyle` / `\textstyle` | như Exact |

### 8.5 Preset tích phân

| Preset | Font ∫ | Sizing | Giới hạn | Backend |
|---|---|---|---|---|
| TeX Standard | Latin Modern Math | TeX | bên cạnh | Native nếu S2 đạt, nếu không thì Exact |
| LaTeX Modern | New Computer Modern Math | TeX | bên cạnh | như trên |
| Times Math | STIX Two Math / TeX Gyre Termes Math | TeX | bên cạnh | Native |
| STIX | STIX Two Math | TeX | bên cạnh | Native |
| XITS | XITS Math | TeX | bên cạnh | Native |
| Word Default | Cambria Math | (Word mặc định) | (Word mặc định) | Native |

### 8.6 Kiểm chứng (test §51)

**Ma trận test:** 4 công thức tích phân (từ `\int x\,dx` tới `\int_0^1 \frac{\frac{x^2+1}{x+1}}{\sqrt{1-x^4}}\,dx`) × {LaTeX reference (pdfLaTeX + CM), Latin Modern Math, XITS Math, STIX Two Math, Cambria Math} × {inline, display} × {TeX, Grow}.

**Thu ảnh:**

- Word: `Range.EnhMetaFileBits` hoặc `ExportAsFixedFormat` ra PDF.
- TeX: dvisvgm, kèm metrics.

**Assertion tự động** (ngoài so sánh ảnh có ngưỡng):

1. Ở display + `TeX`: chiều cao của ∫ ≥ `DisplayOperatorMinHeight` của font (đọc từ font cache).
2. Ở `Grow`: chiều cao ∫ ≥ 0,9 × (ascent + descent của Operand).
3. Tâm của ∫ nằm trên math axis, sai lệch không quá 1% em.
4. Giới hạn trên/dưới không chạm glyph, theo `UpperLimitGapMin` / `LowerLimitGapMin`.
5. Không có run văn bản nào chứa U+222B…U+2233 nằm ngoài `m:nary`. Đây là kiểm tra trên OMML, không cần ảnh.
