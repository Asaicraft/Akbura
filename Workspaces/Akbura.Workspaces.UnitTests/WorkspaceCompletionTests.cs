using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceCompletionTests
{
    private const string CardSource = """
        namespace Gallery;

        using Avalonia.Controls;

        param string Title;
        param bool Compact = false;

        <StackPanel/>
        """;

    [Fact]
    public void Completion_ComponentNameUsesVisibleTypesAndComponents()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            <Sta
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "StackPanel" &&
                        item.Kind == AkburaCompletionKind.Component);
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Card");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "AbstractView");
                Assert.Equal(
                    "Sta",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_AttributeUsesAkburaParametersAndClrMembers()
    {
        const string source = """
            namespace Gallery;

            <Card 
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Title" &&
                        item.Kind == AkburaCompletionKind.Parameter &&
                        item.InsertText == "Title=\"\"" &&
                        item.CaretOffsetFromEnd == 1);
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Compact");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Width");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Loaded");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "x.Name");
            });
    }

    [Fact]
    public void Completion_AttributeExcludesExistingMember()
    {
        const string source = """
            namespace Gallery;

            <Card Title="Hello" Com
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Title");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Compact");
                Assert.Equal(
                    "Com",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_PropertyElementUsesParentComponent()
    {
        const string source = """
            namespace Gallery;

            <Card>
                <Card.Ti
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Card.Title" &&
                        item.Kind ==
                            AkburaCompletionKind.PropertyElement);
            });
    }

    [Fact]
    public void Completion_ClosingTagDoesNotRequireSemantics()
    {
        const string source = "<Card></";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "App.akbura");

        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        var item = Assert.Single(result.Items);
        Assert.Equal("Card", item.InsertText);
        Assert.Equal(AkburaCompletionKind.ClosingTag, item.Kind);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public void Completion_SemanticContextCanArriveAfterPopup()
    {
        const string source = "<Sta";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "App.akbura");

        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        Assert.Empty(result.Items);
        Assert.True(result.IsIncomplete);
    }

    [Fact]
    public void Completion_ComponentPrefixIsStrictAndSystemTypesAreHidden()
    {
        const string source = """
            namespace Gallery;

            using System;
            using Avalonia.Controls;

            <
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "StackPanel");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Card");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Boolean");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Exception");
                var card = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "Card");
                var stackPanel = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "StackPanel");
                Assert.Equal(0, card.Priority);
                Assert.Equal("Akbura component", card.Suffix);
                Assert.Equal(10, stackPanel.Priority);
                Assert.Equal("Avalonia.Controls", stackPanel.Suffix);
                Assert.True(
                    result.Items.IndexOf(card) <
                    result.Items.IndexOf(stackPanel));
                Assert.True(result.IsIncomplete);
            });
    }

    [Fact]
    public void CompletionItem_ComputesDetailedDescriptionOnDemand()
    {
        var calls = 0;
        var item = new AkburaCompletionItem(
            "Card",
            "Card",
            AkburaCompletionKind.Component,
            description: string.Empty,
            descriptionFactory: () =>
            {
                calls++;
                return "global::Gallery.Card";
            });

        Assert.Equal(0, calls);
        Assert.Equal("global::Gallery.Card", item.Description);
        Assert.Equal("global::Gallery.Card", item.Description);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Completion_UsesAkburaComponentFromProjectReference()
    {
        const string libraryCSharpSource = """
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
        const string libraryComponentSource = """
            namespace Library;

            using Avalonia.Controls;

            param string Title;

            <StackPanel/>
            """;
        const string applicationCSharpSource = """
            namespace Application
            {
                public partial class App : Akbura.AkburaControl
                {
                }
            }
            """;
        const string componentCompletionSource = """
            namespace Application;

            using Library;

            <Car
            """;
        const string parameterCompletionSource = """
            namespace Application;

            using Library;

            <Card 
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        var libraryDirectory = Path.Combine(directory, "Library");
        var applicationDirectory = Path.Combine(directory, "Application");
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(applicationDirectory);

        try
        {
            var platformReferences = CreatePlatformReferences();
            var libraryProjectId = ProjectId.CreateNewId("Library");
            var applicationProjectId = ProjectId.CreateNewId("Application");
            var libraryCompilation = CSharpCompilation.Create(
                "Library",
                [CSharpSyntaxTree.ParseText(libraryCSharpSource)],
                platformReferences,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var applicationCompilation = CSharpCompilation.Create(
                "Application",
                [CSharpSyntaxTree.ParseText(applicationCSharpSource)],
                platformReferences.Append(
                    libraryCompilation.ToMetadataReference()),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var libraryContext = new ProjectContext(
                libraryProjectId,
                Path.Combine(libraryDirectory, "Library.csproj"),
                libraryDirectory,
                "Library",
                libraryCompilation,
                ImmutableArray<ProjectReference>.Empty);
            var applicationContext = new ProjectContext(
                applicationProjectId,
                Path.Combine(applicationDirectory, "Application.csproj"),
                applicationDirectory,
                "Application",
                applicationCompilation,
                [new ProjectReference(libraryProjectId)]);

            using var workspace = new AkburaWorkspace();
            var libraryProject = workspace.AddOrUpdateProject(libraryContext);
            workspace.OpenOrChangeDocumentContext(
                libraryProject.Id,
                new Uri(Path.Combine(libraryDirectory, "Card.akbura")),
                SourceText.From(libraryComponentSource));
            var applicationProject = workspace.AddOrUpdateProject(
                applicationContext);

            var componentResult = GetCompletionResult(
                workspace,
                applicationProject.Id,
                Path.Combine(applicationDirectory, "App.akbura"),
                componentCompletionSource);
            Assert.Contains(
                componentResult.Items,
                static item => item.DisplayText == "Card");
            var applicationReference = Assert.Single(
                workspace.CurrentSolution
                    .GetRequiredProject(applicationProject.Id)
                    .Compilation
                    .CompilationReferences);
            Assert.Equal(
                0,
                applicationReference.CachedComponentSymbolCount);

            var parameterResult = GetCompletionResult(
                workspace,
                applicationProject.Id,
                Path.Combine(applicationDirectory, "App.akbura"),
                parameterCompletionSource);
            Assert.Contains(
                parameterResult.Items,
                static item =>
                    item.DisplayText == "Title" &&
                    item.Kind == AkburaCompletionKind.Parameter);
            applicationReference = Assert.Single(
                workspace.CurrentSolution
                    .GetRequiredProject(applicationProject.Id)
                    .Compilation
                    .CompilationReferences);
            Assert.Equal(
                1,
                applicationReference.CachedComponentSymbolCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Completion_UsesAkburaParametersFromMetadataReference()
    {
        const string source = """
            namespace Application;

            using Library;

            <Card 
            """;
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var libraryReference =
                CreateEmbeddedComponentReference(directory);
            var applicationCompilation = CSharpCompilation.Create(
                "Application",
                [CSharpSyntaxTree.ParseText(
                    "namespace Application { " +
                    "public partial class App : " +
                    "Akbura.AkburaControl { } }")],
                CreatePlatformReferences().Append(libraryReference),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                Path.Combine(directory, "Application.csproj"),
                directory,
                "Application",
                applicationCompilation,
                ImmutableArray<ProjectReference>.Empty);

            using var workspace = new AkburaWorkspace(projectContext);
            var result = GetCompletionResult(
                workspace,
                workspace.CurrentSolution.Projects.Keys.Single(),
                Path.Combine(directory, "App.akbura"),
                source);

            Assert.Contains(
                result.Items,
                static item =>
                    item.DisplayText == "Title" &&
                    item.Kind == AkburaCompletionKind.Parameter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WithWorkspace(
        string source,
        Action<
            AkburaWorkspace,
            AkburaDocumentContext,
            AkburaSyntacticDocument> assertion)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: directory,
                rootNamespace: string.Empty,
                CreateCompilation(),
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(projectContext);
            workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory, "Card.akbura")),
                SourceText.From(CardSource));
            var appPath = Path.Combine(directory, "App.akbura");
            var text = SourceText.From(source);
            var semanticContext =
                workspace.OpenOrChangeDocumentContext(
                    new Uri(appPath),
                    text);
            var syntacticDocument =
                AkburaSyntacticDocument.Parse(text, appPath);

            assertion(
                workspace,
                semanticContext,
                syntacticDocument);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AkburaCompletionResult GetCompletionResult(
        AkburaWorkspace workspace,
        AkburaProjectId projectId,
        string path,
        string source)
    {
        var text = SourceText.From(source);
        var semanticContext = workspace.OpenOrChangeDocumentContext(
            projectId,
            new Uri(path),
            text);
        var syntacticDocument = AkburaSyntacticDocument.Parse(text, path);
        return workspace.LanguageServices.Completion.GetCompletions(
            syntacticDocument,
            semanticContext,
            source.Length);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public double Width { get; set; }

                    public event System.EventHandler? Loaded;
                }

                public sealed class StackPanel : Control
                {
                    public double Spacing { get; set; }
                }

                public abstract class AbstractView : Control
                {
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }

            namespace Gallery
            {
                public partial class Card : Akbura.AkburaControl
                {
                }

                public partial class App : Akbura.AkburaControl
                {
                }
            }
            """;

        return CSharpCompilation.Create(
            "WorkspaceCompletionTests",
            [CSharpSyntaxTree.ParseText(source)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static PortableExecutableReference
        CreateEmbeddedComponentReference(string directory)
    {
        const string componentSource = """
            namespace Library;

            using Avalonia.Controls;

            param string Title;

            <StackPanel/>
            """;
        const string csharpSource = """
            namespace Avalonia.Controls
            {
                public class Control
                {
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

            namespace Library
            {
                public partial class Card : Akbura.AkburaControl
                {
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(csharpSource)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var manifest = AkburaModuleManifestBuilder.Build(
            "Library",
            "Library",
            [new AkburaModuleSourceText(
                "Card.akbura",
                componentSource)],
            compilation);
        using var manifestStream = new MemoryStream();
        AkburaModuleManifestSerializer.Write(
            manifestStream,
            manifest);

        var assemblyPath = Path.Combine(directory, "Library.dll");
        var emitResult = compilation.Emit(
            assemblyPath,
            manifestResources:
            [
                CreateResource(
                    AkburaModuleManifest.ResourceName,
                    manifestStream.ToArray()),
                CreateEmbeddedSourceResource(
                    "Card.akbura",
                    componentSource),
            ]);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromFile(assemblyPath);
    }

    private static ResourceDescription CreateEmbeddedSourceResource(
        string name,
        string content)
    {
        var preamble = System.Text.Encoding.Unicode.GetPreamble();
        var text = System.Text.Encoding.Unicode.GetBytes(content);
        var bytes = new byte[preamble.Length + text.Length];
        preamble.CopyTo(bytes, 0);
        text.CopyTo(bytes, preamble.Length);
        return CreateResource(name, bytes);
    }

    private static ResourceDescription CreateResource(
        string name,
        byte[] content)
    {
        return new ResourceDescription(
            name,
            () => new MemoryStream(content, writable: false),
            isPublic: true);
    }

    private static MetadataReference[] CreatePlatformReferences()
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
        return trustedPlatformAssemblies
            .Select(static path =>
                MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
