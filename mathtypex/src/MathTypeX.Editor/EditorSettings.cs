using System.IO;
using System.Text.Json;

namespace MathTypeX.Editor;

/// <summary>Thiết lập cá nhân (lưu ở %APPDATA%\MathTypeX\settings.json, không gửi đi đâu).</summary>
internal sealed class EditorSettings
{
    public string MathFont { get; set; } = "Cambria Math";
    public bool PreferDisplay { get; set; }
    public bool DecimalComma { get; set; }
    public bool UprightDifferential { get; set; }
    public bool GrowIntegrals { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MathTypeX", "settings.json");

    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath)) ?? new EditorSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Thiết lập hỏng thì dùng mặc định.
        }
        return new EditorSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
