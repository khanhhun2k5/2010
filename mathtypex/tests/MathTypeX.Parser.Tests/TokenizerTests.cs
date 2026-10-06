namespace MathTypeX.Parser.Tests;

public class TokenizerTests
{
    private static List<Token> Tok(string s, List<Diagnostic>? d = null) => Tokenizer.Tokenize(s, d ?? new List<Diagnostic>());

    [Fact]
    public void ControlWordSkipsFollowingSpaces()
    {
        var t = Tok(@"\alpha  x");
        Assert.Equal(TokenKind.ControlWord, t[0].Kind);
        Assert.Equal("alpha", t[0].Text);
        Assert.Equal(TokenKind.Letter, t[1].Kind);
        Assert.Equal(TokenKind.EndOfInput, t[2].Kind);
    }

    [Fact]
    public void ControlSymbolAndSpans()
    {
        var t = Tok(@"a\,b");
        Assert.Equal(TokenKind.ControlSymbol, t[1].Kind);
        Assert.Equal(",", t[1].Text);
        Assert.Equal(new SourceSpan(1, 2), t[1].Span);
    }

    [Fact]
    public void CommentIsSkipped()
    {
        var t = Tok("x % chú thích\ny");
        Assert.Equal(5, t.Count);
        Assert.Equal("y", t[3].Text);
    }

    [Fact]
    public void CaretNotationIsRejected()
    {
        var d = new List<Diagnostic>();
        var t = Tok("^^5cinput", d);
        Assert.Contains(d, x => x.Code == DiagnosticCode.UnsupportedCaretNotation);
        Assert.DoesNotContain(t, x => x.Kind == TokenKind.ControlWord);
    }

    [Fact]
    public void SurrogatePairIsOneToken()
    {
        var t = Tok("𝔤");
        Assert.Equal(TokenKind.Letter, t[0].Kind);
        Assert.Equal("𝔤", t[0].Text);
        Assert.Equal(2, t[0].Span.Length);
    }

    [Fact]
    public void TrailingBackslashReportsIncompleteCommand()
    {
        var d = new List<Diagnostic>();
        Tok("x\\", d);
        Assert.Contains(d, x => x.Code == DiagnosticCode.IncompleteCommand);
    }
}
