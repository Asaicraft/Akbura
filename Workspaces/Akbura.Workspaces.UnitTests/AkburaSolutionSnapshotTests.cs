using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class AkburaSolutionSnapshotTests
{
    [Fact]
    public void TryGetDocumentContext_ReturnsContextFromCapturedSnapshot()
    {
        using var workspace =
            new AkburaWorkspace();
        var uri =
            new Uri(
                Path.GetFullPath(
                    "Counter.akbura"));
        var originalContext =
            workspace.OpenOrChangeDocumentContext(
                uri,
                SourceText.From(
                    "<Original/>"));
        var capturedSolution =
            workspace.CurrentSolution;

        var updatedContext =
            workspace.OpenOrChangeDocumentContext(
                uri,
                SourceText.From(
                    "<Updated/>"));

        Assert.True(
            capturedSolution.TryGetDocumentContext(
                originalContext.Document.Id,
                out var contextById));
        Assert.True(
            capturedSolution.TryGetDocumentContext(
                uri,
                out var contextByUri));

        Assert.Same(
            originalContext.Project,
            contextById.Project);
        Assert.Same(
            originalContext.Document,
            contextById.Document);
        Assert.Same(
            originalContext.Project,
            contextByUri.Project);
        Assert.Same(
            originalContext.Document,
            contextByUri.Document);
        Assert.NotSame(
            updatedContext.Project,
            contextById.Project);
        Assert.NotSame(
            updatedContext.Document,
            contextById.Document);
    }

    [Fact]
    public void TryGetDocumentContext_ReturnsFalseForUnknownDocument()
    {
        var solution =
            AkburaSolutionSnapshot.Empty;

        Assert.False(
            solution.TryGetDocumentContext(
                AkburaDocumentId.CreateNew(),
                out var contextById));
        Assert.Null(
            contextById);

        Assert.False(
            solution.TryGetDocumentContext(
                new Uri(
                    Path.GetFullPath(
                        "Missing.akbura")),
                out var contextByUri));
        Assert.Null(
            contextByUri);
    }

    [Fact]
    public void TryGetDocumentContext_RejectsNullUri()
    {
        Assert.Throws<ArgumentNullException>(
            static () =>
                AkburaSolutionSnapshot.Empty
                    .TryGetDocumentContext(
                        uri: null!,
                        out _));
    }
}