namespace MathTypeX.Ast;

/// <summary>Phiên bản cấu trúc AST; tăng khi thêm/đổi node theo cách ảnh hưởng tới metadata đã lưu.</summary>
public static class AstInfo
{
    public const int Version = 1;
}

/// <summary>Kiểu chữ toán theo ngữ nghĩa. <see cref="Default"/> = quy ước TeX (chữ Latin và Hy Lạp thường nghiêng, Hy Lạp hoa đứng, số đứng).</summary>
public enum MathVariant
{
    Default,
    Normal,
    Italic,
    Bold,
    BoldItalic,
    DoubleStruck,
    Script,
    BoldScript,
    Calligraphic,
    Fraktur,
    BoldFraktur,
    SansSerif,
    SansSerifBold,
    SansSerifItalic,
    SansSerifBoldItalic,
    Monospace,
}

public enum IdentifierRole
{
    Variable,
    Differential,
    Constant,
}

/// <summary>Lớp atom theo TeX (Appendix G) — quyết định khoảng cách giữa các ký hiệu.</summary>
public enum AtomClass
{
    Ord,
    Op,
    Bin,
    Rel,
    Open,
    Close,
    Punct,
    Inner,
}

public enum DelimiterSize
{
    Normal,
    Big,
    Big2,
    Big3,
    Big4,
}

public enum SpaceKind
{
    Thin,        // \,  3mu
    Medium,      // \:  4mu
    Thick,       // \;  5mu
    NegativeThin, // \! -3mu
    Quad,        // \quad 1em
    QQuad,       // \qquad 2em
    Interword,   // "\ "
    NonBreaking, // ~
    En,          // \enspace 0.5em
}

public enum TextStyle
{
    Normal,
    Bold,
    Italic,
}

public enum FractionKind
{
    Bar,
    NoBar,
    Linear,
    Skewed,
}

public enum MathStyleOverride
{
    Auto,
    Display,
    Text,
}

public enum NaryKind
{
    Integral,
    DoubleIntegral,
    TripleIntegral,
    QuadrupleIntegral,
    ContourIntegral,
    SurfaceIntegral,
    VolumeIntegral,
    Sum,
    Product,
    Coproduct,
    BigCup,
    BigCap,
    BigVee,
    BigWedge,
    BigOPlus,
    BigOTimes,
    BigODot,
    BigUPlus,
    BigSqCup,
}

public enum LimitPlacement
{
    Auto,
    Limits,
    NoLimits,
}

/// <summary>Cỡ của large operator. <see cref="Default"/> = theo typography profile (mặc định TeX).</summary>
public enum NarySizing
{
    Default,
    TeX,
    Grow,
}

public enum VerticalPosition
{
    Top,
    Bottom,
}

public enum TableKind
{
    Matrix,
    PMatrix,
    BMatrix,
    BBMatrix,
    VMatrix,
    VVMatrix,
    SmallMatrix,
    Array,
    Cases,
    RCases,
}

public enum AlignmentKind
{
    Aligned,
    Align,
    AlignStar,
    Gather,
    GatherStar,
    Gathered,
    Split,
    Equation,
    EquationStar,
    Multline,
}

public enum ColumnAlign
{
    Center,
    Left,
    Right,
}

public enum MathStyle
{
    Display,
    Text,
    Script,
    ScriptScript,
}

public enum BoxKind
{
    Boxed,
    Cancel,
    BCancel,
    XCancel,
}

public enum PhantomKind
{
    Full,
    Horizontal,
    Vertical,
}

public static class NaryKindExtensions
{
    public static bool IsIntegral(this NaryKind kind) => kind switch
    {
        NaryKind.Integral or NaryKind.DoubleIntegral or NaryKind.TripleIntegral or NaryKind.QuadrupleIntegral
            or NaryKind.ContourIntegral or NaryKind.SurfaceIntegral or NaryKind.VolumeIntegral => true,
        _ => false,
    };

    /// <summary>Số vi phân cần tìm để kết thúc phần thân (∬ cần dx dy).</summary>
    public static int DifferentialCount(this NaryKind kind) => kind switch
    {
        NaryKind.DoubleIntegral or NaryKind.SurfaceIntegral => 2,
        NaryKind.TripleIntegral or NaryKind.VolumeIntegral => 3,
        NaryKind.QuadrupleIntegral => 4,
        _ => kind.IsIntegral() ? 1 : 0,
    };
}
