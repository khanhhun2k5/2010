using System.Text;

namespace MathTypeX.Fonts.Tests;

/// <summary>Dựng một font OpenType tối giản trong bộ nhớ để test bộ đọc không cần tệp font thật.</summary>
internal static class SyntheticFont
{
    private sealed class Writer
    {
        private readonly List<byte> _b = new();
        public int Position => _b.Count;
        public Writer U16(int v) { _b.Add((byte)(v >> 8)); _b.Add((byte)v); return this; }
        public Writer I16(int v) => U16(v & 0xFFFF);
        public Writer U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); return this; }
        public Writer Bytes(byte[] bytes) { _b.AddRange(bytes); return this; }
        public Writer Tag(string tag) => Bytes(Encoding.ASCII.GetBytes(tag));
        public Writer Pad4() { while (_b.Count % 4 != 0) _b.Add(0); return this; }
        public void SetU16(int at, int v) { _b[at] = (byte)(v >> 8); _b[at + 1] = (byte)v; }
        public byte[] ToArray() => _b.ToArray();
    }

    private static byte[] Name(string family)
    {
        var strings = new[] { (Id: 1, Text: family), (Id: 4, Text: family + " Regular"), (Id: 5, Text: "Version 1.000") };
        var w = new Writer().U16(0).U16(strings.Length).U16(6 + 12 * strings.Length);
        int offset = 0;
        var storage = new List<byte>();
        foreach (var (id, text) in strings)
        {
            byte[] data = Encoding.BigEndianUnicode.GetBytes(text);
            w.U16(3).U16(1).U16(0x409).U16(id).U16(data.Length).U16(offset);
            storage.AddRange(data);
            offset += data.Length;
        }
        return w.Bytes(storage.ToArray()).ToArray();
    }

    private static byte[] Head() => new Writer().U32(0x00010000).U32(0x00010000).U32(0).U32(0x5F0F3CF5).U16(0).U16(1000)
        .Bytes(new byte[54 - 20]).ToArray();

    private static byte[] Os2(int fsType) => new Writer().U16(4).I16(500).U16(400).U16(5).U16(fsType).Bytes(new byte[86]).ToArray();

    /// <summary>cmap format 4: A–Z → glyph 1–26, ∫ (U+222B) → glyph 30.</summary>
    private static byte[] Cmap()
    {
        var segments = new[] { (Start: 0x41, End: 0x5A, Glyph: 1), (Start: 0x222B, End: 0x222B, Glyph: 30), (Start: 0xFFFF, End: 0xFFFF, Glyph: 0) };
        int segCount = segments.Length;
        var sub = new Writer().U16(4).U16(16 + 8 * segCount).U16(0).U16(segCount * 2).U16(4).U16(1).U16(2);
        foreach (var s in segments) sub.U16(s.End);
        sub.U16(0);
        foreach (var s in segments) sub.U16(s.Start);
        foreach (var s in segments) sub.I16(s.Start == 0xFFFF ? 1 : s.Glyph - s.Start);
        foreach (var _ in segments) sub.U16(0);
        return new Writer().U16(0).U16(1).U16(3).U16(1).U32(12).Bytes(sub.ToArray()).ToArray();
    }

    /// <summary>Bảng MATH: hằng số + ∫ (glyph 30) có 3 biến thể và một glyph assembly.</summary>
    private static byte[] Math()
    {
        var constants = new Writer().I16(70).I16(50).U16(1300).U16(1450).I16(150).U16(0).I16(250).U16(0).ToArray();
        var variants = new Writer();
        variants.U16(20);              // minConnectorOverlap
        int coverageAt = variants.Position; variants.U16(0);
        variants.U16(0);               // horizGlyphCoverageOffset
        variants.U16(1).U16(0);        // vertGlyphCount, horizGlyphCount
        int constructionAt = variants.Position; variants.U16(0);
        int construction = variants.Position;
        variants.SetU16(constructionAt, construction);
        int assemblyAt = variants.Position; variants.U16(0);
        variants.U16(3).U16(30).U16(1000).U16(31).U16(1500).U16(32).U16(2200);
        int coverage = variants.Position;
        variants.SetU16(coverageAt, coverage);
        variants.U16(1).U16(1).U16(30);
        int assembly = variants.Position;
        variants.SetU16(assemblyAt, assembly - construction);
        variants.U16(0).U16(0); // MathValueRecord italic correction (rỗng) + partCount 0
        var v = variants.ToArray();

        int constantsOffset = 10, variantsOffset = constantsOffset + constants.Length;
        return new Writer().U32(0x00010000).U16(constantsOffset).U16(0).U16(variantsOffset).Bytes(constants).Bytes(v).ToArray();
    }

    public static byte[] Build(string family = "Test Math", bool withMath = true, bool cff = true, int fsType = 8)
    {
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = Cmap(),
            ["head"] = Head(),
            ["name"] = Name(family),
            ["OS/2"] = Os2(fsType),
        };
        if (withMath) tables["MATH"] = Math();
        return Sfnt(tables, cff, 0);
    }

    private static byte[] Sfnt(SortedDictionary<string, byte[]> tables, bool cff, int baseOffset)
    {
        var w = new Writer().U32(cff ? 0x4F54544Fu : 0x00010000u).U16(tables.Count).U16(0).U16(0).U16(0);
        int offset = 12 + 16 * tables.Count;
        var bodies = new List<byte[]>();
        foreach (var (tag, data) in tables)
        {
            w.Tag(tag).U32(0).U32((uint)(baseOffset + offset)).U32((uint)data.Length);
            int padded = (data.Length + 3) & ~3;
            offset += padded;
            bodies.Add(data);
        }
        foreach (var data in bodies) w.Bytes(data).Pad4();
        return w.ToArray();
    }

    /// <summary>TTC chứa hai mặt chữ: một font toán và một font thường.</summary>
    public static byte[] Collection()
    {
        // Mỗi face được dựng với offset tuyệt đối tính từ đầu tệp TTC.
        int header = 12 + 4 * 2;
        var first = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = Cmap(), ["head"] = Head(), ["name"] = Name("Collection Text"), ["OS/2"] = Os2(0),
        };
        byte[] face0 = Sfnt(first, cff: false, baseOffset: header);
        var second = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = Cmap(), ["head"] = Head(), ["name"] = Name("Collection Math"), ["OS/2"] = Os2(0), ["MATH"] = Math(),
        };
        byte[] face1 = Sfnt(second, cff: false, baseOffset: header + face0.Length);
        return new Writer().Tag("ttcf").U32(0x00010000).U32(2).U32((uint)header).U32((uint)(header + face0.Length))
            .Bytes(face0).Bytes(face1).ToArray();
    }
}
