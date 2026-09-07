using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Diagnostics;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class CanonicalDiagnosticServiceTests
{
    [Theory]
    [InlineData("akbura.module-identity")]
    [InlineData("akbura.module-reference")]
    public void ProjectionDoesNotPublishEmbeddedModuleDiagnosticsThatShareTheLocalFilePath(string moduleProperty)
    {
        var text = SourceText.From("<UnknownControl/>");
        var filePath = Path.GetFullPath("Shared.akbura");
        var span = new TextSpan(1, "UnknownControl".Length);
        var owned = new AkburaDiagnosticRecord
        {
            Id = "AKBURA_TEST_OWNER",
            Severity = AkburaDiagnosticSeverity.Error,
            Message = "Unknown component",
            FilePath = filePath,
            Span = span,
            LineSpan = text.Lines.GetLinePositionSpan(span),
            Kind = AkburaDiagnosticKind.Semantic,
        };
        var embedded = owned with
        {
            Properties = ImmutableDictionary<string, string?>.Empty.Add(moduleProperty, "ReferencedModule"),
        };
        var canonical = AkburaDiagnosticCollection.Deduplicate([owned, embedded]);

        var actual = Assert.Single(AkburaDiagnosticService.SelectDiagnostics(
            canonical,
            filePath,
            text.Length,
            new TextSpan(0, text.Length),
            CancellationToken.None));

        Assert.Equal(2, canonical.Length);
        Assert.True(AkburaDiagnosticCanonicalComparer.Instance.Equals(owned, actual.CanonicalDiagnostic!.Value));
        Assert.Equal("ReferencedModule", embedded.Properties[moduleProperty]);
    }

    [Fact]
    public void ProjectionDoesNotPublishAnImportedFilesDiagnosticAtTheCurrentFilesSpan()
    {
        var text = SourceText.From("// 😀\r\n<UnknownControl/>");
        var filePath = Path.GetFullPath("Current.akbura");
        var span = new TextSpan(8, "UnknownControl".Length);
        var owned = new AkburaDiagnosticRecord
        {
            Id = "AKBURA_TEST_OWNER",
            Severity = AkburaDiagnosticSeverity.Error,
            Message = "Unknown component",
            FilePath = filePath,
            Span = span,
            LineSpan = text.Lines.GetLinePositionSpan(span),
            Kind = AkburaDiagnosticKind.Semantic,
        };
        var imported = owned with
        {
            FilePath = Path.GetFullPath("Imported.akcss"),
            LineSpan = new LinePositionSpan(new LinePosition(12, 3), new LinePosition(12, 17)),
        };

        var actual = Assert.Single(AkburaDiagnosticService.SelectDiagnostics(
            [owned, imported],
            filePath,
            text.Length,
            new TextSpan(0, text.Length),
            CancellationToken.None));

        Assert.Equal(span, actual.Span);
        Assert.Equal(owned.LineSpan, actual.CanonicalDiagnostic!.Value.LineSpan);
        Assert.Equal(filePath, actual.CanonicalDiagnostic.Value.FilePath);
        Assert.True(AkburaDiagnosticCanonicalComparer.Instance.Equals(owned, actual.CanonicalDiagnostic.Value));
    }

    [Theory]
    [InlineData("// 😀\r\n<UnknownControl Missing=", "Canonical.akbura")]
    [InlineData("// 😀\r\n@utilities { UnknownType.example { Width: ; }", "Canonical.akcss")]
    public void WorkspaceDiagnosticsMatchCanonicalEngine(string source, string fileName)
    {
        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath(fileName)), text);
        var expected = AkburaDiagnosticEngine.Collect(
            context.Document.SyntaxTree,
            context.Project.Compilation.GetSemanticModel(context.Document.SyntaxTree));

        var actual = workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, text.Length));

        Assert.NotEmpty(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            var diagnostic = actual[index];
            Assert.NotNull(diagnostic.CanonicalDiagnostic);
            var canonical = diagnostic.CanonicalDiagnostic.Value;
            Assert.True(AkburaDiagnosticCanonicalComparer.Instance.Equals(expected[index], canonical));
            Assert.Equal(expected[index].Span, diagnostic.Span);
            Assert.Equal(expected[index].Id, diagnostic.Code);
            Assert.Equal(expected[index].Message, diagnostic.Message);
            Assert.Equal(expected[index].Severity, diagnostic.Severity);
            Assert.Equal(text.Lines.GetLinePositionSpan(diagnostic.Span), canonical.LineSpan);
            Assert.True(canonical.Provenance.HasFlag(DiagnosticProvenance.Workspaces));
        }
    }

    [Fact]
    public void RequestedSpanFiltersCanonicalDiagnosticsWithoutChangingTheirLocations()
    {
        using var workspace = new AkburaWorkspace();
        var text = SourceText.From("// 😀\r\n<UnknownControl Missing=");
        var document = AkburaSyntacticDocument.Parse(text, Path.GetFullPath("Range.akbura"));
        var all = workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(document, new TextSpan(0, text.Length));
        Assert.NotEmpty(all);
        var requested = all[0].Span;

        var actual = workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(document, requested);
        var expected = all.Where(diagnostic => diagnostic.Span.Length == 0
            ? diagnostic.Span.Start >= requested.Start && diagnostic.Span.Start <= requested.End
            : diagnostic.Span.OverlapsWith(requested)).ToArray();

        Assert.NotEmpty(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.True(AkburaDiagnosticCanonicalComparer.Instance.Equals(
                expected[index].CanonicalDiagnostic!.Value,
                actual[index].CanonicalDiagnostic!.Value));
        }
        Assert.All(actual, diagnostic => Assert.Equal(
            text.Lines.GetLinePositionSpan(diagnostic.Span),
            diagnostic.CanonicalDiagnostic!.Value.LineSpan));
    }

    [Fact]
    public void CancelledCollectionDoesNotPublishPartialDiagnostics()
    {
        using var workspace = new AkburaWorkspace();
        var text = SourceText.From("<UnknownControl Missing=");
        var document = AkburaSyntacticDocument.Parse(text, Path.GetFullPath("Cancelled.akbura"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(
            document,
            new TextSpan(0, text.Length),
            cancellation.Token));
    }
}
