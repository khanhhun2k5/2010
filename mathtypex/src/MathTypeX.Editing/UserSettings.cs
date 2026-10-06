using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using MathTypeX.Ast;

namespace MathTypeX.Editing;

/// <summary>
/// Thiết lập cá nhân dùng chung cho editor và add-in (lưu ở %APPDATA%\MathTypeX\settings.json, không gửi đi đâu).
/// DataContract thay vì System.Text.Json để add-in (.NET Framework 4.8, không NuGet) đọc được cùng một tệp.
/// </summary>
[DataContract]
public sealed class UserSettings
{
    public UserSettings() => SetDefaults();

    [DataMember] public string MathFont { get; set; } = "";
    [DataMember] public bool PreferDisplay { get; set; }
    [DataMember] public bool DecimalComma { get; set; }
    [DataMember] public bool UprightDifferential { get; set; }
    [DataMember] public bool GrowIntegrals { get; set; }
    /// <summary>Beginner mode (§26): dòng trợ giúp cho biết đang nhập đối số nào của lệnh nào. F1 bật/tắt.</summary>
    [DataMember] public bool BeginnerMode { get; set; }
    /// <summary>Ngưỡng độ tin cậy (0–100) khi chuyển LaTeX trong văn bản (docs/05 §9.4).</summary>
    [DataMember] public int ConvertMinConfidence { get; set; }
    [DataMember] public bool ConvertSingleDollar { get; set; }
    /// <summary>Văn bản (kèm delimiter) người dùng đã chọn "luôn bỏ qua" khi chuyển LaTeX, ví dụ "$HOME$".</summary>
    [DataMember] public List<string> IgnoredSources { get; set; } = new();

    public const int MaxIgnoredSources = 500;

    public bool IsIgnored(string source) => IgnoredSources.Contains(source.Trim());

    public void Ignore(IEnumerable<string> sources)
    {
        foreach (var source in sources.Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            IgnoredSources.Remove(source);
            IgnoredSources.Insert(0, source);
        }
        if (IgnoredSources.Count > MaxIgnoredSources) IgnoredSources.RemoveRange(MaxIgnoredSources, IgnoredSources.Count - MaxIgnoredSources);
    }

    private void SetDefaults()
    {
        MathFont = "Cambria Math";
        BeginnerMode = true;
        ConvertMinConfidence = 50;
        ConvertSingleDollar = true;
        IgnoredSources = new List<string>();
    }

    // DataContractJsonSerializer không chạy constructor: đặt mặc định trước khi đọc để trường thiếu trong tệp cũ vẫn đúng.
    [OnDeserializing]
    private void OnDeserializing(StreamingContext context) => SetDefaults();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MathTypeX", "settings.json");

    public NarySizing NarySizing => GrowIntegrals ? NarySizing.Grow : NarySizing.TeX;

    public static UserSettings Load(string? path = null)
    {
        try
        {
            string file = path ?? DefaultPath;
            if (!File.Exists(file)) return new UserSettings();
            using var stream = File.OpenRead(file);
            var settings = new DataContractJsonSerializer(typeof(UserSettings)).ReadObject(stream) as UserSettings ?? new UserSettings();
            if (string.IsNullOrWhiteSpace(settings.MathFont)) settings.MathFont = "Cambria Math";
            settings.ConvertMinConfidence = Math.Max(0, Math.Min(100, settings.ConvertMinConfidence));
            settings.IgnoredSources ??= new List<string>();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or SerializationException or UnauthorizedAccessException or ArgumentException)
        {
            // Thiết lập hỏng thì dùng mặc định.
            return new UserSettings();
        }
    }

    public void Save(string? path = null)
    {
        try
        {
            string file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var stream = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true))
            {
                new DataContractJsonSerializer(typeof(UserSettings)).WriteObject(writer, this);
            }
            // Ghi ra tệp tạm rồi thay thế: editor và add-in có thể cùng đọc tệp này.
            string temp = file + ".tmp";
            File.WriteAllBytes(temp, stream.ToArray());
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
