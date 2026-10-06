namespace MathTypeX.Fonts;

/// <summary>
/// Tập code point dạng các dải [start, end] đã sắp xếp — gọn để lưu cache (một font toán ~ vài trăm dải).
/// </summary>
public sealed class CodePointSet
{
    private readonly int[] _starts;
    private readonly int[] _ends;

    public CodePointSet(IEnumerable<(int Start, int End)> ranges)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges.Where(x => x.End >= x.Start).OrderBy(x => x.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[merged.Count - 1].End + 1)
                merged[merged.Count - 1] = (merged[merged.Count - 1].Start, Math.Max(merged[merged.Count - 1].End, range.End));
            else
                merged.Add(range);
        }
        _starts = merged.Select(m => m.Start).ToArray();
        _ends = merged.Select(m => m.End).ToArray();
    }

    public static CodePointSet Empty { get; } = new(Array.Empty<(int, int)>());

    public IEnumerable<(int Start, int End)> Ranges => _starts.Select((s, i) => (s, _ends[i]));

    public int RangeCount => _starts.Length;

    public bool Contains(int codePoint)
    {
        int lo = 0, hi = _starts.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (codePoint < _starts[mid]) hi = mid - 1;
            else if (codePoint > _ends[mid]) lo = mid + 1;
            else return true;
        }
        return false;
    }

    public bool Contains(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? char.ConvertToUtf32(text[i], text[++i]) : text[i];
            if (!Contains(cp)) return false;
        }
        return true;
    }
}

/// <summary>Bảng cmap: chọn subtable Unicode tốt nhất (3,10 format 12 → 3,1 format 4 → 0,x).</summary>
internal sealed class CharacterMap
{
    private readonly Func<int, int> _lookup;

    private CharacterMap(Func<int, int> lookup, CodePointSet coverage)
    {
        _lookup = lookup;
        Coverage = coverage;
    }

    public static CharacterMap Empty { get; } = new(_ => 0, CodePointSet.Empty);

    public CodePointSet Coverage { get; }

    public int GlyphId(int codePoint) => _lookup(codePoint);

    public static CharacterMap Read(BigEndianReader r, int o)
    {
        int count = r.U16(o + 2);
        int best = -1, bestScore = -1;
        for (int i = 0; i < count; i++)
        {
            int rec = o + 4 + 8 * i;
            int platform = r.U16(rec), encoding = r.U16(rec + 2);
            int sub = o + (int)r.U32(rec + 4);
            int format = r.U16(sub);
            int score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 5,
                (0, _, 12) => 4,
                (3, 1, 4) => 3,
                (0, _, 4) => 2,
                _ => -1,
            };
            if (score > bestScore)
            {
                bestScore = score;
                best = sub;
            }
        }
        if (best < 0) return Empty;
        return r.U16(best) == 12 ? ReadFormat12(r, best) : ReadFormat4(r, best);
    }

    private static CharacterMap ReadFormat12(BigEndianReader r, int o)
    {
        int groups = (int)r.U32(o + 12);
        var starts = new int[groups];
        var ends = new int[groups];
        var glyphs = new int[groups];
        for (int i = 0; i < groups; i++)
        {
            int rec = o + 16 + 12 * i;
            starts[i] = (int)r.U32(rec);
            ends[i] = (int)r.U32(rec + 4);
            glyphs[i] = (int)r.U32(rec + 8);
        }
        int Lookup(int cp)
        {
            for (int i = 0; i < groups; i++)
                if (cp >= starts[i] && cp <= ends[i]) return glyphs[i] + (cp - starts[i]);
            return 0;
        }
        return new CharacterMap(Lookup, new CodePointSet(Enumerable.Range(0, groups).Select(i => (starts[i], ends[i]))));
    }

    private static CharacterMap ReadFormat4(BigEndianReader r, int o)
    {
        int segCount = r.U16(o + 6) / 2;
        int endsAt = o + 14;
        int startsAt = endsAt + 2 * segCount + 2;
        int deltasAt = startsAt + 2 * segCount;
        int rangeOffsetsAt = deltasAt + 2 * segCount;

        int Lookup(int cp)
        {
            if (cp > 0xFFFF) return 0;
            for (int s = 0; s < segCount; s++)
            {
                int end = r.U16(endsAt + 2 * s);
                if (cp > end) continue;
                int start = r.U16(startsAt + 2 * s);
                if (cp < start) return 0;
                int delta = r.I16(deltasAt + 2 * s);
                int rangeOffset = r.U16(rangeOffsetsAt + 2 * s);
                if (rangeOffset == 0) return (cp + delta) & 0xFFFF;
                int glyphAt = rangeOffsetsAt + 2 * s + rangeOffset + 2 * (cp - start);
                int glyph = r.U16(glyphAt);
                return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
            }
            return 0;
        }

        // Coverage chính xác: chỉ tính code point thực sự ánh xạ tới một glyph khác 0.
        var ranges = new List<(int, int)>();
        for (int s = 0; s < segCount; s++)
        {
            int start = r.U16(startsAt + 2 * s), end = r.U16(endsAt + 2 * s);
            if (start == 0xFFFF) continue;
            int runStart = -1;
            for (int cp = start; cp <= end; cp++)
            {
                bool mapped = Lookup(cp) != 0;
                if (mapped && runStart < 0) runStart = cp;
                if (!mapped && runStart >= 0)
                {
                    ranges.Add((runStart, cp - 1));
                    runStart = -1;
                }
            }
            if (runStart >= 0) ranges.Add((runStart, end));
        }
        return new CharacterMap(Lookup, new CodePointSet(ranges));
    }
}
