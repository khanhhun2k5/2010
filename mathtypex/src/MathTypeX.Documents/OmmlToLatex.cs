using System.Text;
using System.Xml.Linq;
using MathTypeX.Ast;
using MathTypeX.Parsing;
using MathTypeX.Render.Omml;

namespace MathTypeX.Documents;

/// <summary>Kết quả chuyển ngược một Word Equation.</summary>
public sealed record ReverseResult(string Latex, string NormalizedLatex, bool Display, IReadOnlyList<string> Fonts, bool Grow)
{
    /// <summary>Font của run toán đầu tiên — dùng để chọn sẵn font khi mở editor.</summary>
    public string? PrimaryFont => Fonts.Count > 0 ? Fonts[0] : null;
}

/// <summary>
/// OMML → LaTeX (docs/07 §12.5, tầng L4): dùng cho mọi Word Equation, kể cả công thức do Word hay công cụ khác tạo.
/// Chiến lược: dịch từng phần tử OMML thành LaTeX rồi cho chính <see cref="LatexParser"/> đọc lại, nên mọi quy tắc
/// (bắt phần thân ∫, đối số hàm…) được dùng chung với đường gõ trực tiếp.
/// Round-trip được kiểm chứng: Print(Parse(Reverse(Write(x)))) == Print(Parse(x)) trên toàn bộ corpus.
/// </summary>
public static class OmmlToLatex
{
    private static readonly XNamespace M = OmmlWriter.M;
    private static readonly XNamespace W = OmmlWriter.W;

    public static ReverseResult Convert(XElement omml)
    {
        bool display = omml.Name == M + "oMathPara" || omml.Descendants(M + "oMathPara").Any();
        var converter = new Converter(display);
        string latex = converter.Element(omml).Trim();
        var doc = LatexParser.Parse(latex);
        return new ReverseResult(latex, LatexPrinter.Print(doc), display, converter.Fonts, converter.Grow);
    }

    private static bool OnOff(XElement? e, bool whenMissing = false)
    {
        if (e is null) return whenMissing;
        string? v = e.Attribute(M + "val")?.Value;
        return v is null or "1" or "on" or "true";
    }

    private static string? Val(XElement? e) => e?.Attribute(M + "val")?.Value;

    private sealed class Converter
    {
        private readonly bool _display;
        private readonly List<string> _fonts = new();
        private bool _inEquationArray;

        public Converter(bool display) => _display = display;

        public IReadOnlyList<string> Fonts => _fonts;

        public bool Grow { get; private set; }

        private string Children(XElement? container)
        {
            if (container is null) return "";
            var b = new LatexBuilder();
            var elements = container.Elements().ToArray();
            for (int i = 0; i < elements.Length; i++)
            {
                // Word có thể tách một đoạn \text{…} thành nhiều run (kiểm tra chính tả, rsid): gộp lại.
                if (NormalTextStyle(elements[i]) is { } style)
                {
                    var text = new StringBuilder();
                    int j = i;
                    while (j < elements.Length && NormalTextStyle(elements[j]) == style)
                    {
                        text.Append(string.Concat(elements[j].Elements(M + "t").Select(t => t.Value)));
                        j++;
                    }
                    b.Append("\\" + style + "{" + EscapeText(text.ToString()) + "}");
                    i = j - 1;
                    continue;
                }
                b.Append(Element(elements[i]));
            }
            return b.ToString();
        }

        /// <summary>"text"/"textbf"/"textit" nếu là run chữ thường (m:nor), ngược lại null.</summary>
        private static string? NormalTextStyle(XElement e)
        {
            if (e.Name != M + "r" || e.Element(M + "rPr")?.Element(M + "nor") is not { } nor || !OnOff(nor)) return null;
            var wPr = e.Element(W + "rPr");
            bool bold = wPr?.Element(W + "b") is { } b && b.Attribute(W + "val")?.Value is null or "1" or "true" or "on";
            bool italic = wPr?.Element(W + "i") is { } i && i.Attribute(W + "val")?.Value is null or "1" or "true" or "on";
            return bold ? "textbf" : italic ? "textit" : "text";
        }

        public string Element(XElement e)
        {
            if (e.Name.Namespace == W)
            {
                return e.Name.LocalName switch
                {
                    "del" or "bookmarkStart" or "bookmarkEnd" or "proofErr" or "rPr" or "permStart" or "permEnd" => "",
                    "r" => WordTextRun(e),
                    _ => Children(e),
                };
            }

            switch (e.Name.LocalName)
            {
                case "oMathPara":
                {
                    var lines = e.Elements(M + "oMath").Select(Children).Where(s => s.Length > 0).ToArray();
                    return lines.Length <= 1 ? string.Concat(lines) : "\\begin{gathered}" + string.Join("\\\\", lines) + "\\end{gathered}";
                }
                case "r":
                    return Run(e);
                case "f":
                    return Fraction(e);
                case "rad":
                {
                    bool hideDegree = OnOff(e.Element(M + "radPr")?.Element(M + "degHide"));
                    string degree = Children(e.Element(M + "deg"));
                    string body = Children(e.Element(M + "e"));
                    return hideDegree || degree.Length == 0 ? $"\\sqrt{{{body}}}" : $"\\sqrt[{degree}]{{{body}}}";
                }
                case "sSub":
                    return Base(e.Element(M + "e")) + "_{" + Children(e.Element(M + "sub")) + "}";
                case "sSup":
                    return Base(e.Element(M + "e")) + Superscript(e.Element(M + "sup"));
                case "sSubSup":
                    return Base(e.Element(M + "e")) + "_{" + Children(e.Element(M + "sub")) + "}" + Superscript(e.Element(M + "sup"));
                case "sPre":
                    return "{}_{" + Children(e.Element(M + "sub")) + "}^{" + Children(e.Element(M + "sup")) + "}" + Base(e.Element(M + "e"));
                case "nary":
                    return Nary(e);
                case "func":
                    return FunctionName(e.Element(M + "fName")) + " " + Children(e.Element(M + "e"));
                case "d":
                    return Delimiter(e);
                case "acc":
                {
                    string chr = Val(e.Element(M + "accPr")?.Element(M + "chr")) ?? "̂";
                    return "\\" + AccentCommand(chr) + "{" + Children(e.Element(M + "e")) + "}";
                }
                case "bar":
                {
                    bool top = Val(e.Element(M + "barPr")?.Element(M + "pos")) == "top";
                    return (top ? "\\overline{" : "\\underline{") + Children(e.Element(M + "e")) + "}";
                }
                case "groupChr":
                    return GroupChar(e, label: null, labelAbove: false);
                case "limLow":
                case "limUpp":
                    return Limit(e, e.Name.LocalName == "limUpp");
                case "m":
                    return Matrix(e, "matrix");
                case "eqArr":
                    return EquationArray(e);
                case "borderBox":
                    return BorderBox(e);
                case "phant":
                    return Phantom(e);
                default:
                    // e, num, den, sub, sup, deg, lim, fName, box… và phần tử chưa biết: lấy nội dung.
                    return Children(e);
            }
        }

        // ── Run ──────────────────────────────────────────────────────────

        private string Run(XElement r)
        {
            var rPr = r.Element(M + "rPr");
            bool normalText = rPr?.Element(M + "nor") is { } nor && OnOff(nor);
            string text = string.Concat(r.Elements(M + "t").Select(t => t.Value));
            var wPr = r.Element(W + "rPr");
            if (normalText)
            {
                bool bold = wPr?.Element(W + "b") is { } b && b.Attribute(W + "val")?.Value is null or "1" or "true" or "on";
                bool italic = wPr?.Element(W + "i") is { } i && i.Attribute(W + "val")?.Value is null or "1" or "true" or "on";
                string cmd = bold ? "textbf" : italic ? "textit" : "text";
                return "\\" + cmd + "{" + EscapeText(text) + "}";
            }

            string? font = wPr?.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value;
            if (!string.IsNullOrEmpty(font) && !_fonts.Contains(font!)) _fonts.Add(font!);

            string? sty = Val(rPr?.Element(M + "sty"));
            string? scr = Val(rPr?.Element(M + "scr"));
            // Run chữ đứng trùng tên hàm (sin, arg, lim…) đứng riêng → lệnh hàm, không phải \mathrm{…}.
            string trimmed = text.Trim();
            if (sty == "p" && trimmed.Length > 1 && trimmed.All(char.IsLetter)
                && LatexSymbols.TryGetCommand(trimmed, out var fn) && fn.Kind == SymbolKind.Function)
                return "\\" + trimmed + " ";
            return MathText(text, sty, scr);
        }

        private static string WordTextRun(XElement r)
        {
            string text = string.Concat(r.Elements(W + "t").Select(t => t.Value));
            return text.Length == 0 ? "" : "\\text{" + EscapeText(text) + "}";
        }

        /// <summary>Chữ trong m:t → LaTeX: chữ Hy Lạp/ký hiệu thành lệnh, kiểu chữ thành \mathrm/\mathbb…</summary>
        private string MathText(string text, string? sty, string? scr)
        {
            var b = new LatexBuilder();
            string? openWrapper = null;
            var pending = new StringBuilder();

            void Flush()
            {
                if (openWrapper is null) return;
                b.Command(openWrapper);
                b.Raw("{" + pending + "}");
                pending.Clear();
                openWrapper = null;
            }

            void Emit(string latex, string? wrapper)
            {
                if (wrapper != openWrapper) Flush();
                if (wrapper is null)
                {
                    b.Append(latex);
                }
                else
                {
                    openWrapper = wrapper;
                    if (pending.Length > 0 && latex.Length > 0 && char.IsLetter(latex[0]) && EndsWithCommand(pending)) pending.Append(' ');
                    pending.Append(latex);
                }
            }

            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? char.ConvertToUtf32(text[i], text[++i]) : text[i];
                string ch = char.ConvertFromUtf32(cp);

                if (ch == "\u2003" && i + 1 < text.Length && text[i + 1] == '\u2003')
                {
                    i++;
                    Emit("\\qquad ", null);
                    continue;
                }
                if (SpaceCommands.TryGetValue(ch, out var space))
                {
                    Emit(space, null);
                    continue;
                }
                if (ch == "," && i > 0 && char.IsDigit(text[i - 1]) && i + 1 < text.Length && char.IsDigit(text[i + 1]))
                {
                    Emit("{,}", null);
                    continue;
                }
                if (cp is ' ' or 0x2061 or 0x2062 or 0x2063 or 0x2064) continue;

                if (MathAlphabets.TryDecompose(cp, out char baseChar, out var variant) && VariantWrapper(variant) is { } alphaWrapper)
                {
                    Emit(baseChar.ToString(), alphaWrapper);
                    continue;
                }

                if (ch.Length == 1 && (MathAlphabets.IsLatin(ch[0]) || MathAlphabets.IsGreekLower(ch[0]) || MathAlphabets.IsGreekUpper(ch[0])))
                {
                    Emit(LetterLatex(ch), LetterWrapper(ch[0], sty, scr));
                    continue;
                }

                if (ch.Length == 1 && char.IsDigit(ch[0]))
                {
                    Emit(ch, sty is "b" or "bi" ? "mathbf" : null);
                    continue;
                }

                Emit(SymbolLatex(ch), null);
            }
            Flush();
            return b.ToString();
        }

        private static bool EndsWithCommand(StringBuilder sb)
        {
            int i = sb.Length - 1;
            if (i < 0 || !char.IsLetter(sb[i])) return false;
            while (i >= 0 && char.IsLetter(sb[i])) i--;
            return i >= 0 && sb[i] == '\\';
        }

        private static readonly Dictionary<string, string> SpaceCommands = new()
        {
            [" "] = "\\,", [" "] = "\\,", [" "] = "\\,", [" "] = "\\,", [" "] = "\\:",
            [" "] = "\\;", [" "] = "\\ ", [" "] = "\\quad ", [" "] = "\\enspace ", [" "] = "~",
        };

        private static string LetterLatex(string ch) =>
            LatexSymbols.TryGetCommandForChar(ch, SymbolKind.Identifier, AtomClass.Ord, out var name) ? "\\" + name : ch;

        /// <summary>m:sty → lệnh kiểu chữ. Nghiêng là mặc định của chữ Latin/Hy Lạp thường nên "i" không cần bọc.</summary>
        private static string? LetterWrapper(char c, string? sty, string? scr)
        {
            if (scr is not null && scr != "roman")
            {
                return scr switch
                {
                    "double-struck" => "mathbb",
                    "script" => "mathcal",
                    "fraktur" => "mathfrak",
                    "sans-serif" => "mathsf",
                    "monospace" => "mathtt",
                    _ => null,
                };
            }
            bool italicByDefault = MathAlphabets.IsLatin(c) || MathAlphabets.IsGreekLower(c);
            return sty switch
            {
                "p" => italicByDefault ? "mathrm" : null,
                "b" => "mathbf",
                "bi" => "boldsymbol",
                "i" => italicByDefault ? null : "mathit",
                _ => null,
            };
        }

        private static string? VariantWrapper(MathVariant v) => v switch
        {
            MathVariant.DoubleStruck => "mathbb",
            MathVariant.Script or MathVariant.Calligraphic => "mathcal",
            MathVariant.Fraktur => "mathfrak",
            MathVariant.Bold => "mathbf",
            MathVariant.Italic => null,
            MathVariant.BoldItalic => "boldsymbol",
            MathVariant.SansSerif => "mathsf",
            MathVariant.Monospace => "mathtt",
            _ => null,
        };

        private string SymbolLatex(string ch)
        {
            switch (ch)
            {
                case "−": return "-";
                case "∗": return "*";
                case "′": return "'";
                case "{": return "\\{";
                case "}": return "\\}";
                case "‖": return "\\|";
                case "%" or "#" or "$" or "_": return "\\" + ch;
                case "&": return _inEquationArray ? "&" : "\\&";
                case "\\": return "\\backslash ";
                case "^": return "\\hat{}";
                case "~": return "\\sim ";
            }
            foreach (var cls in new[] { AtomClass.Rel, AtomClass.Bin, AtomClass.Inner, AtomClass.Punct, AtomClass.Open, AtomClass.Close, AtomClass.Ord })
                if (LatexSymbols.TryGetCommandForChar(ch, SymbolKind.Operator, cls, out var name)) return "\\" + name;
            if (LatexSymbols.TryGetCommandForChar(ch, SymbolKind.Identifier, AtomClass.Ord, out var id)) return "\\" + id;
            if (LatexSymbols.TryGetCommandForChar(ch, SymbolKind.LargeOperator, AtomClass.Op, out var big)) return "\\" + big;
            return ch;
        }

        private static string EscapeText(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\textbackslash{}"); break;
                    case '{' or '}' or '$' or '%' or '&' or '#' or '_': sb.Append('\\').Append(c); break;
                    case '^': sb.Append("\\^{}"); break;
                    case '~': sb.Append("\\~{}"); break;
                    case ' ': sb.Append('~'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ── Cấu trúc ─────────────────────────────────────────────────────

        /// <summary>Phần cơ sở của chỉ số: không bọc ngoặc nếu là một atom (x, \alpha, \frac…), ngược lại bọc {…}.</summary>
        private string Base(XElement? e)
        {
            string body = Children(e);
            if (body.Length == 0) return "{}";
            var doc = LatexParser.Parse(body);
            bool atomic = !doc.HasErrors && doc.Body.Children.Count == 1 && doc.Body.Children[0] is not Scripts and not LargeOperator and not FunctionApply;
            return atomic ? body : "{" + body + "}";
        }

        private string Superscript(XElement? sup)
        {
            string body = Children(sup);
            string primes = new string(body.TakeWhile(c => c == '\'').ToArray());
            if (primes.Length > 0 && primes.Length == body.Length) return primes;
            if (primes.Length > 0) return primes + "^{" + body.Substring(primes.Length) + "}";
            return "^{" + body + "}";
        }

        private string Fraction(XElement f)
        {
            string type = Val(f.Element(M + "fPr")?.Element(M + "type")) ?? "bar";
            string num = Children(f.Element(M + "num")), den = Children(f.Element(M + "den"));
            return type switch
            {
                "noBar" => "{" + num + "\\atop " + den + "}",
                "lin" => "{" + num + "}/{" + den + "}",
                _ => "\\frac{" + num + "}{" + den + "}",
            };
        }

        private string Nary(XElement n)
        {
            var pr = n.Element(M + "naryPr");
            string chr = Val(pr?.Element(M + "chr")) ?? "∫";
            bool integral = LatexSymbols.TryGetChar(chr, out var info) && info.Kind == SymbolKind.LargeOperator && info.Nary.IsIntegral();
            // Thiếu m:limLoc: mặc định của Word là tích phân để bên cạnh, các toán tử khác đặt trên/dưới.
            string? limLoc = Val(pr?.Element(M + "limLoc"));
            bool under = limLoc is null ? !integral : limLoc == "undOvr";
            if (OnOff(pr?.Element(M + "grow"))) Grow = true;
            bool hideSub = OnOff(pr?.Element(M + "subHide"));
            bool hideSup = OnOff(pr?.Element(M + "supHide"));

            var b = new LatexBuilder();
            if (LatexSymbols.TryGetCommandForChar(chr, SymbolKind.LargeOperator, AtomClass.Op, out var name)) b.Command(name);
            else b.Raw(chr);

            bool autoUnder = !integral && _display;
            if (under != autoUnder) b.Command(under ? "limits" : "nolimits");

            string sub = Children(n.Element(M + "sub")), sup = Children(n.Element(M + "sup"));
            if (!hideSub && sub.Length > 0) b.Raw("_{" + sub + "}");
            if (!hideSup && sup.Length > 0) b.Raw("^{" + sup + "}");
            b.Append(Children(n.Element(M + "e")));
            return b.ToString();
        }

        /// <summary>Tên hàm: "sin", "lim" (m:limLow), "sin²" (m:sSup)… → \sin, \lim_{…}, \sin^{2}.</summary>
        private string FunctionName(XElement? fName)
        {
            if (fName is null) return "";
            var only = fName.Elements().Where(x => x.Name != M + "ctrlPr").ToArray();
            if (only.Length > 1 && SingleRunText(fName) is { } joined) return NamedFunction(joined);
            if (only.Length == 1)
            {
                var el = only[0];
                switch (el.Name.LocalName)
                {
                    case "r":
                        return NamedFunction(SingleRunText(fName) ?? RunText(el));
                    case "limLow" when SingleRunText(el.Element(M + "e")) is { } lowName:
                        return NamedFunction(lowName) + "_{" + Children(el.Element(M + "lim")) + "}";
                    case "limUpp" when SingleRunText(el.Element(M + "e")) is { } uppName:
                        return NamedFunction(uppName) + "^{" + Children(el.Element(M + "lim")) + "}";
                    case "limUpp" when el.Element(M + "e")?.Elements(M + "limLow").SingleOrDefault() is { } inner && SingleRunText(inner.Element(M + "e")) is { } bothName:
                        return NamedFunction(bothName) + "_{" + Children(inner.Element(M + "lim")) + "}^{" + Children(el.Element(M + "lim")) + "}";
                    case "sSup" when SingleRunText(el.Element(M + "e")) is { } supName:
                        return NamedFunction(supName) + Superscript(el.Element(M + "sup"));
                    case "sSub" when SingleRunText(el.Element(M + "e")) is { } subName:
                        return NamedFunction(subName) + "_{" + Children(el.Element(M + "sub")) + "}";
                    case "sSubSup" when SingleRunText(el.Element(M + "e")) is { } bothScripts:
                        return NamedFunction(bothScripts) + "_{" + Children(el.Element(M + "sub")) + "}" + Superscript(el.Element(M + "sup"));
                }
            }
            return Children(fName);
        }

        private static string RunText(XElement r) => string.Concat(r.Elements(M + "t").Select(t => t.Value)).Trim();

        /// <summary>Chữ của một vùng chỉ gồm các m:r (Word có thể tách "lim" thành nhiều run).</summary>
        private static string? SingleRunText(XElement? e)
        {
            if (e is null) return null;
            var meaningful = e.Elements().Where(x => x.Name != M + "ctrlPr" && x.Name.Namespace != W).ToArray();
            if (meaningful.Length == 0 || meaningful.Any(x => x.Name != M + "r")) return null;
            return string.Concat(meaningful.Select(RunText));
        }

        private static string NamedFunction(string name)
        {
            if (name.Length == 0) return "";
            if (LatexSymbols.TryGetCommand(name, out var info) && info.Kind == SymbolKind.Function) return "\\" + name;
            return "\\operatorname{" + EscapeText(name) + "}";
        }

        private string Delimiter(XElement d)
        {
            var pr = d.Element(M + "dPr");
            string open = Val(pr?.Element(M + "begChr")) ?? "(";
            string close = Val(pr?.Element(M + "endChr")) ?? ")";
            string separator = Val(pr?.Element(M + "sepChr")) ?? "|";
            var parts = d.Elements(M + "e").ToArray();

            if (parts.Length == 1)
            {
                var inner = parts[0].Elements().Where(x => x.Name != M + "ctrlPr").ToArray();
                if (inner.Length == 1 && inner[0].Name == M + "m")
                {
                    string? env = (open, close) switch
                    {
                        ("(", ")") => "pmatrix",
                        ("[", "]") => "bmatrix",
                        ("{", "}") => "Bmatrix",
                        ("|", "|") => "vmatrix",
                        ("‖", "‖") => "Vmatrix",
                        ("{", "") => "cases",
                        ("", "}") => "rcases",
                        _ => null,
                    };
                    if (env is not null) return Matrix(inner[0], env);
                }
                if (open == "(" && close == ")" && inner.Length == 1 && inner[0].Name == M + "f"
                    && Val(inner[0].Element(M + "fPr")?.Element(M + "type")) == "noBar")
                {
                    return "\\binom{" + Children(inner[0].Element(M + "num")) + "}{" + Children(inner[0].Element(M + "den")) + "}";
                }
            }

            var b = new StringBuilder("\\left" + DelimiterLatex(open));
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) b.Append("\\middle" + DelimiterLatex(separator));
                b.Append(' ').Append(Children(parts[i])).Append(' ');
            }
            b.Append("\\right" + DelimiterLatex(close));
            return b.ToString();
        }

        private static string DelimiterLatex(string d) => d switch
        {
            "" => ".",
            "{" => "\\{",
            "}" => "\\}",
            "‖" => "\\|",
            "(" or ")" or "[" or "]" or "|" or "/" => d,
            "⟨" => "\\langle ",
            "⟩" => "\\rangle ",
            "⌊" => "\\lfloor ",
            "⌋" => "\\rfloor ",
            "⌈" => "\\lceil ",
            "⌉" => "\\rceil ",
            _ => d,
        };

        private static string AccentCommand(string chr)
        {
            foreach (var stretchy in new[] { false, true })
                foreach (var kv in LatexSymbols.Accents)
                    if (kv.Value.Char == chr && kv.Value.Stretchy == stretchy) return kv.Key;
            return "hat";
        }

        private static readonly Dictionary<string, string> Arrows = new()
        {
            ["→"] = "xrightarrow", ["←"] = "xleftarrow", ["⇒"] = "xRightarrow", ["⇐"] = "xLeftarrow",
            ["↔"] = "xleftrightarrow", ["⇔"] = "xLeftrightarrow", ["↦"] = "xmapsto", ["↪"] = "xhookrightarrow",
        };

        private string GroupChar(XElement g, string? label, bool labelAbove)
        {
            var pr = g.Element(M + "groupChrPr");
            string chr = Val(pr?.Element(M + "chr")) ?? "⏟";
            bool top = Val(pr?.Element(M + "pos")) == "top";
            string body = Children(g.Element(M + "e"));
            if (!top && Arrows.TryGetValue(chr, out var arrow))
                return "\\" + arrow + (label is not null && !labelAbove ? "[" + label + "]" : "") + "{" + body + "}";
            string cmd = (chr, top) switch
            {
                ("⏞", _) => "overbrace",
                ("⏟", _) => "underbrace",
                ("⎴", _) => "overbracket",
                ("⎵", _) => "underbracket",
                ("⏜", _) => "overparen",
                ("⏝", _) => "underparen",
                (_, true) => "overbrace",
                _ => "underbrace",
            };
            string result = "\\" + cmd + "{" + body + "}";
            if (label is not null) result += (labelAbove ? "^{" : "_{") + label + "}";
            return result;
        }

        private string Limit(XElement e, bool upper)
        {
            var baseEl = e.Element(M + "e");
            string lim = Children(e.Element(M + "lim"));
            var inner = baseEl?.Elements().Where(x => x.Name != M + "ctrlPr").ToArray() ?? Array.Empty<XElement>();
            if (inner.Length == 1 && inner[0].Name == M + "groupChr")
                return GroupChar(inner[0], lim, labelAbove: upper);
            if (SingleRunText(baseEl) is { } name && LatexSymbols.TryGetCommand(name, out var info) && info.Kind == SymbolKind.Function && info.Limits)
                return "\\" + name + (upper ? "^{" : "_{") + lim + "}";
            return (upper ? "\\overset{" : "\\underset{") + lim + "}{" + Children(baseEl) + "}";
        }

        private string Matrix(XElement m, string env)
        {
            var pr = m.Element(M + "mPr");
            var justify = pr?.Element(M + "mcs")?.Elements(M + "mc")
                .Select(mc => Val(mc.Element(M + "mcPr")?.Element(M + "mcJc")) ?? "center").ToArray() ?? Array.Empty<string>();
            bool tight = Val(pr?.Element(M + "cGpRule")) == "3" && Val(pr?.Element(M + "cGp")) == "0";
            bool alignedPattern = env == "matrix" && tight && justify.Length >= 2
                && justify.Select((j, i) => i % 2 == 0 ? j == "right" : j == "left").All(ok => ok);
            if (alignedPattern) env = "aligned";

            var rows = new List<string>();
            foreach (var mr in m.Elements(M + "mr"))
            {
                var cells = mr.Elements(M + "e").Select((cell, c) =>
                {
                    string text = Children(cell).Trim();
                    // Bỏ khoảng \qquad mà OmmlWriter chèn đầu mỗi cặp cột mới của aligned.
                    if (alignedPattern && c > 0 && c % 2 == 0 && text.StartsWith("\\qquad", StringComparison.Ordinal))
                        text = text.Substring(6).TrimStart();
                    return text;
                }).ToList();
                while (cells.Count > 1 && cells[cells.Count - 1].Length == 0 && env is "cases" or "rcases") cells.RemoveAt(cells.Count - 1);
                rows.Add(string.Join("&", cells));
            }
            return "\\begin{" + env + "}" + string.Join("\\\\", rows) + "\\end{" + env + "}";
        }

        private string EquationArray(XElement eqArr)
        {
            bool saved = _inEquationArray;
            _inEquationArray = true;
            try
            {
                var rows = eqArr.Elements(M + "e").Select(Children).ToArray();
                string env = rows.Any(r => r.Contains('&')) ? "aligned" : "gathered";
                return "\\begin{" + env + "}" + string.Join("\\\\", rows) + "\\end{" + env + "}";
            }
            finally
            {
                _inEquationArray = saved;
            }
        }

        private string BorderBox(XElement box)
        {
            var pr = box.Element(M + "borderBoxPr");
            bool hidden = OnOff(pr?.Element(M + "hideTop")) && OnOff(pr?.Element(M + "hideBot"))
                && OnOff(pr?.Element(M + "hideLeft")) && OnOff(pr?.Element(M + "hideRight"));
            bool bltr = OnOff(pr?.Element(M + "strikeBLTR")), tlbr = OnOff(pr?.Element(M + "strikeTLBR"));
            string cmd = hidden && bltr && tlbr ? "xcancel" : hidden && bltr ? "cancel" : hidden && tlbr ? "bcancel" : "boxed";
            return "\\" + cmd + "{" + Children(box.Element(M + "e")) + "}";
        }

        private string Phantom(XElement ph)
        {
            var pr = ph.Element(M + "phantPr");
            string body = Children(ph.Element(M + "e"));
            if (OnOff(pr?.Element(M + "show"), whenMissing: true)) return body;
            if (OnOff(pr?.Element(M + "zeroWid"))) return "\\vphantom{" + body + "}";
            if (OnOff(pr?.Element(M + "zeroAsc")) && OnOff(pr?.Element(M + "zeroDesc"))) return "\\hphantom{" + body + "}";
            return "\\phantom{" + body + "}";
        }
    }

    /// <summary>Ghép LaTeX, tự chèn khoảng trắng sau control word khi cần (\alpha x).</summary>
    private sealed class LatexBuilder
    {
        private readonly StringBuilder _sb = new();

        public void Command(string name) => Append("\\" + name);

        public void Raw(string s) => Append(s);

        public void Append(string s)
        {
            if (s.Length == 0) return;
            if (char.IsLetter(s[0]) && EndsWithControlWord()) _sb.Append(' ');
            _sb.Append(s);
        }

        private bool EndsWithControlWord()
        {
            int i = _sb.Length - 1;
            if (i < 0 || !char.IsLetter(_sb[i])) return false;
            while (i >= 0 && char.IsLetter(_sb[i])) i--;
            return i >= 0 && _sb[i] == '\\';
        }

        public override string ToString() => _sb.ToString();
    }
}
