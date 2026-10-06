using MathTypeX.Ast;

namespace MathTypeX.Parsing;

public enum SymbolKind
{
    Identifier,
    Operator,
    LargeOperator,
    Function,
    Space,
}

public sealed record SymbolInfo(
    SymbolKind Kind,
    string Text,
    AtomClass Class = AtomClass.Ord,
    MathVariant Variant = MathVariant.Default,
    NaryKind Nary = NaryKind.Integral,
    bool Limits = false,
    SpaceKind Space = SpaceKind.Thin);

/// <summary>Bảng lệnh ký hiệu LaTeX (amsmath/amssymb) → ký tự Unicode và lớp atom.</summary>
public static class LatexSymbols
{
    private static readonly Dictionary<string, SymbolInfo> Commands = new(StringComparer.Ordinal);
    // Khoá gồm cả loại và lớp atom để ⊥ (Ord, \bot) và ⊥ (Rel, \perp) được in lại đúng lệnh.
    private static readonly Dictionary<(string Text, SymbolKind Kind, AtomClass Class), string> CharToCommand = new();
    private static readonly Dictionary<string, SymbolInfo> CharInfo = new(StringComparer.Ordinal);

    /// <summary>Dấu mũ: tên lệnh → (ký tự kết hợp, có co giãn không).</summary>
    public static readonly IReadOnlyDictionary<string, (string Char, bool Stretchy)> Accents = new Dictionary<string, (string, bool)>
    {
        ["hat"] = ("̂", false),
        ["widehat"] = ("̂", true),
        ["check"] = ("̌", false),
        ["widecheck"] = ("̌", true),
        ["tilde"] = ("̃", false),
        ["widetilde"] = ("̃", true),
        ["acute"] = ("́", false),
        ["grave"] = ("̀", false),
        ["dot"] = ("̇", false),
        ["ddot"] = ("̈", false),
        ["dddot"] = ("⃛", false),
        ["breve"] = ("̆", false),
        ["bar"] = ("̄", false),
        ["vec"] = ("⃗", false),
        ["overrightarrow"] = ("⃗", true),
        ["overleftarrow"] = ("⃖", true),
        ["overleftrightarrow"] = ("⃡", true),
        ["mathring"] = ("̊", false),
    };

    /// <summary>Tên delimiter dùng sau \left, \right, \big… → ký tự.</summary>
    public static readonly IReadOnlyDictionary<string, string> DelimiterWords = new Dictionary<string, string>
    {
        ["lbrace"] = "{", ["rbrace"] = "}", ["lbrack"] = "[", ["rbrack"] = "]",
        ["langle"] = "⟨", ["rangle"] = "⟩", ["lfloor"] = "⌊", ["rfloor"] = "⌋",
        ["lceil"] = "⌈", ["rceil"] = "⌉", ["vert"] = "|", ["Vert"] = "‖",
        ["lvert"] = "|", ["rvert"] = "|", ["lVert"] = "‖", ["rVert"] = "‖",
        ["uparrow"] = "↑", ["downarrow"] = "↓", ["updownarrow"] = "↕",
        ["Uparrow"] = "⇑", ["Downarrow"] = "⇓", ["Updownarrow"] = "⇕",
        ["backslash"] = "\\", ["lgroup"] = "⟮", ["rgroup"] = "⟯",
        ["llbracket"] = "⟦", ["rrbracket"] = "⟧",
    };

    /// <summary>Ký tự (dạng gõ trực tiếp) dùng được làm delimiter.</summary>
    public static readonly IReadOnlyDictionary<string, string> DelimiterChars = new Dictionary<string, string>
    {
        ["("] = "(", [")"] = ")", ["["] = "[", ["]"] = "]", ["|"] = "|", ["/"] = "/",
        ["."] = "", ["<"] = "⟨", [">"] = "⟩", ["‖"] = "‖", ["⟨"] = "⟨", ["⟩"] = "⟩",
        ["⌊"] = "⌊", ["⌋"] = "⌋", ["⌈"] = "⌈", ["⌉"] = "⌉",
    };

    static LatexSymbols()
    {
        // ── Chữ Hy Lạp thường (\epsilon là ϵ dạng "lunate", \varepsilon là ε; \phi là ϕ, \varphi là φ — đúng như TeX) ──
        Greek("alpha", "α"); Greek("beta", "β"); Greek("gamma", "γ"); Greek("delta", "δ");
        Greek("epsilon", "ϵ"); Greek("varepsilon", "ε"); Greek("zeta", "ζ"); Greek("eta", "η");
        Greek("theta", "θ"); Greek("vartheta", "ϑ"); Greek("iota", "ι"); Greek("kappa", "κ");
        Greek("varkappa", "ϰ"); Greek("lambda", "λ"); Greek("mu", "μ"); Greek("nu", "ν");
        Greek("xi", "ξ"); Greek("omicron", "ο"); Greek("pi", "π"); Greek("varpi", "ϖ");
        Greek("rho", "ρ"); Greek("varrho", "ϱ"); Greek("sigma", "σ"); Greek("varsigma", "ς");
        Greek("tau", "τ"); Greek("upsilon", "υ"); Greek("phi", "ϕ"); Greek("varphi", "φ");
        Greek("chi", "χ"); Greek("psi", "ψ"); Greek("omega", "ω"); Greek("digamma", "ϝ");
        // ── Hy Lạp hoa (đứng theo quy ước TeX); \varGamma… là dạng nghiêng ──
        foreach (var (name, ch) in new[]
                 {
                     ("Gamma", "Γ"), ("Delta", "Δ"), ("Theta", "Θ"), ("Lambda", "Λ"), ("Xi", "Ξ"), ("Pi", "Π"),
                     ("Sigma", "Σ"), ("Upsilon", "Υ"), ("Phi", "Φ"), ("Psi", "Ψ"), ("Omega", "Ω"),
                 })
        {
            Greek(name, ch);
            Add("var" + name, new SymbolInfo(SymbolKind.Identifier, ch, Variant: MathVariant.Italic), register: false);
        }

        // ── Ký hiệu chữ khác (Ord) ──
        Ord("infty", "∞"); Ord("partial", "∂"); Ord("nabla", "∇"); Ord("forall", "∀"); Ord("exists", "∃");
        Ord("nexists", "∄"); Ord("emptyset", "∅"); Ord("varnothing", "⌀"); Ord("neg", "¬"); Ord("lnot", "¬");
        Ord("angle", "∠"); Ord("measuredangle", "∡"); Ord("sphericalangle", "∢"); Ord("triangle", "△");
        Ord("square", "□"); Ord("Box", "□"); Ord("blacksquare", "■"); Ord("Diamond", "◇"); Ord("lozenge", "◊");
        Ord("clubsuit", "♣"); Ord("diamondsuit", "♢"); Ord("heartsuit", "♡"); Ord("spadesuit", "♠");
        Ord("aleph", "ℵ"); Ord("beth", "ℶ"); Ord("gimel", "ℷ"); Ord("hbar", "ℏ"); Ord("hslash", "ℏ");
        Ord("ell", "ℓ"); Ord("wp", "℘"); Ord("Re", "ℜ"); Ord("Im", "ℑ"); Ord("imath", "ı"); Ord("jmath", "ȷ");
        Ord("prime", "′"); Ord("backprime", "‵"); Ord("degree", "°"); Ord("top", "⊤"); Ord("bot", "⊥");
        Ord("flat", "♭"); Ord("natural", "♮"); Ord("sharp", "♯"); Ord("backslash", "\\"); Ord("complement", "∁");
        Ord("mho", "℧"); Ord("surd", "√"); Ord("checkmark", "✓"); Ord("dagger", "†"); Ord("ddagger", "‡");
        Ord("ldots", "…"); Ord("dots", "…"); Ord("dotsc", "…"); Ord("dotso", "…"); Ord("vdots", "⋮");
        Ord("ddots", "⋱"); Ord("iddots", "⋰"); Ord("star", "⋆"); Ord("bigstar", "★"); Ord("S", "§"); Ord("P", "¶");
        Ord("copyright", "©"); Ord("eth", "ð"); Ord("Finv", "Ⅎ"); Ord("Game", "⅁"); Ord("diagup", "╱"); Ord("diagdown", "╲");
        Op("cdots", "⋯", AtomClass.Inner); Op("dotsb", "⋯", AtomClass.Inner); Op("dotsm", "⋯", AtomClass.Inner);

        // ── Phép toán hai ngôi (Bin) ──
        foreach (var (name, ch) in new[]
                 {
                     ("pm", "±"), ("mp", "∓"), ("times", "×"), ("div", "÷"), ("cdot", "⋅"), ("ast", "∗"), ("circ", "∘"),
                     ("bullet", "∙"), ("cap", "∩"), ("cup", "∪"), ("setminus", "∖"), ("smallsetminus", "∖"), ("wedge", "∧"),
                     ("land", "∧"), ("vee", "∨"), ("lor", "∨"), ("oplus", "⊕"), ("ominus", "⊖"), ("otimes", "⊗"),
                     ("oslash", "⊘"), ("odot", "⊙"), ("sqcap", "⊓"), ("sqcup", "⊔"), ("uplus", "⊎"), ("amalg", "⨿"),
                     ("wr", "≀"), ("diamond", "⋄"), ("bigtriangleup", "△"), ("bigtriangledown", "▽"),
                     ("triangleleft", "◃"), ("triangleright", "▹"), ("lhd", "⊲"), ("rhd", "⊳"), ("unlhd", "⊴"),
                     ("unrhd", "⊵"), ("ltimes", "⋉"), ("rtimes", "⋊"), ("boxplus", "⊞"), ("boxminus", "⊟"),
                     ("boxtimes", "⊠"), ("boxdot", "⊡"), ("divideontimes", "⋇"), ("dotplus", "∔"), ("intercal", "⊺"),
                 })
            Op(name, ch, AtomClass.Bin);

        // ── Quan hệ (Rel) — tên chuẩn đặt trước để bộ in LaTeX chọn đúng ──
        foreach (var (name, ch) in new[]
                 {
                     ("le", "≤"), ("leq", "≤"), ("ge", "≥"), ("geq", "≥"), ("ne", "≠"), ("neq", "≠"),
                     ("leqslant", "⩽"), ("geqslant", "⩾"), ("ll", "≪"), ("gg", "≫"), ("lll", "⋘"), ("ggg", "⋙"),
                     ("equiv", "≡"), ("sim", "∼"), ("simeq", "≃"), ("approx", "≈"), ("cong", "≅"), ("ncong", "≇"),
                     ("nsim", "≁"), ("propto", "∝"), ("asymp", "≍"), ("doteq", "≐"), ("triangleq", "≜"),
                     ("coloneqq", "≔"), ("eqqcolon", "≕"), ("subset", "⊂"), ("supset", "⊃"), ("subseteq", "⊆"),
                     ("supseteq", "⊇"), ("subsetneq", "⊊"), ("supsetneq", "⊋"), ("nsubseteq", "⊈"), ("nsupseteq", "⊉"),
                     ("sqsubseteq", "⊑"), ("sqsupseteq", "⊒"), ("in", "∈"), ("ni", "∋"), ("owns", "∋"),
                     ("notin", "∉"), ("perp", "⊥"), ("parallel", "∥"), ("nparallel", "∦"), ("mid", "∣"), ("nmid", "∤"),
                     ("models", "⊨"), ("vdash", "⊢"), ("dashv", "⊣"), ("vDash", "⊨"), ("Vdash", "⊩"),
                     ("prec", "≺"), ("succ", "≻"), ("preceq", "⪯"), ("succeq", "⪰"), ("bowtie", "⋈"), ("smile", "⌣"),
                     ("frown", "⌢"), ("lesssim", "≲"), ("gtrsim", "≳"), ("lessgtr", "≶"), ("gtrless", "≷"),
                     ("nleq", "≰"), ("ngeq", "≱"), ("nless", "≮"), ("ngtr", "≯"), ("therefore", "∴"), ("because", "∵"),
                     ("to", "→"), ("rightarrow", "→"), ("leftarrow", "←"), ("gets", "←"), ("leftrightarrow", "↔"),
                     ("Rightarrow", "⇒"), ("Leftarrow", "⇐"), ("Leftrightarrow", "⇔"), ("implies", "⟹"),
                     ("impliedby", "⟸"), ("Longleftrightarrow", "⟺"), ("iff", "⟺"), ("mapsto", "↦"),
                     ("longrightarrow", "⟶"), ("longleftarrow", "⟵"), ("longleftrightarrow", "⟷"),
                     ("Longrightarrow", "⟹"), ("Longleftarrow", "⟸"), ("longmapsto", "⟼"), ("uparrow", "↑"),
                     ("downarrow", "↓"), ("updownarrow", "↕"), ("Uparrow", "⇑"), ("Downarrow", "⇓"),
                     ("Updownarrow", "⇕"), ("nearrow", "↗"), ("searrow", "↘"), ("swarrow", "↙"), ("nwarrow", "↖"),
                     ("hookrightarrow", "↪"), ("hookleftarrow", "↩"), ("rightharpoonup", "⇀"), ("rightharpoondown", "⇁"),
                     ("leftharpoonup", "↼"), ("leftharpoondown", "↽"), ("rightleftharpoons", "⇌"),
                     ("leftrightharpoons", "⇋"), ("rightrightarrows", "⇉"), ("leftleftarrows", "⇇"),
                     ("rightleftarrows", "⇄"), ("leftrightarrows", "⇆"), ("twoheadrightarrow", "↠"),
                     ("rightsquigarrow", "⇝"), ("leadsto", "⇝"), ("nrightarrow", "↛"), ("nleftarrow", "↚"),
                     ("nRightarrow", "⇏"), ("nLeftrightarrow", "⇎"), ("circeq", "≗"), ("bumpeq", "≏"),
                     ("approxeq", "≊"), ("backsim", "∽"), ("thicksim", "∼"), ("thickapprox", "≈"),
                 })
            Op(name, ch, AtomClass.Rel);
        Op("colon", ":", AtomClass.Punct);

        // ── Large operator ──
        Big("sum", "∑", NaryKind.Sum); Big("prod", "∏", NaryKind.Product); Big("coprod", "∐", NaryKind.Coproduct);
        Big("int", "∫", NaryKind.Integral); Big("iint", "∬", NaryKind.DoubleIntegral);
        Big("iiint", "∭", NaryKind.TripleIntegral); Big("iiiint", "⨌", NaryKind.QuadrupleIntegral);
        Big("oint", "∮", NaryKind.ContourIntegral); Big("oiint", "∯", NaryKind.SurfaceIntegral);
        Big("oiiint", "∰", NaryKind.VolumeIntegral);
        Big("bigcup", "⋃", NaryKind.BigCup); Big("bigcap", "⋂", NaryKind.BigCap); Big("bigvee", "⋁", NaryKind.BigVee);
        Big("bigwedge", "⋀", NaryKind.BigWedge); Big("bigoplus", "⨁", NaryKind.BigOPlus);
        Big("bigotimes", "⨂", NaryKind.BigOTimes); Big("bigodot", "⨀", NaryKind.BigODot);
        Big("biguplus", "⨄", NaryKind.BigUPlus); Big("bigsqcup", "⨆", NaryKind.BigSqCup);
        Add("intop", Commands["int"], register: false);
        Add("smallint", Commands["int"], register: false);

        // ── Hàm (chữ đứng). Limits = giới hạn đặt trên/dưới khi display ──
        foreach (var name in new[]
                 {
                     "arccos", "arcsin", "arctan", "arg", "cos", "cosh", "cot", "coth", "csc", "deg", "dim", "exp",
                     "hom", "ker", "lg", "ln", "log", "sec", "sin", "sinh", "tan", "tanh",
                 })
            Add(name, new SymbolInfo(SymbolKind.Function, name));
        foreach (var name in new[] { "det", "gcd", "inf", "lim", "liminf", "limsup", "max", "min", "Pr", "sup" })
            Add(name, new SymbolInfo(SymbolKind.Function, name, Limits: true));

        // ── Khoảng trắng ──
        Spc("quad", SpaceKind.Quad); Spc("qquad", SpaceKind.QQuad); Spc("enspace", SpaceKind.En);
        Spc("thinspace", SpaceKind.Thin); Spc("medspace", SpaceKind.Medium); Spc("thickspace", SpaceKind.Thick);
        Spc("negthinspace", SpaceKind.NegativeThin);

        // ── Ký tự gõ trực tiếp ──
        foreach (var (ch, cls) in new[]
                 {
                     ("+", AtomClass.Bin), ("=", AtomClass.Rel), ("<", AtomClass.Rel), (">", AtomClass.Rel),
                     (":", AtomClass.Rel), (",", AtomClass.Punct), (";", AtomClass.Punct), (".", AtomClass.Ord),
                     ("!", AtomClass.Close), ("?", AtomClass.Close), ("(", AtomClass.Open), (")", AtomClass.Close),
                     ("[", AtomClass.Open), ("]", AtomClass.Close), ("|", AtomClass.Ord), ("/", AtomClass.Ord),
                     ("@", AtomClass.Ord), ("\"", AtomClass.Ord), ("−", AtomClass.Bin), ("∗", AtomClass.Bin),
                     ("⟨", AtomClass.Open), ("⟩", AtomClass.Close), ("⌊", AtomClass.Open), ("⌋", AtomClass.Close),
                     ("⌈", AtomClass.Open), ("⌉", AtomClass.Close), ("{", AtomClass.Open), ("}", AtomClass.Close),
                     ("‖", AtomClass.Ord),
                 })
            CharInfo[ch] = new SymbolInfo(SymbolKind.Operator, ch, cls);
    }

    private static void Add(string name, SymbolInfo info, bool register = true)
    {
        Commands[name] = info;
        if (!register) return;
        var key = (info.Text, info.Kind, info.Class);
        if (!CharToCommand.ContainsKey(key) && info.Kind is not SymbolKind.Function and not SymbolKind.Space)
            CharToCommand[key] = name;
        if (!CharInfo.ContainsKey(info.Text) && info.Kind is not SymbolKind.Function and not SymbolKind.Space)
            CharInfo[info.Text] = info;
    }

    private static void Greek(string name, string ch) => Add(name, new SymbolInfo(SymbolKind.Identifier, ch));

    private static void Ord(string name, string ch) => Add(name, new SymbolInfo(SymbolKind.Identifier, ch, Variant: MathVariant.Normal));

    private static void Op(string name, string ch, AtomClass cls) => Add(name, new SymbolInfo(SymbolKind.Operator, ch, cls));

    private static void Big(string name, string ch, NaryKind kind) =>
        Add(name, new SymbolInfo(SymbolKind.LargeOperator, ch, AtomClass.Op, Nary: kind, Limits: !kind.IsIntegral()));

    private static void Spc(string name, SpaceKind kind) => Add(name, new SymbolInfo(SymbolKind.Space, "", Space: kind));

    public static bool TryGetCommand(string name, out SymbolInfo info) => Commands.TryGetValue(name, out info!);

    /// <summary>Thông tin về một ký tự gõ trực tiếp (ví dụ "≤", "∫", "α").</summary>
    public static bool TryGetChar(string ch, out SymbolInfo info) => CharInfo.TryGetValue(ch, out info!);

    /// <summary>Lệnh chuẩn để in lại một ký tự (≤ → "le"), theo đúng loại và lớp atom.</summary>
    public static bool TryGetCommandForChar(string ch, SymbolKind kind, AtomClass cls, out string name) =>
        CharToCommand.TryGetValue((ch, kind, cls), out name!);

    public static IEnumerable<string> CommandNames => Commands.Keys;
}
