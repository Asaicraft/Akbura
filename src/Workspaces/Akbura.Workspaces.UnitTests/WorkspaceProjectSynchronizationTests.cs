using Akbura.Language;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Akbura.Workspaces.Documents;

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

    [Fact]
    public void SynchronizeProjectDocuments_RemovesDocumentsMissingFromProjectSnapshot()
    {
        using var workspace = new AkburaWorkspace();
        var directory = Path.Combine(Path.GetTempPath(), nameof(WorkspaceProjectSynchronizationTests), Guid.NewGuid().ToString("N"));
        var glyphUri = new Uri(Path.Combine(directory, "Glyph.akbura"));
        var viewUri = new Uri(Path.Combine(directory, "View.akbura"));
        var glyph = new AkburaDocumentInput(glyphUri, SourceText.From("param string Text; <Border/>"));
        var view = new AkburaDocumentInput(viewUri, SourceText.From("<Glyph Text=\"Test\"/>"));

        var project = workspace.SynchronizeProjectDocuments(workspace.DefaultProjectId, [glyph, view]);
        Assert.Equal(2, project.Documents.Count);
        Assert.True(project.TryGetDocument(glyphUri, out var glyphDocument));

        project = workspace.SynchronizeProjectDocuments(workspace.DefaultProjectId, [view]);

        Assert.Single(project.Documents);
        Assert.False(project.TryGetDocument(glyphUri, out _));
        Assert.True(project.TryGetDocument(viewUri, out _));
        Assert.DoesNotContain(glyphDocument.SyntaxTree, project.Compilation.SyntaxTrees);
    }

    [Fact]
    public async Task ProjectSynchronizer_RemovesDeletedDocumentsAndPreservesCurrentDocument()
    {
        using var roslynWorkspace = new AdhocWorkspace();
        var directory = Path.Combine(Path.GetTempPath(), nameof(WorkspaceProjectSynchronizationTests), Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(directory, "App.csproj");
        var project = roslynWorkspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "App", "App", LanguageNames.CSharp, filePath: projectPath));
        var glyphPath = Path.Combine(directory, "Glyph.akbura");
        var viewPath = Path.Combine(directory, "View.akbura");
        var glyph = project.AddDocument("Glyph.akbura", SourceText.From("param string Text; <Border/>"), filePath: glyphPath);
        project = glyph.Project;
        var view = project.AddDocument("View.akbura", SourceText.From("<Glyph Text=\"Test\"/>"), filePath: viewPath);
        project = view.Project;
        var compilation = CSharpCompilation.Create("App");
        var context = new RoslynProjectContextFactory().Create(project, compilation);
        using var workspace = new AkburaWorkspace(context);
        var synchronizer = new AkburaProjectSynchronizer(workspace);
        var glyphUri = new Uri(glyphPath);
        var viewUri = new Uri(viewPath);

        await synchronizer.SynchronizeProjectAsync(project, compilation, null, null, CancellationToken.None);
        Assert.Contains(GetGlyphCompletions(workspace, workspace.DefaultProjectId, viewUri).Items,
            item => item.Kind == AkburaCompletionKind.Component && item.DisplayText == "Glyph");
        workspace.OpenOrChangeDocumentContext(workspace.DefaultProjectId, viewUri,
            SourceText.From("<Glyph Text=\"Current editor buffer\"/>"));

        project = project.RemoveDocument(glyph.Id);
        await synchronizer.SynchronizeProjectAsync(project, compilation, null, viewUri, CancellationToken.None);

        Assert.False(workspace.TryGetDocument(glyphUri, out _));
        Assert.True(workspace.TryGetDocument(viewUri, out var activeDocument));
        Assert.Equal("<Glyph Text=\"Current editor buffer\"/>", activeDocument.Text.ToString());
        Assert.DoesNotContain(GetGlyphCompletions(workspace, workspace.DefaultProjectId, viewUri).Items,
            item => item.Kind == AkburaCompletionKind.Component && item.DisplayText == "Glyph");

        project = project.RemoveDocument(view.Id);
        await synchronizer.SynchronizeProjectAsync(project, compilation, null, viewUri, CancellationToken.None);

        Assert.False(workspace.TryGetDocument(glyphUri, out _));
        Assert.False(workspace.TryGetDocument(viewUri, out _));
    }

    private static AkburaCompletionResult GetGlyphCompletions(AkburaWorkspace workspace, AkburaProjectId projectId, Uri uri)
    {
        const string source = "using App;\r\n<G";
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(projectId, uri, text);
        var document = AkburaSyntacticDocument.Parse(text, uri.LocalPath);
        return workspace.LanguageServices.Completion.GetCompletions(document, context, source.Length);
    }
}
