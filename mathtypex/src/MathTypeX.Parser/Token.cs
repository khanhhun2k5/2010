namespace MathTypeX.Parsing;

public enum TokenKind
{
    EndOfInput,
    ControlWord,   // \frac  (Text = "frac")
    ControlSymbol, // \,     (Text = ",")
    BeginGroup,    // {
    EndGroup,      // }
    MathShift,     // $
    Alignment,     // &
    Superscript,   // ^
    Subscript,     // _
    Letter,
    Digit,
    Other,
    Space,
    Active,        // ~
    Parameter,     // #
}

public readonly record struct Token(TokenKind Kind, string Text, SourceSpan Span)
{
    public bool IsWord(string name) => Kind == TokenKind.ControlWord && Text == name;

    public bool IsOther(string text) => Kind == TokenKind.Other && Text == text;

    public override string ToString() => $"{Kind}:{Text}@{Span}";
}
