namespace MathTypeX;

/// <summary>Vị trí một đoạn trong source gốc (đơn vị: UTF-16 code unit).</summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static SourceSpan Empty => default;

    public static SourceSpan FromBounds(int start, int end) => new(start, Math.Max(0, end - start));

    public SourceSpan Union(SourceSpan other)
    {
        if (Length == 0 && Start == 0) return other;
        if (other.Length == 0 && other.Start == 0) return this;
        return FromBounds(Math.Min(Start, other.Start), Math.Max(End, other.End));
    }

    public override string ToString() => $"[{Start}..{End})";
}
