using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Akbura.LanguageServer.Protocol.Serialization;
using System.Text.Json;

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
        Assert.Equal("broken", diagnostic.Message);
        Assert.Equal("Akbura", diagnostic.Source);
    }

    [Fact]
    public void PreservesCanonicalIdentityPropertiesAndRelatedLocationsAcrossJson()
    {
        var text = SourceText.From("😀\r\nОшибка");
        var span = new TextSpan(4, 6);
        var relatedPath = Path.GetFullPath("Связанный.akcss");
        var canonical = new AkburaDiagnosticRecord
        {
            Id = "AKBURA_TEST_CANONICAL",
            Severity = AkburaDiagnosticSeverity.Warning,
            Message = "Ошибка свойства 😀",
            FilePath = Path.GetFullPath("Представление.akbura"),
            Span = span,
            LineSpan = text.Lines.GetLinePositionSpan(span),
            Kind = AkburaDiagnosticKind.Semantic,
            AdditionalLocations =
            [
                new AkburaDiagnosticLocation(
                    relatedPath,
                    new TextSpan(12, 4),
                    new LinePositionSpan(new LinePosition(2, 2), new LinePosition(2, 6))),
            ],
            Properties = ImmutableDictionary<string, string?>.Empty.Add("property", "Ширина"),
            Provenance = DiagnosticProvenance.AkburaCore | DiagnosticProvenance.Workspaces,
            DocumentVersion = 1,
        };
        var source = ImmutableArray.Create(new AkburaDiagnosticSpan(
            canonical.Span,
            canonical.Id,
            canonical.Message,
            canonical.Severity)
        {
            CanonicalDiagnostic = canonical,
        });

        var mapped = Assert.Single(AkburaProtocolMapper.ToDiagnostics(
            text,
            source,
            new Utf16PositionConverter(),
            documentVersion: 7));
        var json = JsonSerializer.Serialize(mapped, AkburaProtocolJson.CreateOptions());
        var diagnostic = JsonSerializer.Deserialize<Protocol.Diagnostic>(json, AkburaProtocolJson.CreateOptions())!;

        Assert.Equal("Akbura", diagnostic.Source);
        Assert.Equal(canonical.Id, diagnostic.Code);
        Assert.Equal(canonical.Message, diagnostic.Message);
        Assert.Equal(span, new Utf16PositionConverter().ToTextSpan(text, diagnostic.Range));
        var data = diagnostic.Data!.Value;
        Assert.Equal(canonical.LogicalId, data.GetProperty("akbura.logical-id").GetString());
        Assert.Equal("7", data.GetProperty("akbura.document-version").GetString());
        Assert.Equal("Ширина", data.GetProperty("property").GetString());
        Assert.Contains("language-server", data.GetProperty("akbura.origin").GetString(), StringComparison.Ordinal);
        var related = Assert.Single(diagnostic.RelatedInformation!);
        Assert.Equal(new Uri(relatedPath).AbsoluteUri, related.Location.Uri);
        Assert.Equal(2, related.Location.Range.Start.Line);
        Assert.Equal(2, related.Location.Range.Start.Character);
        Assert.Equal(6, related.Location.Range.End.Character);
        Assert.Equal(1, source[0].CanonicalDiagnostic!.Value.DocumentVersion);
    }

    [Theory]
    [InlineData("первый\r\n😀ошибка\r\nпоследний", "ошибка", 1, 2, 1, 8)]
    [InlineData("α\r\n😀ошибка\r\n𐐀tail", "ошибка\r\n𐐀tail", 1, 2, 2, 6)]
    public void MapsCrLfAndNonAsciiLocationsFromTheSameSourceText(
        string source,
        string markedText,
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter)
    {
        var text = SourceText.From(source);
        var span = new TextSpan(source.IndexOf(markedText, StringComparison.Ordinal), markedText.Length);
        var positions = new Utf16PositionConverter();
        var diagnostics = ImmutableArray.Create(new AkburaDiagnosticSpan(
            span,
            "AKBURA_TEST_UNICODE",
            "Ошибка свойства 😀",
            AkburaDiagnosticSeverity.Warning));

        var diagnostic = Assert.Single(AkburaProtocolMapper.ToDiagnostics(text, diagnostics, positions));

        Assert.Equal("AKBURA_TEST_UNICODE", diagnostic.Code);
        Assert.Equal("Ошибка свойства 😀", diagnostic.Message);
        Assert.Equal(2, diagnostic.Severity);
        Assert.Equal(startLine, diagnostic.Range.Start.Line);
        Assert.Equal(startCharacter, diagnostic.Range.Start.Character);
        Assert.Equal(endLine, diagnostic.Range.End.Line);
        Assert.Equal(endCharacter, diagnostic.Range.End.Character);
        Assert.Equal(span, positions.ToTextSpan(text, diagnostic.Range));
        Assert.Equal(
            text.Lines.GetLinePositionSpan(span),
            new LinePositionSpan(
                new LinePosition(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character),
                new LinePosition(diagnostic.Range.End.Line, diagnostic.Range.End.Character)));
    }

    [Fact]
    public void MapsZeroWidthEndOfFileDiagnosticWithoutExpandingCanonicalSpan()
    {
        var text = SourceText.From("😀\r\nя");
        var span = new TextSpan(text.Length, 0);
        var positions = new Utf16PositionConverter();
        var diagnostics = ImmutableArray.Create(new AkburaDiagnosticSpan(
            span,
            "AKBURA_TEST_EOF",
            "Expected token",
            AkburaDiagnosticSeverity.Error));

        var diagnostic = Assert.Single(AkburaProtocolMapper.ToDiagnostics(text, diagnostics, positions));

        Assert.Equal(1, diagnostic.Range.Start.Line);
        Assert.Equal(1, diagnostic.Range.Start.Character);
        Assert.Equal(1, diagnostic.Range.End.Line);
        Assert.Equal(1, diagnostic.Range.End.Character);
        Assert.Equal(span, positions.ToTextSpan(text, diagnostic.Range));
    }
}
