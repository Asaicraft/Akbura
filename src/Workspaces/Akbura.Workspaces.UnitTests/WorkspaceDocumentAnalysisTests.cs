using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

/// <summary>
/// Covers diagnostic analysis stages, source coordinates, and snapshot updates.
/// </summary>
public sealed class WorkspaceDocumentAnalysisTests
{
    [Fact]
    public void SyntaxAndSemanticStagesUseCanonicalDiagnosticsAndUtf16Offsets()
    {
        using var workspace = WorkspaceCompletionTests.CreateRuntimeUtilityWorkspace(
            WorkspaceCompletionTests.CreateRuntimeUtilityReferences());
        const string source = "using Avalonia.Controls;\r\n<StackPanel><TextBlock Text=\"🚀\" /><UnknownWidget /></StackPanel>";
        var path = new Uri(Path.GetFullPath("DiagnosticView.akbura"));
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(path, text);
        var diagnostics = workspace.LanguageServices.DocumentAnalysis;

        var syntax = diagnostics.AnalyzeDocument(context, AkburaDiagnosticAnalysisLevel.Syntax);
        Assert.True(syntax.IsComplete);
        Assert.Equal(AkburaDiagnosticAnalysisLevel.Syntax, syntax.CompletedLevel);
        Assert.Empty(syntax.Diagnostics);

        var semantic = diagnostics.AnalyzeDocument(context);
        var expected = workspace.LanguageServices.Diagnostics.GetDiagnosticReport(context, new TextSpan(0, text.Length));
        Assert.True(semantic.IsComplete);
        Assert.Equal(AkburaDiagnosticAnalysisLevel.Semantic, semantic.CompletedLevel);
        Assert.Equal(expected.Diagnostics.Select(item => item.LogicalId), semantic.Diagnostics.Select(item => item.LogicalId));

        var offset = source.IndexOf("UnknownWidget", StringComparison.Ordinal);
        var entry = Assert.Single(semantic.Diagnostics, item => item.Span.Start == offset);
        Assert.Equal("UnknownWidget", text.ToString(entry.Span));
        Assert.Equal(context.Document.FilePath, entry.FilePath);
        Assert.Equal(text.Lines.GetLinePositionSpan(entry.Span), entry.LineSpan);
        Assert.Equal(source.Substring(source.IndexOf("<StackPanel", StringComparison.Ordinal), offset - source.IndexOf("<StackPanel", StringComparison.Ordinal)).Length,
            entry.LineSpan.Start.Character);
        Assert.Equal(AkburaDiagnosticSeverity.Error, entry.Severity);
        Assert.True(entry.Provenance.HasFlag(DiagnosticProvenance.Workspaces));

        var corrected = workspace.OpenOrChangeDocumentContext(path, SourceText.From(source.Replace("<UnknownWidget />", "<Border />")));
        var after = diagnostics.AnalyzeDocument(corrected);
        Assert.NotEqual(semantic.Snapshot.Document, after.Snapshot.Document);
        Assert.DoesNotContain(after.Diagnostics, item => item.Severity == AkburaDiagnosticSeverity.Error);
    }

    [Fact]
    public void MissingReferencesRetainSyntaxAndMarkSemanticAnalysisIncomplete()
    {
        using var workspace = new AkburaWorkspace();
        var document = workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("Incomplete.akbura")), SourceText.From("<Border Text="));
        var service = workspace.LanguageServices.DocumentAnalysis;
        var result = service.AnalyzeDocument(document);

        Assert.Equal(AkburaDiagnosticAnalysisLevel.Semantic, result.RequestedLevel);
        Assert.Equal(AkburaDiagnosticAnalysisLevel.Syntax, result.CompletedLevel);
        Assert.False(result.IsComplete);
        Assert.NotEmpty(result.IncompleteReasons);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, item =>
        {
            Assert.Equal(AkburaDiagnosticKind.Syntax, item.Kind);
            Assert.Equal(document.Document.FilePath, item.FilePath);
        });

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.AnalyzeDocument(document, cancellationToken: cancellation.Token));
    }
}
