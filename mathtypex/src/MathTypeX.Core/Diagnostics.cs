namespace MathTypeX;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum DiagnosticCode
{
    MissingCloseBrace,
    ExtraCloseBrace,
    MissingArgument,
    UnknownCommand,
    UnmatchedLeft,
    UnmatchedRight,
    MissingDelimiter,
    DoubleSuperscript,
    DoubleSubscript,
    MisplacedAlignment,
    MisplacedRowSeparator,
    UnsupportedCaretNotation,
    UnexpectedMathShift,
    UnexpectedParameter,
    IncompleteCommand,
    MissingEnvironmentEnd,
    MismatchedEnvironmentEnd,
    UnknownEnvironment,
    MisplacedEnd,
    MissingCloseBracket,
    UnsupportedCommand,
    VietnameseImeMangled,
    ApproximateRendering,
    NestingTooDeep,
    MisplacedLimits,
}

/// <summary>Vai trò của một đối số, dùng để sinh thông báo lỗi dễ hiểu ("thiếu } để kết thúc mẫu số").</summary>
public sealed record ArgRole(string Id, string Vi, string En)
{
    public static readonly ArgRole Numerator = new("numerator", "tử số", "numerator");
    public static readonly ArgRole Denominator = new("denominator", "mẫu số", "denominator");
    public static readonly ArgRole Radicand = new("radicand", "biểu thức dưới căn", "radicand");
    public static readonly ArgRole RootIndex = new("rootIndex", "chỉ số căn", "root index");
    public static readonly ArgRole Superscript = new("superscript", "số mũ (chỉ số trên)", "superscript");
    public static readonly ArgRole Subscript = new("subscript", "chỉ số dưới", "subscript");
    public static readonly ArgRole AccentBase = new("accentBase", "phần được đặt dấu", "accented expression");
    public static readonly ArgRole Content = new("content", "nội dung", "content");
    public static readonly ArgRole Text = new("text", "phần chữ", "text");
    public static readonly ArgRole Group = new("group", "nhóm { }", "group");
    public static readonly ArgRole Over = new("over", "phần phía trên", "upper part");
    public static readonly ArgRole Under = new("under", "phần phía dưới", "lower part");
    public static readonly ArgRole Base = new("base", "phần chính", "base");
    public static readonly ArgRole EnvironmentName = new("environmentName", "tên môi trường", "environment name");
    public static readonly ArgRole OperatorName = new("operatorName", "tên toán tử", "operator name");
    public static readonly ArgRole Label = new("label", "nhãn", "label");
    public static readonly ArgRole Tag = new("tag", "số hiệu", "tag");
    public static readonly ArgRole ColumnSpec = new("columnSpec", "định dạng cột", "column specification");
}

/// <summary>Gợi ý sửa lỗi: thay đoạn <see cref="Span"/> bằng <see cref="Replacement"/> (Length = 0 nghĩa là chèn).</summary>
public sealed record FixIt(SourceSpan Span, string Replacement);

public sealed record Diagnostic(
    DiagnosticCode Code,
    DiagnosticSeverity Severity,
    SourceSpan Span,
    ArgRole? Role = null,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IReadOnlyList<FixIt>? FixIts = null)
{
    public IReadOnlyList<string> Arguments => Args ?? Array.Empty<string>();

    public IReadOnlyList<FixIt> Fixes => FixIts ?? Array.Empty<FixIt>();

    public override string ToString() => $"{Severity} {Code} {Span}: {DiagnosticFormatter.Format(this, UiLanguage.En)}";
}
