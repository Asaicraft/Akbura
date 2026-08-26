namespace Akbura.LanguageServer.UnitTests;

public sealed class PositionConverterTests
{
    private readonly Utf16PositionConverter _converter = new();

    [Fact]
    public void ConvertsCrLfAndSurrogatePairsInUtf16CodeUnits()
    {
        var text = SourceText.From("a😀b\r\nкириллица\n");

        Assert.Equal(
            3,
            _converter.ToOffset(
                text,
                new Position { Line = 0, Character = 3 }));
        Assert.Equal(
            1,
            _converter.ToPosition(text, 7).Line);
        Assert.Equal(
            1,
            _converter.ToPosition(text, 7).Character);
    }

    [Fact]
    public void RoundTripsRangesIncludingEmptyFinalLine()
    {
        var text = SourceText.From("one\ntwo\n");
        var span = TextSpan.FromBounds(2, text.Length);

        var range = _converter.ToRange(text, span);

        Assert.Equal(span, _converter.ToTextSpan(text, range));
        Assert.Equal(2, range.End.Line);
        Assert.Equal(0, range.End.Character);
    }

    [Fact]
    public void RejectsPositionPastLineEnd()
    {
        var text = SourceText.From("abc");

        Assert.Throws<AkburaProtocolException>(() =>
            _converter.ToOffset(
                text,
                new Position { Line = 0, Character = 4 }));
    }
}