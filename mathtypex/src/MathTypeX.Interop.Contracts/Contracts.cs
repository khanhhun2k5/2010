using System.Runtime.Serialization;

namespace MathTypeX.Interop;

/// <summary>Hình chữ nhật trên màn hình, đơn vị pixel vật lý (đúng như Word trả về từ Window.GetPoint).</summary>
[DataContract]
public sealed class ScreenRect
{
    [DataMember] public int Left { get; set; }
    [DataMember] public int Top { get; set; }
    [DataMember] public int Width { get; set; }
    [DataMember] public int Height { get; set; }

    public override string ToString() => $"({Left},{Top}) {Width}×{Height}";
}

/// <summary>Add-in → editor: mở editor tại con trỏ để soạn (mới) hoặc sửa (có <see cref="Latex"/>).</summary>
[DataContract]
public sealed class EditRequest
{
    [DataMember] public string Host { get; set; } = "Word";
    [DataMember] public string Latex { get; set; } = "";
    [DataMember] public bool Display { get; set; }
    [DataMember] public bool DisplayAllowed { get; set; } = true;
    [DataMember] public double? FontSizePt { get; set; }
    [DataMember] public string? TextFont { get; set; }
    [DataMember] public string? PreferredMathFont { get; set; }
    [DataMember] public ScreenRect? Caret { get; set; }
    [DataMember] public long OwnerWindow { get; set; }
    [DataMember] public int HostProcessId { get; set; }
    [DataMember] public string? Notice { get; set; }
}

/// <summary>Editor → add-in: kết quả soạn thảo. <see cref="Cancelled"/> = người dùng nhấn Esc.</summary>
[DataContract]
public sealed class EditResult
{
    [DataMember] public bool Cancelled { get; set; }
    [DataMember] public string Latex { get; set; } = "";
    [DataMember] public string NormalizedLatex { get; set; } = "";
    [DataMember] public bool Display { get; set; }
    [DataMember] public string MathFont { get; set; } = "Cambria Math";
    /// <summary>Gói Flat OPC sẵn sàng cho Range.InsertXML.</summary>
    [DataMember] public string FlatOpc { get; set; } = "";

    public static EditResult Cancel() => new() { Cancelled = true };
}

/// <summary>Một công thức tìm thấy khi quét cả tài liệu (VS-9), gửi sang editor để người dùng duyệt.</summary>
[DataContract]
public sealed class ScanItem
{
    [DataMember] public int Id { get; set; }
    /// <summary>Văn bản gốc kèm delimiter, ví dụ "$x^2$".</summary>
    [DataMember] public string Source { get; set; } = "";
    /// <summary>LaTeX sẽ được chuyển (đã bỏ delimiter, sửa ký tự AutoCorrect, gộp dấu câu).</summary>
    [DataMember] public string Latex { get; set; } = "";
    [DataMember] public bool Display { get; set; }
    [DataMember] public int Confidence { get; set; }
    /// <summary>Được tích sẵn: đủ độ tin cậy và không bị quy tắc nào loại.</summary>
    [DataMember] public bool Recommended { get; set; }
    [DataMember] public string[] Reasons { get; set; } = new string[0];
    /// <summary>Lý do không thể chuyển (đang ở vùng code, trong equation có sẵn…); có giá trị thì mục bị khoá.</summary>
    [DataMember] public string? Blocked { get; set; }
    /// <summary>Thân bài, chú thích, header…</summary>
    [DataMember] public string Location { get; set; } = "";
    /// <summary>Đoạn văn quanh công thức để người dùng nhận ra vị trí, ví dụ "…ta có ⟦$$E=mc^2$$⟧ nên…".</summary>
    [DataMember] public string Context { get; set; } = "";
}

/// <summary>Add-in → editor: mở hộp duyệt kết quả quét tài liệu.</summary>
[DataContract]
public sealed class ScanRequest
{
    [DataMember] public string DocumentName { get; set; } = "";
    [DataMember] public ScanItem[] Items { get; set; } = new ScanItem[0];
    [DataMember] public string MathFont { get; set; } = "Cambria Math";
    [DataMember] public long OwnerWindow { get; set; }
}

/// <summary>Editor → add-in: các mục được chọn để chuyển, và văn bản người dùng muốn luôn bỏ qua.</summary>
[DataContract]
public sealed class ScanResult
{
    [DataMember] public bool Cancelled { get; set; }
    [DataMember] public int[] SelectedIds { get; set; } = new int[0];
    [DataMember] public string[] IgnoredSources { get; set; } = new string[0];
}

/// <summary>Phong bì của mọi thông điệp trên pipe (một dòng JSON / thông điệp).</summary>
[DataContract]
public sealed class RpcEnvelope
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Method { get; set; } = "";
    [DataMember] public string? Payload { get; set; }
    [DataMember] public string? Error { get; set; }
}

public static class RpcMethods
{
    public const string Ping = "ping";
    public const string Edit = "edit";
    public const string Review = "review";
}
