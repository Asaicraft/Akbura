using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceCompletionSemanticRebaseTests
{
    [Fact]
    public void Completion_RebasesStaleSemanticContextToCurrentBindingText()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding I} x.DataType=\"MainViewModel\"/>");
        var (document, position) = fixture.ParseCurrent("<Border Tag=${Binding In|} x.DataType=\"MainViewModel\"/>");

        var result = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);
        var item = Assert.Single(result.Items, static item => item.DisplayText == "IncrementCommand");
        var change = fixture.Workspace.LanguageServices.Completion.GetCompletionChange(document, fixture.SemanticContext, position, item);
        var changed = document.Text.WithChanges(change.Changes);

        Assert.Contains("${Binding IncrementCommand}", changed.ToString(), StringComparison.Ordinal);
        Assert.Equal(fixture.SemanticSource, fixture.SemanticContext.Document.Text.ToString());
        Assert.Same(fixture.SemanticContext.Project, fixture.SemanticContext.Solution.GetRequiredProject(fixture.SemanticContext.Project.Id));
    }

    [Fact]
    public void Completion_RebasesWhenTextInsertedBeforeBinding()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding Inc} x.DataType=\"MainViewModel\"/>");
        var (document, position) = fixture.ParseCurrent("\r\n<Border Tag=${Binding Inc|} x.DataType=\"MainViewModel\"/>");

        var result = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);

        Assert.Contains(result.Items, static item => item.DisplayText == "IncrementCommand");
    }

    [Fact]
    public void Completion_RebasesWhenCharacterDeletedFromBinding()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding Incrx} x.DataType=\"MainViewModel\"/>");
        var (document, position) = fixture.ParseCurrent("<Border Tag=${Binding Incr|} x.DataType=\"MainViewModel\"/>");

        var result = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);

        Assert.Contains(result.Items, static item => item.DisplayText == "IncrementCommand");
    }

    [Fact]
    public void Completion_RebasesCorrectBindingWhenEarlierBindingChanges()
    {
        const string staleMarkup = "<StackPanel><Border Tag=${Binding I} x.DataType=\"AlphaViewModel\"/><Border Tag=${Binding Inv} x.DataType=\"BravoViewModel\"/></StackPanel>";
        const string currentMarkup = "<StackPanel><Border Tag=${Binding IncrementCommand} x.DataType=\"AlphaViewModel\"/><Border Tag=${Binding Inv|} x.DataType=\"BravoViewModel\"/></StackPanel>";
        using var fixture = Fixture.Create(staleMarkup);
        var (document, position) = fixture.ParseCurrent(currentMarkup);

        var result = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);

        Assert.Contains(result.Items, static item => item.DisplayText == "InvoiceCommand");
        Assert.DoesNotContain(result.Items, static item => item.DisplayText == "IncrementCommand");
    }

    [Fact]
    public void Completion_RebasesEqualLengthTextWhenDataTypeChanges()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding In} x.DataType=\"AlphaViewModel\"/>");
        var (document, position) = fixture.ParseCurrent("<Border Tag=${Binding In|} x.DataType=\"BravoViewModel\"/>");
        Assert.Equal(fixture.SemanticContext.Document.Text.Length, document.Text.Length);

        var result = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);

        Assert.Contains(result.Items, static item => item.DisplayText == "InvoiceCommand");
        Assert.DoesNotContain(result.Items, static item => item.DisplayText == "IncrementCommand");
    }

    [Fact]
    public void Completion_RebasesEachEditWithoutWaitingForPublishedSemantics()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding I} x.DataType=\"MainViewModel\"/>");

        foreach (var prefix in new[] { "I", "In", "Inc", "Incr" })
        {
            var (document, position) = fixture.ParseCurrent($"<Border Tag=${{Binding {prefix}|}} x.DataType=\"MainViewModel\"/>");
            var result = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);
            Assert.Contains(result.Items, static item => item.DisplayText == "IncrementCommand");
        }
    }

    [Fact]
    public void Completion_InvalidatesRebasedContextWhenProjectContextChanges()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding I} x.DataType=\"MainViewModel\"/>");
        var (document, position) = fixture.ParseCurrent("<Border Tag=${Binding In|} x.DataType=\"MainViewModel\"/>");
        var initial = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);
        Assert.Contains(initial.Items, static item => item.DisplayText == "IncrementCommand");

        var oldProjectContext = fixture.SemanticContext.Project.Context;
        fixture.Workspace.AddOrUpdateProject(new ProjectContext(oldProjectContext.RoslynProjectId, oldProjectContext.ProjectFilePath, oldProjectContext.ProjectDirectory, oldProjectContext.RootNamespace, Fixture.CreateCompilation("InsertedCommand"), oldProjectContext.ProjectReferences));
        Assert.True(fixture.Workspace.CurrentSolution.TryGetDocumentContext(fixture.SemanticContext.Document.Id, out var changedContext));

        var changed = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, changedContext, position);

        Assert.Contains(changed.Items, static item => item.DisplayText == "InsertedCommand");
        Assert.DoesNotContain(changed.Items, static item => item.DisplayText == "IncrementCommand");
    }

    [Fact]
    public void Normalizer_PreservesOtherProjectsInSolutionSnapshot()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding I} x.DataType=\"MainViewModel\"/>");
        var libraryProjectId = ProjectId.CreateNewId("Library");
        var libraryProject = fixture.Workspace.AddOrUpdateProject(new ProjectContext(libraryProjectId, Path.Combine(fixture.Directory, "Library.csproj"), fixture.Directory, "Library", Fixture.CreateCompilation().WithAssemblyName("Library"), ImmutableArray<ProjectReference>.Empty));
        Assert.True(fixture.Workspace.CurrentSolution.TryGetDocumentContext(fixture.SemanticContext.Document.Id, out var sourceContext));
        var (document, _) = fixture.ParseCurrent("<Border Tag=${Binding In|} x.DataType=\"MainViewModel\"/>");

        var normalization = AkburaSemanticContextNormalizer.Normalize(document, sourceContext);

        Assert.Equal(AkburaSemanticContextMode.Rebased, normalization.Mode);
        Assert.NotNull(normalization.Context);
        Assert.Equal(2, normalization.Context!.Solution.Projects.Count);
        Assert.Same(libraryProject, normalization.Context.Solution.GetRequiredProject(libraryProject.Id));
        Assert.Same(sourceContext.Document, sourceContext.Project.Documents[sourceContext.Document.Id]);
    }

    [Fact]
    public void Normalizer_RejectsDifferentDocumentWithSameBasename()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding I} x.DataType=\"MainViewModel\"/>");
        var otherDirectory = Path.Combine(fixture.Directory, "Other");
        System.IO.Directory.CreateDirectory(otherDirectory);
        var sourceWithCaret = "namespace Gallery;\r\n\r\nusing Avalonia.Controls;\r\nusing Avalonia.Data;\r\n\r\n<Border Tag=${Binding In|} x.DataType=\"MainViewModel\"/>";
        var position = sourceWithCaret.IndexOf('|');
        var document = AkburaSyntacticDocument.Parse(SourceText.From(sourceWithCaret.Remove(position, 1)), Path.Combine(otherDirectory, "View.akbura"));

        var normalization = AkburaSemanticContextNormalizer.Normalize(document, fixture.SemanticContext);
        var completion = fixture.Workspace.LanguageServices.Completion.GetCompletions(document, fixture.SemanticContext, position);

        Assert.Equal(AkburaSemanticContextMode.Unavailable, normalization.Mode);
        Assert.Equal(AkburaSemanticContextFailureReason.DocumentMismatch, normalization.FailureReason);
        Assert.Null(normalization.Context);
        Assert.Empty(completion.Items);
    }

    [Fact]
    public void Normalizer_UsesCaseSensitiveDocumentIdentityOnCaseSensitivePlatforms()
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            return;
        }

        using var fixture = Fixture.Create("<Border Tag=${Binding I} x.DataType=\"MainViewModel\"/>");
        var sourceWithCaret = "namespace Gallery;\r\n\r\nusing Avalonia.Controls;\r\nusing Avalonia.Data;\r\n\r\n<Border Tag=${Binding In|} x.DataType=\"MainViewModel\"/>";
        var position = sourceWithCaret.IndexOf('|');
        var differentCasePath = fixture.FilePath.ToUpperInvariant();
        Assert.NotEqual(fixture.FilePath, differentCasePath);
        var document = AkburaSyntacticDocument.Parse(SourceText.From(sourceWithCaret.Remove(position, 1)), differentCasePath);

        var normalization = AkburaSemanticContextNormalizer.Normalize(document, fixture.SemanticContext);

        Assert.Equal(AkburaSemanticContextMode.Unavailable, normalization.Mode);
        Assert.Equal(AkburaSemanticContextFailureReason.DocumentMismatch, normalization.FailureReason);
        Assert.Null(normalization.Context);
    }

    [Fact]
    public void Normalizer_ReusesRebasedContextForSameTextVersion()
    {
        using var fixture = Fixture.Create("<Border Tag=${Binding I} x.DataType=\"MainViewModel\"/>");
        var (document, _) = fixture.ParseCurrent("<Border Tag=${Binding In|} x.DataType=\"MainViewModel\"/>");

        var first = AkburaSemanticContextNormalizer.Normalize(document, fixture.SemanticContext);
        var second = AkburaSemanticContextNormalizer.Normalize(document, fixture.SemanticContext);

        Assert.Equal(AkburaSemanticContextMode.Rebased, first.Mode);
        Assert.Equal(AkburaSemanticContextMode.Rebased, second.Mode);
        Assert.NotNull(first.Context);
        Assert.Same(first.Context, second.Context);
    }

    private sealed class Fixture : IDisposable
    {
        private const string SourcePrefix = "namespace Gallery;\r\n\r\nusing Avalonia.Controls;\r\nusing Avalonia.Data;\r\n\r\n";

        private Fixture(string directory, string filePath, AkburaWorkspace workspace, AkburaDocumentContext semanticContext, string semanticSource)
        {
            Directory = directory;
            FilePath = filePath;
            Workspace = workspace;
            SemanticContext = semanticContext;
            SemanticSource = semanticSource;
        }

        public string Directory { get; }

        public string FilePath { get; }

        public AkburaWorkspace Workspace { get; }

        public AkburaDocumentContext SemanticContext { get; }

        public string SemanticSource { get; }

        public static Fixture Create(string staleMarkup)
        {
            var directory = Path.Combine(Path.GetTempPath(), nameof(WorkspaceCompletionSemanticRebaseTests), Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var projectId = ProjectId.CreateNewId("Application");
            var projectContext = new ProjectContext(projectId, Path.Combine(directory, "Application.csproj"), directory, "Gallery", CreateCompilation(), ImmutableArray<ProjectReference>.Empty);
            var workspace = new AkburaWorkspace(projectContext);
            var filePath = Path.Combine(directory, "View.akbura");
            var semanticSource = SourcePrefix + staleMarkup;
            var semanticContext = workspace.OpenOrChangeDocumentContext(new Uri(filePath), SourceText.From(semanticSource));
            return new Fixture(directory, filePath, workspace, semanticContext, semanticSource);
        }

        public (AkburaSyntacticDocument Document, int Position) ParseCurrent(string markupWithCaret)
        {
            var sourceWithCaret = SourcePrefix + markupWithCaret;
            var position = sourceWithCaret.IndexOf('|');
            Assert.True(position >= 0);
            var source = sourceWithCaret.Remove(position, 1);
            return (AkburaSyntacticDocument.Parse(SourceText.From(source), FilePath), position);
        }

        public void Dispose()
        {
            Workspace.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
        }

        internal static CSharpCompilation CreateCompilation(string mainViewModelProperty = "IncrementCommand")
        {
            const string source = """
                namespace Avalonia.Controls
                {
                    public class Control
                    {
                        public object? Tag { get; set; }
                    }

                    public sealed class Border : Control
                    {
                    }

                    public sealed class StackPanel : Control
                    {
                    }
                }

                namespace Avalonia.Data
                {
                    public class Binding
                    {
                        public string? Path { get; set; }
                    }

                    public sealed class ReflectionBinding : Binding
                    {
                    }

                    public sealed class CompiledBinding : Binding
                    {
                    }
                }

                namespace Gallery
                {
                    public sealed class MainViewModel
                    {
                        public string __MAIN_PROPERTY__ { get; } = "";
                    }

                    public sealed class AlphaViewModel
                    {
                        public string IncrementCommand { get; } = "";
                    }

                    public sealed class BravoViewModel
                    {
                        public string InvoiceCommand { get; } = "";
                    }
                }
                """;
            var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
            var resolvedSource = source.Replace("__MAIN_PROPERTY__", mainViewModelProperty, StringComparison.Ordinal);
            return CSharpCompilation.Create(nameof(WorkspaceCompletionSemanticRebaseTests), [CSharpSyntaxTree.ParseText(resolvedSource)], platform.Select(static path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
