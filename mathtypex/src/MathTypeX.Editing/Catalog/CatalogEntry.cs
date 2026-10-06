namespace MathTypeX.Editing.Catalog;

/// <summary>Nhóm trong Visual Formula Library (§9).</summary>
public enum Category
{
    Basic, Fraction, Power, Root, Greek, Calculus, Integral, SumProduct, Limit, Trigonometry, Logarithm,
    Vector, Matrix, LinearAlgebra, Probability, Statistics, SetTheory, Logic, Geometry, NumberTheory,
    Relations, Arrows, Accents, Text,
}

/// <summary>
/// Một mục trong catalog: lệnh LaTeX, mẫu có placeholder, tên/mô tả tiếng Việt và tiếng Anh, ví dụ để xem trước.
/// <see cref="Snippet"/> dùng cú pháp $1, $2 … (điểm Tab) và $0 (vị trí kết thúc).
/// </summary>
public sealed record CatalogEntry
{
    public required string Id { get; init; }
    /// <summary>Tên lệnh không có "\" (frac, alpha…) — dùng cho autocomplete khi gõ "\fra".</summary>
    public required string Trigger { get; init; }
    public required string Snippet { get; init; }
    public required Category Category { get; init; }
    public required string NameVi { get; init; }
    public required string NameEn { get; init; }
    public string KeywordsVi { get; init; } = "";
    public string KeywordsEn { get; init; } = "";
    public string DescriptionVi { get; init; } = "";
    public string DescriptionEn { get; init; } = "";
    /// <summary>Cú pháp đọc được cho người mới, ví dụ \frac{tử số}{mẫu số}.</summary>
    public string SyntaxVi { get; init; } = "";
    /// <summary>LaTeX ví dụ để vẽ preview (mặc định = snippet bỏ placeholder).</summary>
    public string Example { get; init; } = "";
    /// <summary>Tên các đối số (tiếng Việt) theo thứ tự — dùng cho trợ giúp ngữ cảnh.</summary>
    public IReadOnlyList<string> ArgumentsVi { get; init; } = Array.Empty<string>();
    public string? Symbol { get; init; }
}

/// <summary>Tên nhóm hiển thị trong Command Palette và thư viện công thức.</summary>
public static class CategoryText
{
    public static string Vi(Category c) => c switch
    {
        Category.Basic => "Cơ bản",
        Category.Fraction => "Phân số",
        Category.Power => "Luỹ thừa, chỉ số",
        Category.Root => "Căn",
        Category.Greek => "Chữ Hy Lạp",
        Category.Calculus => "Giải tích",
        Category.Integral => "Tích phân",
        Category.SumProduct => "Tổng, tích",
        Category.Limit => "Giới hạn",
        Category.Trigonometry => "Lượng giác",
        Category.Logarithm => "Logarit, mũ",
        Category.Vector => "Vectơ",
        Category.Matrix => "Ma trận",
        Category.LinearAlgebra => "Đại số tuyến tính",
        Category.Probability => "Xác suất",
        Category.Statistics => "Thống kê",
        Category.SetTheory => "Tập hợp",
        Category.Logic => "Logic",
        Category.Geometry => "Hình học",
        Category.NumberTheory => "Số học",
        Category.Relations => "Quan hệ",
        Category.Arrows => "Mũi tên",
        Category.Accents => "Dấu trên chữ",
        Category.Text => "Chữ thường",
        _ => c.ToString(),
    };

    public static string En(Category c) => c switch
    {
        Category.SumProduct => "Sums & products",
        Category.LinearAlgebra => "Linear algebra",
        Category.SetTheory => "Set theory",
        Category.NumberTheory => "Number theory",
        _ => c.ToString(),
    };
}
