namespace Akbura.LanguageServer.UnitTests;

public sealed class SemanticTokenEncoderTests
{
    [Fact]
    public void EncoderSplitsMultilineTokensAndUsesRelativeCoordinates()
    {
        var text = SourceText.From("ab\ncd");
        var classifications = ImmutableArray.Create(
            new AkburaClassifiedSpan(
                new TextSpan(0, text.Length),
                AkburaClassificationKind.String));

        var result = new AkburaSemanticTokenEncoder().Encode(
            text,
            classifications,
            new Utf16PositionConverter());

        Assert.Equal(
            new[] { 0, 0, 2, 14, 0, 1, 0, 2, 14, 0 },
            result.Data);
        Assert.False(string.IsNullOrWhiteSpace(result.ResultId));
    }

    [Fact]
    public void DeltaReplacesOnlyChangedMiddleData()
    {
        var current = new SemanticTokens
        {
            ResultId = "next",
            Data = [1, 2, 9, 4, 5],
        };

        var delta = AkburaSemanticTokenCache.CreateDelta(
            [1, 2, 3, 4, 5],
            current);

        var edit = Assert.Single(delta.Edits);
        Assert.Equal(2, edit.Start);
        Assert.Equal(1, edit.DeleteCount);
        Assert.Equal(new[] { 9 }, edit.Data);
    }

    [Fact]
    public void CacheKeepsCurrentAndPreviousResult()
    {
        var cache = new AkburaSemanticTokenCache();
        var uri = new Uri("file:///tokens.akbura");
        cache.Store(uri, new SemanticTokens { ResultId = "one", Data = [1] });
        cache.Store(uri, new SemanticTokens { ResultId = "two", Data = [2] });

        Assert.True(cache.TryGet(uri, "one", out var previous));
        Assert.Equal(new[] { 1 }, previous);
        Assert.True(cache.TryGet(uri, "two", out var current));
        Assert.Equal(new[] { 2 }, current);
    }
}