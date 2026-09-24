using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceCollectionParameterTests
{
    [Theory]
    [InlineData("System.Collections.IList")]
    [InlineData("System.Collections.Generic.IList<int>")]
    [InlineData("System.Collections.ObjectModel.ObservableCollection<int>")]
    public void CollectionDefaults_AreOptionalInPublishedWorkspaceDiagnostics(string type)
    {
        var directory = Directory.CreateTempSubdirectory("akbura-list-parameter-");
        try
        {
            const string declarations = """
                namespace Avalonia.Controls
                {
                    public class Control { }
                    public class Border : Control { }
                }
                namespace Akbura
                {
                    public class AkburaControl : Avalonia.Controls.Control { }
                }
                """;
            var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
            var compilation = CSharpCompilation.Create("WorkspaceListParameters",
                [CSharpSyntaxTree.ParseText(declarations)],
                paths.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var project = new ProjectContext(ProjectId.CreateNewId(), string.Empty,
                directory.FullName, "Gallery", compilation, ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(project);
            workspace.OpenOrChangeDocumentContext(new Uri(Path.Combine(directory.FullName, "ItemsPanel.akbura")),
                SourceText.From($"using Avalonia.Controls; param {type} Data; param string Required; <Border/>"));
            var context = workspace.OpenOrChangeDocumentContext(new Uri(Path.Combine(directory.FullName, "View.akbura")),
                SourceText.From("<ItemsPanel/>"));
            var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(
                context, new TextSpan(0, context.Document.Text.Length));
            var required = Assert.Single(diagnostics.Where(d =>
                d.Code == "AKBURA_SEMANTIC_MarkupRequiredParameterNotSet"));
            Assert.Contains("Required", required.Message);
            Assert.DoesNotContain("'Data'", required.Message);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void ArrayListExpression_DoesNotPublishFalseCollectionConversionDiagnostic()
    {
        var directory = Directory.CreateTempSubdirectory("akbura-list-source-");
        try
        {
            const string declarations = """
                namespace Avalonia.Controls
                {
                    public class Control { }
                    public class Border : Control { }
                }
                namespace Akbura
                {
                    public class AkburaControl : Avalonia.Controls.Control { }
                }
                namespace Gallery
                {
                    public partial class View : Akbura.AkburaControl
                    {
                        public System.Collections.ArrayList LegacyNumbers { get; } = new();
                    }

                    public partial class ItemsPanel : Akbura.AkburaControl { }
                }
                """;
            var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
            var compilation = CSharpCompilation.Create(
                "WorkspaceListSources",
                [CSharpSyntaxTree.ParseText(declarations)],
                paths.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var project = new ProjectContext(
                ProjectId.CreateNewId(),
                string.Empty,
                directory.FullName,
                "Gallery",
                compilation,
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(project);
            workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory.FullName, "ItemsPanel.akbura")),
                SourceText.From(
                    "using Avalonia.Controls; using System.Collections.Generic; " +
                    "param IList<int> Data; <Border/>"));
            var context = workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory.FullName, "View.akbura")),
                SourceText.From("<ItemsPanel Data={LegacyNumbers} />"));

            var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(
                context,
                new TextSpan(0, context.Document.Text.Length));

            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == "AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert");
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Severity == AkburaDiagnosticSeverity.Error);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
