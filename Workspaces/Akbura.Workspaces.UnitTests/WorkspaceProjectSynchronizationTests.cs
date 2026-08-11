using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceProjectSynchronizationTests
{
    [Fact]
    public void SynchronizeProjectDocuments_PublishesOneProjectChange()
    {
        using var workspace = new AkburaWorkspace();
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceProjectSynchronizationTests),
            Guid.NewGuid().ToString("N"));
        var firstUri = new Uri(Path.Combine(directory, "First.akbura"));
        var secondUri = new Uri(Path.Combine(directory, "Styles.akcss"));
        var changedCount = 0;
        workspace.Changed += (_, _) => changedCount++;

        var project = workspace.SynchronizeProjectDocuments(
            workspace.DefaultProjectId,
            [
                new AkburaDocumentInput(
                    firstUri,
                    SourceText.From("<First/>")),
                new AkburaDocumentInput(
                    secondUri,
                    SourceText.From("@utilities { }")),
            ]);

        Assert.Equal(1, changedCount);
        Assert.Equal(2, project.Documents.Count);
        Assert.True(project.TryGetDocument(firstUri, out _));
        Assert.True(project.TryGetDocument(secondUri, out _));

        var unchanged = workspace.SynchronizeProjectDocuments(
            workspace.DefaultProjectId,
            [
                new AkburaDocumentInput(
                    firstUri,
                    SourceText.From("<First/>")),
                new AkburaDocumentInput(
                    secondUri,
                    SourceText.From("@utilities { }")),
            ]);

        Assert.Equal(1, changedCount);
        Assert.Same(project, unchanged);
    }
}
