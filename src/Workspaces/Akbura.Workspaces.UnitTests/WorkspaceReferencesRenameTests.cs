using Akbura.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceReferencesRenameTests
{
    [Fact]
    public void StateReferencesAndRenameUseSemanticOccurrences()
    {
        const string source = """
            using Avalonia.Controls;

            state int count = 0;
            var doubled = count * 2;

            <StackPanel Width={count}/>
            """;
        var directory = Directory.CreateTempSubdirectory(
            "akbura-reference-tests-");
        try
        {
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: directory.FullName,
                rootNamespace: "Gallery",
                CreateCompilation(),
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(projectContext);
            var uri = new Uri(Path.Combine(
                directory.FullName,
                "Counter.akbura"));
            var context = workspace.OpenOrChangeDocumentContext(
                uri,
                SourceText.From(source));
            var declarationPosition = source.IndexOf(
                "count",
                StringComparison.Ordinal) + 1;

            var references = workspace.LanguageServices.References
                .FindReferences(
                    context,
                    declarationPosition,
                    includeDeclaration: true);

            Assert.False(references.IsEmpty);
            Assert.Equal("count", references.Name);
            Assert.True(references.Locations.Length >= 3);
            Assert.Single(references.Locations.Where(
                static location => location.IsDeclaration));
            Assert.All(
                references.Locations,
                location => Assert.Equal(
                    "count",
                    source.Substring(
                        location.Span.Start,
                        location.Span.Length)));

            var info = workspace.LanguageServices.Rename.GetRenameInfo(
                context,
                declarationPosition);
            Assert.True(info.CanRename);
            Assert.Equal("count", info.Placeholder);

            var edit = workspace.LanguageServices.Rename.GetRenameChanges(
                context,
                declarationPosition,
                "total");
            var changes = Assert.Single(edit.Changes).Value;
            var changed = SourceText.From(source)
                .WithChanges(changes)
                .ToString();
            Assert.DoesNotContain("count", changed);
            Assert.Equal(3, CountOccurrences(changed, "total"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public double Width { get; set; }
                }

                public sealed class StackPanel : Control
                {
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }
            """;
        return CSharpCompilation.Create(
            "ReferenceTests",
            [CSharpSyntaxTree.ParseText(source)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference[] CreatePlatformReferences()
    {
        var paths = ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator) ?? [];
        return paths.Select(static path =>
                MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static int CountOccurrences(
        string value,
        string search)
    {
        var count = 0;
        var position = 0;
        while ((position = value.IndexOf(
                   search,
                   position,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += search.Length;
        }

        return count;
    }
}