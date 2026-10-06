using MathTypeX.Ast;

namespace MathTypeX.Render.Omml;

/// <summary>Cách biểu diễn môi trường căn dòng (aligned, align) trong OMML — chốt sau spike S1.</summary>
public enum AlignmentStrategy
{
    /// <summary>Ma trận cột phải/trái xen kẽ, khe cột 0: căn chính xác, không phụ thuộc cách Word hiểu "&amp;".</summary>
    Matrix,

    /// <summary>m:eqArr với ký tự "&amp;" trong m:t làm điểm căn (cách Word tự lưu — cần S1 xác nhận).</summary>
    EquationArray,
}

public sealed record OmmlOptions
{
    public bool Display { get; init; }

    public string MathFont { get; init; } = "Cambria Math";

    public string TextFont { get; init; } = "Times New Roman";

    /// <summary>Font riêng cho ký hiệu tích phân (slot "integral"); null = dùng <see cref="MathFont"/>.</summary>
    public string? IntegralFont { get; init; }

    /// <summary>Cỡ chữ (pt); null = theo đoạn văn xung quanh.</summary>
    public double? FontSizePt { get; init; }

    /// <summary>Cỡ ∫ ∑ ∏ khi node để <see cref="NarySizing.Default"/> (mặc định TeX theo quyết định Q4).</summary>
    public NarySizing NarySizing { get; init; } = NarySizing.TeX;

    /// <summary>Vi phân d đứng (ISO 80000-2) thay vì nghiêng (thường gặp trong SGK Việt Nam).</summary>
    public bool UprightDifferential { get; init; }

    public AlignmentStrategy Alignment { get; init; } = AlignmentStrategy.Matrix;

    public static OmmlOptions Default { get; } = new();
}

/// <summary>Kết quả: phần tử m:oMath/m:oMathPara và danh sách chỗ chỉ hiển thị gần đúng trong Word.</summary>
public sealed record OmmlResult(System.Xml.Linq.XElement Element, IReadOnlyList<string> Approximations)
{
    public string Xml => Element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
}
