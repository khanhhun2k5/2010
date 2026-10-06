using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MathTypeX.Fonts;

/// <summary>
/// Quét font đã cài (docs/04 §7.2): chỉ đọc phần đầu tệp để tìm thẻ "MATH", chỉ parse đầy đủ font toán;
/// kết quả được cache theo (đường dẫn, kích thước, thời điểm sửa).
/// </summary>
public sealed class FontScanner
{
    private static readonly string[] Extensions = { ".otf", ".ttf", ".ttc", ".otc" };

    private readonly FontCache? _cache;

    public FontScanner(FontCache? cache = null) => _cache = cache;

    /// <summary>Thư mục font Office nhìn thấy (Windows) hoặc thư mục hệ thống (Linux/macOS, cho test và CLI).</summary>
    public static IReadOnlyList<string> DefaultDirectories()
    {
        var dirs = new List<string>();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(windows)) dirs.Add(windows);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) dirs.Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dirs.AddRange(new[] { "/usr/share/fonts", "/usr/local/share/fonts", "/usr/share/texmf/fonts/opentype", "/Library/Fonts", "/System/Library/Fonts" });
        if (!string.IsNullOrEmpty(home))
        {
            dirs.Add(Path.Combine(home, ".fonts"));
            dirs.Add(Path.Combine(home, ".local", "share", "fonts"));
            dirs.Add(Path.Combine(home, "Library", "Fonts"));
        }
        return dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Mọi mặt chữ có bảng MATH trong các thư mục, sắp theo tên family.</summary>
    public IReadOnlyList<FontFace> ScanMathFonts(IEnumerable<string>? directories = null)
    {
        var result = new List<FontFace>();
        foreach (var file in EnumerateFontFiles(directories ?? DefaultDirectories()))
        {
            var faces = ReadMathFaces(file);
            result.AddRange(faces);
        }
        _cache?.Save();
        return result
            .GroupBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => StyleRank(f.FullName)).ThenBy(f => f.FullName.Length).First())
            .OrderBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Ưu tiên mặt chữ Regular làm đại diện cho family.</summary>
    private static int StyleRank(string fullName)
    {
        string n = fullName.ToLowerInvariant();
        return (n.Contains("italic") || n.Contains("oblique") ? 2 : 0) + (n.Contains("bold") ? 1 : 0);
    }

    private static IEnumerable<string> EnumerateFontFiles(IEnumerable<string> directories)
    {
        foreach (var dir in directories)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var f in files) yield return f;
        }
    }

    private IReadOnlyList<FontFace> ReadMathFaces(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (_cache is not null && _cache.TryGet(info, out var cached)) return cached;
            IReadOnlyList<FontFace> faces = QuickHasMathTable(file)
                ? OpenTypeReader.ReadFile(file).Where(f => f.HasMathTable).ToArray()
                : Array.Empty<FontFace>();
            _cache?.Put(info, faces);
            return faces;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidFontException)
        {
            return Array.Empty<FontFace>();
        }
    }

    /// <summary>Đọc table directory (vài KB đầu tệp) để biết có bảng MATH không, không đọc cả tệp.</summary>
    public static bool QuickHasMathTable(string file)
    {
        using var stream = File.OpenRead(file);
        var head = new byte[(int)Math.Min(stream.Length, 64 * 1024)];
        int read = 0;
        while (read < head.Length)
        {
            int n = stream.Read(head, read, head.Length - read);
            if (n == 0) break;
            read += n;
        }
        var r = new BigEndianReader(head);
        try
        {
            var offsets = new List<int>();
            if (r.Tag(0) == "ttcf")
            {
                uint count = r.U32(8);
                for (int i = 0; i < count && i < 64; i++) offsets.Add((int)r.U32(12 + 4 * i));
            }
            else
            {
                offsets.Add(0);
            }
            foreach (int o in offsets)
            {
                int numTables = r.U16(o + 4);
                for (int i = 0; i < numTables; i++)
                    if (r.Tag(o + 12 + 16 * i) == "MATH") return true;
            }
        }
        catch (InvalidFontException)
        {
            // Table directory nằm ngoài 64 KB đầu — hiếm; coi như không phải font toán.
        }
        return false;
    }
}

/// <summary>Cache kết quả quét font (JSON) — chỉ quét lại tệp mới hoặc đã thay đổi.</summary>
public sealed class FontCache
{
    private readonly string _path;
    private readonly Dictionary<string, CacheEntry> _entries;
    private bool _dirty;

    private FontCache(string path, Dictionary<string, CacheEntry> entries)
    {
        _path = path;
        _entries = entries;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MathTypeX", "fontcache.json");

    public static FontCache Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var list = (List<CacheEntry>?)new DataContractJsonSerializer(typeof(List<CacheEntry>)).ReadObject(stream);
                if (list is not null) return new FontCache(path, list.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is IOException or SerializationException or UnauthorizedAccessException)
        {
            // Cache hỏng thì quét lại từ đầu.
        }
        return new FontCache(path, new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase));
    }

    private static string KeyOf(FileInfo f) => $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}";

    public bool TryGet(FileInfo file, out IReadOnlyList<FontFace> faces)
    {
        if (_entries.TryGetValue(KeyOf(file), out var entry))
        {
            faces = entry.Faces.Select(f => f.ToFace(file.FullName)).ToArray();
            return true;
        }
        faces = Array.Empty<FontFace>();
        return false;
    }

    public void Put(FileInfo file, IReadOnlyList<FontFace> faces)
    {
        string key = KeyOf(file);
        _entries[key] = new CacheEntry { Key = key, Faces = faces.Select(FaceDto.From).ToList() };
        _dirty = true;
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = File.Create(_path);
            new DataContractJsonSerializer(typeof(List<CacheEntry>)).WriteObject(stream, _entries.Values.ToList());
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [DataContract]
    private sealed class CacheEntry
    {
        [DataMember] public string Key { get; set; } = "";
        [DataMember] public List<FaceDto> Faces { get; set; } = new();
    }

    [DataContract]
    private sealed class FaceDto
    {
        [DataMember] public int FaceIndex { get; set; }
        [DataMember] public string Family { get; set; } = "";
        [DataMember] public string FullName { get; set; } = "";
        [DataMember] public string Version { get; set; } = "";
        [DataMember] public bool IsCff { get; set; }
        [DataMember] public int UnitsPerEm { get; set; }
        [DataMember] public int FsType { get; set; }
        [DataMember] public bool HasMath { get; set; }
        [DataMember] public int ScriptPercent { get; set; }
        [DataMember] public int DisplayOperatorMinHeight { get; set; }
        [DataMember] public int AxisHeight { get; set; }
        [DataMember] public int IntegralVariants { get; set; } = -1;
        [DataMember] public bool IntegralAssembly { get; set; }
        [DataMember] public int IntegralLargest { get; set; }
        /// <summary>Các dải code point dạng [s0, e0, s1, e1, …].</summary>
        [DataMember] public List<int> Coverage { get; set; } = new();

        public static FaceDto From(FontFace f) => new()
        {
            FaceIndex = f.FaceIndex,
            Family = f.Family,
            FullName = f.FullName,
            Version = f.Version,
            IsCff = f.IsCff,
            UnitsPerEm = f.UnitsPerEm,
            FsType = f.FsType,
            HasMath = f.Math is not null,
            ScriptPercent = f.Math?.ScriptPercentScaleDown ?? 0,
            DisplayOperatorMinHeight = f.Math?.DisplayOperatorMinHeight ?? 0,
            AxisHeight = f.Math?.AxisHeight ?? 0,
            IntegralVariants = f.Math?.Integral?.VariantCount ?? -1,
            IntegralAssembly = f.Math?.Integral?.HasAssembly ?? false,
            IntegralLargest = f.Math?.Integral?.LargestVariantAdvance ?? 0,
            Coverage = f.Coverage.Ranges.SelectMany(r => new[] { r.Start, r.End }).ToList(),
        };

        public FontFace ToFace(string path)
        {
            var ranges = new List<(int, int)>();
            for (int i = 0; i + 1 < Coverage.Count; i += 2) ranges.Add((Coverage[i], Coverage[i + 1]));
            MathTableInfo? math = HasMath
                ? new MathTableInfo((short)ScriptPercent, (ushort)DisplayOperatorMinHeight, (short)AxisHeight,
                    IntegralVariants < 0 ? null : new GlyphConstructionInfo(IntegralVariants, IntegralAssembly, IntegralLargest))
                : null;
            return new FontFace(path, FaceIndex, Family, FullName, Version, IsCff, (ushort)UnitsPerEm, (ushort)FsType, math, new CodePointSet(ranges));
        }
    }
}
