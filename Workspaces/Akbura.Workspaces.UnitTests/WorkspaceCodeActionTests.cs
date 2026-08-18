using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceCodeActionTests
{
    [Fact]
    public void CodeAction_UnknownComponentOffersNamespaceImport()
    {
        const string source =
            "using Avalonia.Controls;\n\n<TemplatedControl/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);

        var action = Assert.Single(GetActions(
            workspace,
            context,
            source,
            "TemplatedControl"));

        Assert.Equal(
            AkburaCodeActionKind.AddNamespaceImport,
            action.Kind);
        Assert.Equal(
            "Avalonia.Controls.Primitives",
            action.NamespaceName);
        Assert.Contains(
            "Avalonia.Controls.Primitives",
            action.Title,
            StringComparison.Ordinal);
        Assert.Equal(
            "TemplatedControl",
            action.SubjectText);

        var changed = context.Document.Text
            .WithChanges(action.Changes)
            .ToString();
        Assert.Equal(
            "using Avalonia.Controls;\n" +
            "using Avalonia.Controls.Primitives;\n\n" +
            "<TemplatedControl/>\n",
            changed);
    }

    [Fact]
    public void CodeAction_CaretInsideUnknownComponentOffersImport()
    {
        const string source = "<TemplatedControl/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);
        var position = source.IndexOf(
            "TemplatedControl",
            StringComparison.Ordinal) + 5;

        var action = Assert.Single(
            workspace.LanguageServices.CodeActions.GetCodeActions(
                context,
                new TextSpan(position, 0)));

        Assert.Equal(
            "Avalonia.Controls.Primitives",
            action.NamespaceName);
    }

    [Fact]
    public void CodeAction_AppliedImportResolvesComponent()
    {
        const string source =
            "using Avalonia.Controls;\n\n<TemplatedControl/>\n";
        using var workspace = CreateWorkspace();
        var path = Path.GetFullPath("View.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(path),
            SourceText.From(source));
        var action = Assert.Single(GetActions(
            workspace,
            context,
            source,
            "TemplatedControl"));
        var changedText = context.Document.Text.WithChanges(action.Changes);
        var changedContext = workspace.OpenOrChangeDocumentContext(
            new Uri(path),
            changedText);

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetDiagnostics(
                changedContext,
                new TextSpan(0, changedText.Length));

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code ==
                ErrorCodes.AKBURA_SEMANTIC_MarkupComponentNotFound);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void CodeAction_PreservesNewLine(string newLine)
    {
        var source = "using Avalonia.Controls;" + newLine + newLine +
            "<TemplatedControl/>" + newLine;
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);
        var action = Assert.Single(GetActions(
            workspace,
            context,
            source,
            "TemplatedControl"));

        var changed = context.Document.Text
            .WithChanges(action.Changes)
            .ToString();

        Assert.Contains(
            "using Avalonia.Controls;" + newLine +
            "using Avalonia.Controls.Primitives;" + newLine,
            changed,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            newLine == "\n" ? "\r\n" : "\n\n",
            newLine == "\n" ? changed : changed.Replace("\r\n", ""),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CodeAction_DoesNotOfferExistingUsing()
    {
        const string source =
            "using Avalonia.Controls.Primitives;\n\n" +
            "<TemplatedControl/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);

        Assert.Empty(GetActions(
            workspace,
            context,
            source,
            "TemplatedControl"));
    }

    [Fact]
    public void CodeAction_DoesNotDuplicateGlobalUsing()
    {
        const string source =
            "global using Avalonia.Controls.Primitives;\n\n" +
            "<TemplatedControl/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);

        Assert.Empty(GetActions(
            workspace,
            context,
            source,
            "TemplatedControl"));
    }

    [Fact]
    public void CodeAction_MultipleNamespacesCreatesMultipleActions()
    {
        const string source = "<SharedControl/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);

        var actions = GetActions(
            workspace,
            context,
            source,
            "SharedControl");

        Assert.Equal(2, actions.Length);
        Assert.Equal(
            ["LibraryA.Controls", "LibraryB.Controls"],
            actions.Select(static action => action.NamespaceName));
    }

    [Fact]
    public void CodeAction_AmbiguousSameNamespaceIsNotOffered()
    {
        const string source = "<DuplicateControl/>\n";
        var firstReference = CreateReference(
            "DuplicateLibraryA",
            """
            namespace Shared.Controls
            {
                public sealed class DuplicateControl
                {
                }
            }
            """);
        var secondReference = CreateReference(
            "DuplicateLibraryB",
            """
            namespace Shared.Controls
            {
                public sealed class DuplicateControl
                {
                }
            }
            """);
        using var workspace = CreateWorkspace(
            firstReference,
            secondReference);
        var context = Open(workspace, source);

        Assert.Empty(GetActions(
            workspace,
            context,
            source,
            "DuplicateControl"));
    }

    [Fact]
    public void CodeAction_DoesNotOfferInaccessibleMetadataType()
    {
        const string source = "<HiddenControl/>\n";
        var reference = CreateReference(
            "HiddenLibrary",
            """
            namespace Hidden.Controls
            {
                internal sealed class HiddenControl
                {
                }
            }
            """);
        using var workspace = CreateWorkspace(reference);
        var context = Open(workspace, source);

        Assert.Empty(GetActions(
            workspace,
            context,
            source,
            "HiddenControl"));
    }

    [Fact]
    public void CodeAction_MetadataReferenceComponentIsOffered()
    {
        const string source = "<ReferencedView/>\n";
        var reference = CreateReference(
            "ReferencedLibrary",
            """
            namespace Referenced.Controls
            {
                public sealed class ReferencedView
                {
                }
            }
            """);
        using var workspace = CreateWorkspace(reference);
        var context = Open(workspace, source);

        var action = Assert.Single(GetActions(
            workspace,
            context,
            source,
            "ReferencedView"));

        Assert.Equal("Referenced.Controls", action.NamespaceName);
    }

    [Fact]
    public void CodeAction_ProjectReferenceComponentIsOffered()
    {
        const string libraryComponentSource = """
            namespace Library.Controls;

            using Avalonia.Controls;

            <Control/>
            """;
        const string applicationSource = "<ReferencedCard/>\n";
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCodeActionTests),
            Guid.NewGuid().ToString("N"));
        var libraryDirectory = Path.Combine(directory, "Library");
        var applicationDirectory = Path.Combine(directory, "Application");
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(applicationDirectory);

        try
        {
            var libraryId = ProjectId.CreateNewId("Library");
            var applicationId = ProjectId.CreateNewId("Application");
            var libraryCompilation = CreateCompilation();
            var applicationCompilation = CSharpCompilation.Create(
                "Application",
                references: GetPlatformReferences().Append(
                    libraryCompilation.ToMetadataReference()),
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var workspace = new AkburaWorkspace();
            var library = workspace.AddOrUpdateProject(new ProjectContext(
                libraryId,
                Path.Combine(libraryDirectory, "Library.csproj"),
                libraryDirectory,
                "Library",
                libraryCompilation,
                ImmutableArray<ProjectReference>.Empty));
            workspace.OpenOrChangeDocumentContext(
                library.Id,
                new Uri(Path.Combine(
                    libraryDirectory,
                    "ReferencedCard.akbura")),
                SourceText.From(libraryComponentSource));
            var application = workspace.AddOrUpdateProject(
                new ProjectContext(
                    applicationId,
                    Path.Combine(
                        applicationDirectory,
                        "Application.csproj"),
                    applicationDirectory,
                    "Application",
                    applicationCompilation,
                    [new ProjectReference(libraryId)]));
            var context = workspace.OpenOrChangeDocumentContext(
                application.Id,
                new Uri(Path.Combine(
                    applicationDirectory,
                    "View.akbura")),
                SourceText.From(applicationSource));

            var action = Assert.Single(GetActions(
                workspace,
                context,
                applicationSource,
                "ReferencedCard"));

            Assert.Equal("Library.Controls", action.NamespaceName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("AbstractView")]
    [InlineData("GenericView")]
    public void CodeAction_DoesNotOfferInvalidComponent(string name)
    {
        var source = $"<{name}/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);

        Assert.Empty(GetActions(
            workspace,
            context,
            source,
            name));
    }

    [Fact]
    public void CodeAction_DoesNotOfferForUnrelatedDiagnostic()
    {
        const string source = "state int value = Missing;\n\n<Button/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);
        var start = source.IndexOf("Missing", StringComparison.Ordinal);

        var actions = workspace.LanguageServices.CodeActions.GetCodeActions(
            context,
            new TextSpan(start, "Missing".Length));

        Assert.Empty(actions);
    }

    [Fact]
    public void CodeAction_InsertsAfterNamespaceDeclaration()
    {
        const string source =
            "namespace Gallery;\n\n<TemplatedControl/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);
        var action = Assert.Single(GetActions(
            workspace,
            context,
            source,
            "TemplatedControl"));

        var changed = context.Document.Text
            .WithChanges(action.Changes)
            .ToString();

        Assert.Equal(
            "namespace Gallery;\n" +
            "using Avalonia.Controls.Primitives;\n\n" +
            "<TemplatedControl/>\n",
            changed);
    }

    [Fact]
    public void CodeAction_InsertsBeforeFirstMarkupMember()
    {
        const string source = "<TemplatedControl/>\n";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);
        var action = Assert.Single(GetActions(
            workspace,
            context,
            source,
            "TemplatedControl"));

        Assert.Equal(
            "using Avalonia.Controls.Primitives;\n" + source,
            context.Document.Text.WithChanges(action.Changes).ToString());
    }

    private static ImmutableArray<AkburaCodeAction> GetActions(
        AkburaWorkspace workspace,
        AkburaDocumentContext context,
        string source,
        string subject)
    {
        var start = source.IndexOf(subject, StringComparison.Ordinal);
        Assert.True(start >= 0);
        return workspace.LanguageServices.CodeActions.GetCodeActions(
            context,
            new TextSpan(start, subject.Length));
    }

    private static AkburaDocumentContext Open(
        AkburaWorkspace workspace,
        string source)
    {
        return workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("View.akbura")),
            SourceText.From(source));
    }

    private static AkburaWorkspace CreateWorkspace(
        params MetadataReference[] additionalReferences)
    {
        return new AkburaWorkspace(new ProjectContext(
            ProjectId.CreateNewId(),
            projectFilePath: string.Empty,
            projectDirectory: Environment.CurrentDirectory,
            rootNamespace: string.Empty,
            CreateCompilation(additionalReferences),
            ImmutableArray<ProjectReference>.Empty));
    }

    private static CSharpCompilation CreateCompilation(
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        const string source = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                }

                public sealed class Button : Control
                {
                }
            }

            namespace Avalonia.Controls.Primitives
            {
                public class TemplatedControl :
                    Avalonia.Controls.Control
                {
                }
            }

            namespace Akbura
            {
                public class AkburaControl :
                    Avalonia.Controls.Control
                {
                }
            }

            namespace LibraryA.Controls
            {
                public sealed class SharedControl
                {
                }
            }

            namespace LibraryB.Controls
            {
                public sealed class SharedControl
                {
                }
            }

            namespace Invalid.Controls
            {
                public abstract class AbstractView
                {
                }

                public sealed class GenericView<T>
                {
                }

                internal sealed class InternalView
                {
                }
            }
            """;

        var platformAssemblies =
            ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
        var compilation = CSharpCompilation.Create(
            "WorkspaceCodeActionTests",
            [CSharpSyntaxTree.ParseText(source)],
            platformAssemblies.Select(static path =>
                MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));

        return additionalReferences == null
            ? compilation
            : compilation.AddReferences(additionalReferences);
    }

    private static IEnumerable<MetadataReference> GetPlatformReferences()
    {
        return (((string?)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [])
            .Select(static path =>
                MetadataReference.CreateFromFile(path));
    }

    private static PortableExecutableReference CreateReference(
        string assemblyName,
        string source)
    {
        var platformAssemblies =
            ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            platformAssemblies.Select(static path =>
                MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
