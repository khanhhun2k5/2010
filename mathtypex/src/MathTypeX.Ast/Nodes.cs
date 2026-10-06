namespace MathTypeX.Ast;

/// <summary>
/// Gốc của mọi node. AST bất biến, không chứa font (font do TypographyResolver quyết định).
/// <see cref="Span"/> trỏ về source gốc để tô lỗi và đồng bộ con trỏ.
/// </summary>
public abstract record MathNode
{
    public SourceSpan Span { get; init; }
}

// ── Khung ──────────────────────────────────────────────────────────────────

public sealed record Row(IReadOnlyList<MathNode> Children) : MathNode
{
    public static Row Empty { get; } = new(Array.Empty<MathNode>());

    public static Row Of(params MathNode[] children) => new(children);
}

/// <summary>Nhóm {…} tường minh: là một atom Ord, ảnh hưởng tới spacing và phạm vi.</summary>
public sealed record Group(Row Content) : MathNode;

// ── Atom ──────────────────────────────────────────────────────────────────

/// <summary>Biến/ký hiệu chữ. <see cref="Text"/> là ký tự gốc (ví dụ "R" cho \mathbb{R}, "α" cho \alpha).</summary>
public sealed record Identifier(string Text, MathVariant Variant = MathVariant.Default, IdentifierRole Role = IdentifierRole.Variable) : MathNode;

public sealed record Number(string Text, MathVariant Variant = MathVariant.Default) : MathNode;

public sealed record Operator(string Text, AtomClass Class, DelimiterSize Size = DelimiterSize.Normal) : MathNode;

public sealed record TextRun(string Text, TextStyle Style = TextStyle.Normal) : MathNode;

public sealed record Space(SpaceKind Kind) : MathNode;

/// <summary>\displaystyle, \textstyle… áp dụng cho phần còn lại của nhóm hiện tại.</summary>
public sealed record StyleSwitch(MathStyle Style) : MathNode;

// ── Cấu trúc ──────────────────────────────────────────────────────────────

public sealed record Fraction(MathNode Numerator, MathNode Denominator, FractionKind Kind = FractionKind.Bar, MathStyleOverride Style = MathStyleOverride.Auto) : MathNode;

public sealed record Radical(MathNode Radicand, MathNode? Index = null) : MathNode;

public sealed record Scripts(MathNode Base, MathNode? Sub, MathNode? Sup) : MathNode;

/// <summary>∫ ∑ ∏ … — luôn là cấu trúc n-ary, không bao giờ là ký tự text (bất biến D9).</summary>
public sealed record LargeOperator(
    string Symbol,
    NaryKind Kind,
    MathNode? Lower = null,
    MathNode? Upper = null,
    LimitPlacement Limits = LimitPlacement.Auto,
    NarySizing Sizing = NarySizing.Default,
    MathNode? Operand = null) : MathNode;

/// <summary>Hàm có tên đứng: \sin x, \lim_{x\to0} f(x), \operatorname{rank} A.</summary>
public sealed record FunctionApply(
    string Name,
    bool IsBuiltin,
    bool LimitsByDefault,
    MathNode? Lower = null,
    MathNode? Upper = null,
    LimitPlacement Limits = LimitPlacement.Auto,
    MathNode? Argument = null) : MathNode;

/// <summary>\left( … \middle| … \right). Chuỗi rỗng = ngoặc ẩn (\left.).</summary>
public sealed record Fenced(string Open, string Close, IReadOnlyList<MathNode> Parts, IReadOnlyList<string> Separators) : MathNode
{
    public static Fenced Simple(string open, string close, MathNode content) =>
        new(open, close, new[] { content }, Array.Empty<string>());
}

public sealed record Accent(MathNode Base, string AccentChar, bool Stretchy) : MathNode;

public sealed record Bar(MathNode Base, VerticalPosition Position) : MathNode;

/// <summary>\overbrace, \underbrace, \xrightarrow… ; <see cref="Label"/> là chữ đặt trên/dưới ngoặc.</summary>
public sealed record GroupChar(MathNode Base, string Char, VerticalPosition Position, MathNode? Label = null) : MathNode;

/// <summary>\overset, \underset, \stackrel.</summary>
public sealed record UnderOver(MathNode Base, MathNode? Under, MathNode? Over) : MathNode;

public sealed record TableRow(IReadOnlyList<MathNode> Cells);

public sealed record Table(TableKind Kind, IReadOnlyList<TableRow> Rows, IReadOnlyList<ColumnAlign> Columns) : MathNode
{
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells.Count);
}

public sealed record AlignedRow(IReadOnlyList<MathNode> Cells, string? Tag = null, string? Label = null, bool NoNumber = false);

public sealed record Alignment(AlignmentKind Kind, IReadOnlyList<AlignedRow> Rows) : MathNode;

public sealed record Boxed(MathNode Content, BoxKind Kind) : MathNode;

public sealed record Phantom(MathNode Content, PhantomKind Kind) : MathNode;

// ── Soạn thảo & lỗi ───────────────────────────────────────────────────────

/// <summary>Ô trống □: đối số còn thiếu hoặc tab-stop của snippet.</summary>
public sealed record Placeholder(int TabIndex = 0, string? Hint = null) : MathNode;

public sealed record ErrorNode(string Raw) : MathNode;

public sealed record UnknownCommand(string Name, IReadOnlyList<MathNode> Arguments) : MathNode;

/// <summary>Kết quả parse: cây và danh sách chẩn đoán (parser không bao giờ throw).</summary>
public sealed record MathDocument(Row Body, IReadOnlyList<Diagnostic> Diagnostics)
{
    public int AstVersion => AstInfo.Version;

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
