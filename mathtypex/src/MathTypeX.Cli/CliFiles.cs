namespace MathTypeX.Cli;

/// <summary>
/// Đường dẫn vào/ra của mọi lệnh CLI. Lệnh có <c>--out</c> không bao giờ đòi người gọi tạo thư mục trước:
/// thư mục cha (kể cả nhiều cấp lồng nhau) được tạo tự động; tệp vào phải tồn tại; tệp ra phải thật sự được ghi.
/// </summary>
internal static class CliFiles
{
    /// <summary>Tệp đầu vào bắt buộc; thiếu hoặc không tồn tại thì báo lỗi rõ ràng (mã thoát 2).</summary>
    public static string RequireInput(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"Thiếu {what}.");
        if (Directory.Exists(path)) throw new ArgumentException($"{what} phải là một tệp, không phải thư mục: {Path.GetFullPath(path)}");
        if (!File.Exists(path)) throw new FileNotFoundException($"Không tìm thấy {what}: {Path.GetFullPath(path)}", path);
        return path;
    }

    /// <summary>
    /// Chuẩn bị tệp đầu ra: trả về đường dẫn tuyệt đối và tạo mọi thư mục cha còn thiếu.
    /// Báo lỗi nếu đường dẫn trỏ vào một thư mục có sẵn.
    /// </summary>
    public static string PrepareOutput(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Thiếu đường dẫn cho --out.");
        string full = Path.GetFullPath(path);
        if (Directory.Exists(full)) throw new ArgumentException($"--out phải là một tệp, không phải thư mục: {full}");
        string? directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        return full;
    }

    /// <summary>Sau khi ghi: tệp phải tồn tại và khác rỗng — không để lệnh "thành công" mà không có kết quả.</summary>
    public static void EnsureWritten(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new IOException($"Không tạo được tệp {path}.");
        if (info.Length == 0) throw new IOException($"Tệp {path} rỗng (0 byte).");
    }
}
