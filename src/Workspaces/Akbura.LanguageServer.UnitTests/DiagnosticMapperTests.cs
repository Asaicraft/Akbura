using Akbura.Language.Syntax;

namespace Akbura.LanguageServer.UnitTests;

public sealed class DiagnosticMapperTests
{
    [Fact]
    public void MapsSeverityCodeMessageAndUtf16Range()
    {
        var text = SourceText.From("😀error");
        var source = ImmutableArray.Create(
            new AkburaDiagnosticSpan(
                new TextSpan(2, 5),
                "AKBURA_TEST",
                "broken",
                AkburaDiagnosticSeverity.Error));

        var mapped = AkburaProtocolMapper.ToDiagnostics(
            text,
            source,
            new Utf16PositionConverter());

        var diagnostic = Assert.Single(mapped);
        Assert.Equal(1, diagnostic.Severity);
        Assert.Equal("AKBURA_TEST", diagnostic.Code);
        Assert.Equal(2, diagnostic.Range.Start.Character);
        Assert.Equal(7, diagnostic.Range.End.Character);
    }
}