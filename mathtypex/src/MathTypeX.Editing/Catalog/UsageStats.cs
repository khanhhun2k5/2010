using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MathTypeX.Editing.Catalog;

/// <summary>
/// Thống kê dùng lệnh cục bộ (§27: Recently used / Frequently used) — chỉ lưu Id của mục catalog và số lần dùng,
/// không lưu nội dung công thức, không gửi đi đâu.
/// </summary>
public sealed class UsageStats
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly List<string> _recent = new();

    public int Boost(string id) => _counts.TryGetValue(id, out int n) ? (int)(Math.Log(1 + n) * 60) : 0;

    public IReadOnlyList<string> Recent => _recent;

    public IReadOnlyList<string> Frequent => _counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).Take(20).ToArray();

    public void Record(string id)
    {
        _counts[id] = _counts.TryGetValue(id, out int n) ? n + 1 : 1;
        _recent.Remove(id);
        _recent.Insert(0, id);
        if (_recent.Count > 20) _recent.RemoveAt(_recent.Count - 1);
    }

    [DataContract]
    private sealed class Dto
    {
        [DataMember] public Dictionary<string, int> Counts { get; set; } = new();
        [DataMember] public List<string> Recent { get; set; } = new();
    }

    public static UsageStats Load(string path)
    {
        var stats = new UsageStats();
        try
        {
            if (!File.Exists(path)) return stats;
            using var stream = File.OpenRead(path);
            if (new DataContractJsonSerializer(typeof(Dto)).ReadObject(stream) is Dto dto)
            {
                foreach (var kv in dto.Counts) stats._counts[kv.Key] = kv.Value;
                stats._recent.AddRange(dto.Recent.Take(20));
            }
        }
        catch (Exception ex) when (ex is IOException or SerializationException or UnauthorizedAccessException)
        {
        }
        return stats;
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            new DataContractJsonSerializer(typeof(Dto)).WriteObject(stream, new Dto { Counts = new Dictionary<string, int>(_counts), Recent = _recent.ToList() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
