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
}
