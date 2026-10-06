using System.Text;

namespace MathTypeX.Fonts;

/// <summary>Đọc dữ liệu big-endian của OpenType; mọi lỗi định dạng ném <see cref="InvalidFontException"/>.</summary>
internal sealed class BigEndianReader
{
    private readonly byte[] _data;

    public BigEndianReader(byte[] data) => _data = data;

    public int Length => _data.Length;

    private void Check(int offset, int size)
    {
        if (offset < 0 || size < 0 || offset + size > _data.Length)
            throw new InvalidFontException($"Đọc ngoài phạm vi tệp font (offset {offset}, {size} byte).");
    }

    public byte U8(int o)
    {
        Check(o, 1);
        return _data[o];
    }

    public ushort U16(int o)
    {
        Check(o, 2);
        return (ushort)((_data[o] << 8) | _data[o + 1]);
    }

    public short I16(int o) => unchecked((short)U16(o));

    public uint U32(int o)
    {
        Check(o, 4);
        return (uint)((_data[o] << 24) | (_data[o + 1] << 16) | (_data[o + 2] << 8) | _data[o + 3]);
    }

    public string Tag(int o)
    {
        Check(o, 4);
        return Encoding.ASCII.GetString(_data, o, 4);
    }

    public string Utf16BE(int o, int length)
    {
        Check(o, length);
        var chars = new char[length / 2];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)((_data[o + 2 * i] << 8) | _data[o + 2 * i + 1]);
        return new string(chars);
    }

    public string Latin1(int o, int length)
    {
        Check(o, length);
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = (char)_data[o + i];
        return new string(chars);
    }
}

public sealed class InvalidFontException : Exception
{
    public InvalidFontException(string message) : base(message)
    {
    }
}

/// <summary>Thông tin về ký hiệu ∫ trong bảng MATH (MathVariants) — dùng để chẩn đoán chế độ Grow.</summary>
public sealed record GlyphConstructionInfo(int VariantCount, bool HasAssembly, int LargestVariantAdvance);

/// <summary>Thông tin về bảng MATH của font.</summary>
public sealed record MathTableInfo(
    short ScriptPercentScaleDown,
    ushort DisplayOperatorMinHeight,
    short AxisHeight,
    GlyphConstructionInfo? Integral);

/// <summary>Một mặt chữ (face) trong tệp .ttf/.otf/.ttc.</summary>
public sealed record FontFace(
    string Path,
    int FaceIndex,
    string Family,
    string FullName,
    string Version,
    bool IsCff,
    ushort UnitsPerEm,
    ushort FsType,
    MathTableInfo? Math,
    CodePointSet Coverage)
{
    public bool HasMathTable => Math is not null;

    /// <summary>fsType bit 1 (0x0002) = "Restricted License embedding" — không được nhúng.</summary>
    public bool EmbeddingRestricted => (FsType & 0x000F) == 0x0002;
}

/// <summary>
/// Bộ đọc OpenType tối giản, chỉ đọc (docs/04 §7.2): table directory (cả .ttc), name, head, OS/2, cmap, MATH.
/// Không phụ thuộc thư viện ngoài.
/// </summary>
public static class OpenTypeReader
{
    public static IReadOnlyList<FontFace> ReadFile(string path) => Read(File.ReadAllBytes(path), path);

    public static IReadOnlyList<FontFace> Read(byte[] data, string path = "")
    {
        var r = new BigEndianReader(data);
        if (data.Length < 12) throw new InvalidFontException("Tệp quá ngắn để là font.");
        if (r.Tag(0) == "ttcf")
        {
            uint count = r.U32(8);
            if (count == 0 || count > 1024) throw new InvalidFontException("Số mặt chữ trong TTC không hợp lệ.");
            var faces = new List<FontFace>((int)count);
            for (int i = 0; i < count; i++)
                faces.Add(ReadFace(r, (int)r.U32(12 + 4 * i), path, i));
            return faces;
        }
        return new[] { ReadFace(r, 0, path, 0) };
    }

    private static FontFace ReadFace(BigEndianReader r, int offset, string path, int index)
    {
        uint version = r.U32(offset);
        bool cff = version == 0x4F54544F; // 'OTTO'
        if (!cff && version != 0x00010000 && version != 0x74727565) // 'true'
            throw new InvalidFontException($"Không phải font OpenType/TrueType (sfnt version 0x{version:X8}).");

        int numTables = r.U16(offset + 4);
        var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        for (int i = 0; i < numTables; i++)
        {
            int rec = offset + 12 + 16 * i;
            tables[r.Tag(rec)] = ((int)r.U32(rec + 8), (int)r.U32(rec + 12));
        }

        var names = tables.TryGetValue("name", out var name) ? ReadNames(r, name.Offset) : new Dictionary<int, string>();
        string family = names.TryGetValue(16, out var typo) ? typo : names.TryGetValue(1, out var fam) ? fam : System.IO.Path.GetFileNameWithoutExtension(path);
        string full = names.TryGetValue(4, out var f) ? f : family;
        string ver = names.TryGetValue(5, out var v) ? v : "";

        ushort upem = tables.TryGetValue("head", out var head) ? r.U16(head.Offset + 18) : (ushort)1000;
        ushort fsType = tables.TryGetValue("OS/2", out var os2) ? r.U16(os2.Offset + 8) : (ushort)0;

        var cmap = tables.TryGetValue("cmap", out var cm) ? CharacterMap.Read(r, cm.Offset) : CharacterMap.Empty;
        MathTableInfo? math = tables.TryGetValue("MATH", out var mt) ? ReadMath(r, mt.Offset, cmap) : null;

        return new FontFace(path, index, family, full, ver, cff, upem, fsType, math, cmap.Coverage);
    }

    /// <summary>Bảng name: ưu tiên Windows/Unicode tiếng Anh (3,1,0x409), rồi Unicode, rồi Macintosh Roman.</summary>
    private static Dictionary<int, string> ReadNames(BigEndianReader r, int o)
    {
        int count = r.U16(o + 2);
        int storage = o + r.U16(o + 4);
        var best = new Dictionary<int, (int Score, string Value)>();
        for (int i = 0; i < count; i++)
        {
            int rec = o + 6 + 12 * i;
            int platform = r.U16(rec), encoding = r.U16(rec + 2), language = r.U16(rec + 4), nameId = r.U16(rec + 6);
            int length = r.U16(rec + 8), offset = r.U16(rec + 10);
            if (nameId is not (1 or 4 or 5 or 16)) continue;
            int score;
            string value;
            if (platform == 3 && encoding is 1 or 10)
            {
                score = language == 0x409 ? 3 : 2;
                value = r.Utf16BE(storage + offset, length);
            }
            else if (platform == 0)
            {
                score = 1;
                value = r.Utf16BE(storage + offset, length);
            }
            else if (platform == 1 && encoding == 0)
            {
                score = 0;
                value = r.Latin1(storage + offset, length);
            }
            else
            {
                continue;
            }
            if (!best.TryGetValue(nameId, out var current) || current.Score < score)
                best[nameId] = (score, value.Trim());
        }
        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
    }

    // ── MATH ─────────────────────────────────────────────────────────────

    private static MathTableInfo ReadMath(BigEndianReader r, int o, CharacterMap cmap)
    {
        int constants = o + r.U16(o + 4);
        int variants = o + r.U16(o + 8);
        short scriptPercent = r.I16(constants);
        ushort displayOpMin = r.U16(constants + 6);
        short axisHeight = r.I16(constants + 12); // MathValueRecord AxisHeight: value ở offset 12
        GlyphConstructionInfo? integral = null;
        int glyph = cmap.GlyphId(0x222B);
        if (glyph > 0 && r.U16(o + 8) != 0) integral = ReadVerticalConstruction(r, variants, glyph);
        return new MathTableInfo(scriptPercent, displayOpMin, axisHeight, integral);
    }

    private static GlyphConstructionInfo? ReadVerticalConstruction(BigEndianReader r, int variants, int glyph)
    {
        int coverageOffset = r.U16(variants + 2);
        int vertCount = r.U16(variants + 6);
        if (coverageOffset == 0 || vertCount == 0) return null;
        int coverageIndex = CoverageIndex(r, variants + coverageOffset, glyph);
        if (coverageIndex < 0 || coverageIndex >= vertCount) return null;
        int construction = variants + r.U16(variants + 10 + 2 * coverageIndex);
        int assemblyOffset = r.U16(construction);
        int variantCount = r.U16(construction + 2);
        int largest = 0;
        for (int i = 0; i < variantCount; i++)
            largest = Math.Max(largest, r.U16(construction + 4 + 4 * i + 2));
        return new GlyphConstructionInfo(variantCount, assemblyOffset != 0, largest);
    }

    /// <summary>Tra glyph trong bảng Coverage (format 1: danh sách; format 2: dải).</summary>
    private static int CoverageIndex(BigEndianReader r, int o, int glyph)
    {
        int format = r.U16(o);
        if (format == 1)
        {
            int count = r.U16(o + 2);
            for (int i = 0; i < count; i++)
                if (r.U16(o + 4 + 2 * i) == glyph) return i;
        }
        else if (format == 2)
        {
            int count = r.U16(o + 2);
            for (int i = 0; i < count; i++)
            {
                int rec = o + 4 + 6 * i;
                int start = r.U16(rec), end = r.U16(rec + 2), startIndex = r.U16(rec + 4);
                if (glyph >= start && glyph <= end) return startIndex + glyph - start;
            }
        }
        return -1;
    }
}
