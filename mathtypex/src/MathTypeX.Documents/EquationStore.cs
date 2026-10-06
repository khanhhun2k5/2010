using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml.Linq;

namespace MathTypeX.Documents;

/// <summary>Metadata của một công thức (docs/07 §12.2) — lưu trong tài liệu và trong kho cục bộ.</summary>
[DataContract]
public sealed class EquationRecord
{
    [DataMember] public int Schema { get; set; } = 1;
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Key { get; set; } = "";
    [DataMember] public string OriginalLatex { get; set; } = "";
    [DataMember] public string NormalizedLatex { get; set; } = "";
    [DataMember] public bool Display { get; set; }
    [DataMember] public string MathFont { get; set; } = "";
    [DataMember] public string RenderMode { get; set; } = "Native";
    [DataMember] public int AstVersion { get; set; } = Ast.AstInfo.Version;
    [DataMember] public string CreatedUtc { get; set; } = "";
    [DataMember] public string UpdatedUtc { get; set; } = "";
    [DataMember] public string AppVersion { get; set; } = "";

    public static EquationRecord Create(string key, string originalLatex, string normalizedLatex, bool display, string mathFont)
    {
        string now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return new EquationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Key = key,
            OriginalLatex = originalLatex,
            NormalizedLatex = normalizedLatex,
            Display = display,
            MathFont = mathFont,
            CreatedUtc = now,
            UpdatedUtc = now,
            AppVersion = typeof(EquationRecord).Assembly.GetName().Version?.ToString() ?? "",
        };
    }

    internal string ToJson()
    {
        var serializer = new DataContractJsonSerializer(typeof(EquationRecord));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, this);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    internal static EquationRecord? FromJson(string json)
    {
        try
        {
            var serializer = new DataContractJsonSerializer(typeof(EquationRecord));
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return serializer.ReadObject(ms) as EquationRecord;
        }
        catch (SerializationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Kho metadata trong tài liệu (tầng L1): một CustomXMLPart với namespace riêng, mỗi công thức một phần tử
/// &lt;eq key="…"&gt;{JSON}&lt;/eq&gt;. Dữ liệu đọc từ tài liệu là dữ liệu không tin cậy — chỉ được dùng làm source để parse lại.
/// </summary>
public sealed class EquationStore
{
    public const string Namespace = "urn:mathtypex:equations:v1";
    private static readonly XNamespace Ns = Namespace;

    private readonly Dictionary<string, EquationRecord> _byKey = new(StringComparer.Ordinal);

    public IReadOnlyCollection<EquationRecord> Records => _byKey.Values;

    public static EquationStore Parse(string? xml)
    {
        var store = new EquationStore();
        if (string.IsNullOrWhiteSpace(xml)) return store;
        try
        {
            var root = XDocument.Parse(xml!).Root;
            if (root is null || root.Name != Ns + "store") return store;
            foreach (var eq in root.Elements(Ns + "eq"))
            {
                var record = EquationRecord.FromJson(eq.Value);
                if (record is not null && record.Key.Length > 0) store.Upsert(record);
            }
        }
        catch (System.Xml.XmlException)
        {
            // Phần XML hỏng: coi như kho rỗng (không làm hỏng việc sửa công thức).
        }
        return store;
    }

    public EquationRecord? FindByKey(string key) => _byKey.TryGetValue(key, out var r) ? r : null;

    /// <summary>Thêm hoặc thay bản ghi cùng khoá; bản mới hơn (UpdatedUtc) thắng.</summary>
    public void Upsert(EquationRecord record)
    {
        if (_byKey.TryGetValue(record.Key, out var existing) && string.CompareOrdinal(existing.UpdatedUtc, record.UpdatedUtc) > 0) return;
        _byKey[record.Key] = record;
    }

    public string ToXml()
    {
        var root = new XElement(Ns + "store", new XAttribute("schema", 1),
            _byKey.Values.OrderBy(r => r.CreatedUtc, StringComparer.Ordinal)
                .Select(r => new XElement(Ns + "eq", new XAttribute("key", r.Key), r.ToJson())));
        return root.ToString(SaveOptions.DisableFormatting);
    }
}

/// <summary>
/// Kho cục bộ (tầng L3): %LOCALAPPDATA%\MathTypeX\equations.jsonl — cứu trường hợp copy công thức sang tài liệu khác
/// trên cùng máy. Mỗi dòng một bản ghi; bản sau ghi đè bản trước cùng khoá. Người dùng xoá được bất cứ lúc nào.
/// </summary>
public sealed class LocalEquationStore
{
    private const int MaxLines = 20000;
    private readonly string _path;

    public LocalEquationStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MathTypeX", "equations.jsonl");
    }

    public string Path => _path;

    public EquationRecord? FindByKey(string key)
    {
        if (!File.Exists(_path)) return null;
        EquationRecord? found = null;
        foreach (var line in File.ReadLines(_path))
        {
            if (line.IndexOf(key, StringComparison.Ordinal) < 0) continue;
            var record = EquationRecord.FromJson(line);
            if (record?.Key == key) found = record;
        }
        return found;
    }

    public void Save(EquationRecord record)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, record.ToJson() + "\n", new UTF8Encoding(false));
            CompactIfNeeded();
        }
        catch (IOException)
        {
            // Kho cục bộ chỉ là tầng dự phòng.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void CompactIfNeeded()
    {
        var lines = File.ReadAllLines(_path);
        if (lines.Length <= MaxLines) return;
        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var record = EquationRecord.FromJson(line);
            if (record is not null) latest[record.Key] = line;
        }
        File.WriteAllLines(_path, latest.Values.Skip(Math.Max(0, latest.Count - MaxLines / 2)), new UTF8Encoding(false));
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
