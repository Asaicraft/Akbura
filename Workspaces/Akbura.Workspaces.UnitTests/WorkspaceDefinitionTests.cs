using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceDefinitionTests
{
    private const string ComponentSource = """
        using Avalonia.Controls;
        using Library.Styles.akcss;

        <StackPanel gap-4/>
        """;

    [Fact]
    public void Definition_MetadataMarkupTypeCreatesNavigableSource()
    {
        WithWorkspace(
            (workspace, context, text) =>
            {
                var position = ComponentSource.IndexOf(
                    "StackPanel",
                    StringComparison.Ordinal);

                var definition = workspace.LanguageServices.Definition
                    .GetDefinition(context, position);

                Assert.NotNull(definition);
                Assert.NotNull(definition!.TargetText);
                Assert.EndsWith(
                    ".metadata.cs",
                    definition.TargetFilePath,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal(
                    "StackPanel",
                    GetTargetText(definition));
                Assert.Contains(
                    "class StackPanel",
                    definition.TargetText!.ToString(),
                    StringComparison.Ordinal);
                Assert.Equal(
                    "StackPanel",
                    text.ToString(definition.SourceSpan));
            });
    }

    [Fact]
    public void Definition_AkburaComponentPrefersComponentSource()
    {
        const string routerSource = """
            using Avalonia.Controls;

            <StackPanel/>
            """;
        const string appSource = """
            <Router/>
            """;
        const string componentTypes = """
            public partial class Router : Akbura.AkburaControl
            {
            }

            public partial class App : Akbura.AkburaControl
            {
            }
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceDefinitionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var libraryReference = CreateLibraryReference(
                directory,
                "@utilities { }");
            var compilation = CreateApplicationCompilation(
                libraryReference,
                componentTypes);
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: directory,
                rootNamespace: string.Empty,
                compilation,
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(
                projectContext);
            var routerPath = Path.Combine(
                directory,
                "Router.akbura");
            var appPath = Path.Combine(
                directory,
                "App.akbura");
            File.WriteAllText(routerPath, routerSource);
            File.WriteAllText(appPath, appSource);

            workspace.OpenOrChangeDocumentContext(
                new Uri(routerPath),
                SourceText.From(routerSource));
            var appContext =
                workspace.OpenOrChangeDocumentContext(
                    new Uri(appPath),
                    SourceText.From(appSource));
            var definition = workspace.LanguageServices.Definition
                .GetDefinition(
                    appContext,
                    appSource.IndexOf(
                        "Router",
                        StringComparison.Ordinal));

            Assert.NotNull(definition);
            Assert.Null(definition!.TargetText);
            Assert.Equal(
                Path.GetFullPath(routerPath),
                definition.TargetFilePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Definition_AkburaComponentPropertyUsesParameterDeclaration()
    {
        const string componentSource = """
            using Avalonia.Controls;

            param string Title;

            <StackPanel/>
            """;
        const string attributeUsageSource = """
            <Card Title="Hello"/>
            """;
        const string propertyElementUsageSource = """
            <Card>
                <Card.Title>
                    Hello
                </Card.Title>
            </Card>
            """;
        const string componentTypes = """
            public partial class Card : Akbura.AkburaControl
            {
            }

            public partial class AttributeUsage : Akbura.AkburaControl
            {
            }

            public partial class PropertyElementUsage : Akbura.AkburaControl
            {
            }
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceDefinitionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var libraryReference = CreateLibraryReference(
                directory,
                "@utilities { }");
            var compilation = CreateApplicationCompilation(
                libraryReference,
                componentTypes);
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: directory,
                rootNamespace: string.Empty,
                compilation,
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(
                projectContext);
            var componentPath = Path.Combine(
                directory,
                "Card.akbura");
            var attributeUsagePath = Path.Combine(
                directory,
                "AttributeUsage.akbura");
            var propertyElementUsagePath = Path.Combine(
                directory,
                "PropertyElementUsage.akbura");

            File.WriteAllText(
                componentPath,
                componentSource);
            File.WriteAllText(
                attributeUsagePath,
                attributeUsageSource);
            File.WriteAllText(
                propertyElementUsagePath,
                propertyElementUsageSource);

            workspace.OpenOrChangeDocumentContext(
                new Uri(componentPath),
                SourceText.From(componentSource));
            var attributeUsageContext =
                workspace.OpenOrChangeDocumentContext(
                    new Uri(attributeUsagePath),
                    SourceText.From(attributeUsageSource));
            var propertyElementUsageContext =
                workspace.OpenOrChangeDocumentContext(
                    new Uri(propertyElementUsagePath),
                    SourceText.From(propertyElementUsageSource));

            var attributeDefinition =
                workspace.LanguageServices.Definition
                    .GetDefinition(
                        attributeUsageContext,
                        attributeUsageSource.IndexOf(
                            "Title",
                            StringComparison.Ordinal));
            var propertyElementDefinition =
                workspace.LanguageServices.Definition
                    .GetDefinition(
                        propertyElementUsageContext,
                        propertyElementUsageSource.IndexOf(
                            "Title",
                            StringComparison.Ordinal));

            Assert.NotNull(attributeDefinition);
            Assert.NotNull(propertyElementDefinition);
            Assert.Equal(
                Path.GetFullPath(componentPath),
                attributeDefinition!.TargetFilePath);
            Assert.Equal(
                Path.GetFullPath(componentPath),
                propertyElementDefinition!.TargetFilePath);
            Assert.Equal(
                "Title",
                GetTargetText(attributeDefinition));
            Assert.Equal(
                "Title",
                GetTargetText(propertyElementDefinition));
            Assert.Equal(
                "Title",
                attributeUsageSource.Substring(
                    attributeDefinition.SourceSpan.Start,
                    attributeDefinition.SourceSpan.Length));
            Assert.Equal(
                "Card.Title",
                propertyElementUsageSource.Substring(
                    propertyElementDefinition.SourceSpan.Start,
                    propertyElementDefinition.SourceSpan.Length));
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Fact]
    public void Definition_EmbeddedAkcssUtilityCreatesNavigableSource()
    {
        WithWorkspace(
            (workspace, context, text) =>
            {
                var syntaxTree =
                    ComponentSyntaxTree.ParseText(
                        text,
                        context.Document.FilePath);
                var compilation = new AkburaCompilation(
                    context.Project.CSharpCompilation,
                    [syntaxTree]);
                var attribute = Assert.Single(
                    syntaxTree
                        .GetRootSyntax()
                        .DescendantNodes()
                        .OfType<TailwindFullAttributeSyntax>());
                var operation = Assert.IsAssignableFrom<
                    ITailwindUtilityAttributeOperation>(
                    compilation
                        .GetSemanticModel(syntaxTree)
                        .GetOperation(attribute));
                Assert.IsType<MetadataTailwindUtilitySymbol>(
                    operation.Utility);

                var utilityStart = ComponentSource.IndexOf(
                    "gap-4",
                    StringComparison.Ordinal);

                for (var offset = 0;
                     offset < "gap-4".Length;
                     offset++)
                {
                    var definition = workspace.LanguageServices.Definition
                        .GetDefinition(
                            context,
                            utilityStart + offset);

                    Assert.NotNull(definition);
                    Assert.NotNull(definition!.TargetText);
                    Assert.EndsWith(
                        "Styles.akcss",
                        definition.TargetFilePath,
                        StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(
                        "Library",
                        definition.TargetAssemblyName);
                    Assert.Equal(
                        "Styles.akcss",
                        definition.TargetSourcePath);
                    Assert.Equal(
                        "gap",
                        GetTargetText(definition));
                    Assert.Contains(
                        ".gap-",
                        definition.TargetText!.ToString(),
                        StringComparison.Ordinal);
                    Assert.Equal(
                        "gap-4",
                        text.ToString(definition.SourceSpan));
                }
            });
    }

    [Fact]
    public void Definition_EmbeddedAkcssApplyUtilityCreatesNavigableSource()
    {
        const string akcssSource = """
            @using Library.Styles.akcss;
            @using Avalonia.Controls;

            @utilities {
                StackPanel.card {
                    @apply gap-4;
                }
            }
            """;

        WithWorkspace(
            (workspace, context, _) =>
            {
                var path = Path.Combine(
                    Path.GetDirectoryName(context.Document.FilePath)!,
                    "LocalStyles.akcss");
                var text = SourceText.From(akcssSource);
                var akcssContext =
                    workspace.OpenOrChangeDocumentContext(
                        new Uri(path),
                        text);
                var utilityStart = akcssSource.IndexOf(
                    "gap-4",
                    StringComparison.Ordinal);

                for (var offset = 0;
                     offset < "gap-4".Length;
                     offset++)
                {
                    var definition = workspace.LanguageServices.Definition
                        .GetDefinition(
                            akcssContext,
                            utilityStart + offset);

                    Assert.NotNull(definition);
                    Assert.Equal(
                        "gap-4",
                        text.ToString(definition!.SourceSpan));
                    Assert.Equal(
                        "gap",
                        GetTargetText(definition));
                }
            });
    }

    [Fact]
    public void Definition_ProjectReferencedAkcssUtilityUsesPhysicalSource()
    {
        const string libraryCSharpSource = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                }

                public sealed class StackPanel : Control
                {
                }

                public sealed class Border : Control
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
        const string applicationCSharpSource = """
            public partial class Counter : Akbura.AkburaControl
            {
            }
            """;
        const string stylesSource = """
            @using Avalonia.Controls;

            @utilities {
                StackPanel.gap-(double value) {
                }

                Border.border-(string color)-(int shade) {
                }
            }
            """;
        const string componentSource = """
            using Avalonia.Controls;

            <StackPanel gap-4>
                <Border border-slate-800/>
            </StackPanel>
            """;
        const string globalUsingsSource = """
            using Library.Styles.akcss;
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceDefinitionTests),
            Guid.NewGuid().ToString("N"));
        var libraryDirectory = Path.Combine(
            directory,
            "Library");
        var applicationDirectory = Path.Combine(
            directory,
            "Application");
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(applicationDirectory);

        try
        {
            var libraryProjectId =
                ProjectId.CreateNewId("Library");
            var applicationProjectId =
                ProjectId.CreateNewId("Application");
            var platformReferences =
                CreatePlatformReferences();
            var libraryCompilation =
                CSharpCompilation.Create(
                    "Library",
                    [CSharpSyntaxTree.ParseText(
                        libraryCSharpSource)],
                    platformReferences,
                    new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary));
            var applicationCompilation =
                CSharpCompilation.Create(
                    "Application",
                    [CSharpSyntaxTree.ParseText(
                        applicationCSharpSource)],
                    platformReferences.Append(
                        libraryCompilation.ToMetadataReference()),
                    new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary));
            var libraryContext = new ProjectContext(
                libraryProjectId,
                Path.Combine(
                    libraryDirectory,
                    "Library.csproj"),
                libraryDirectory,
                "Library",
                libraryCompilation,
                ImmutableArray<ProjectReference>.Empty);
            var applicationContext = new ProjectContext(
                applicationProjectId,
                Path.Combine(
                    applicationDirectory,
                    "Application.csproj"),
                applicationDirectory,
                "Application",
                applicationCompilation,
                [new ProjectReference(libraryProjectId)]);
            var stylesPath = Path.Combine(
                libraryDirectory,
                "Styles.akcss");
            var componentPath = Path.Combine(
                applicationDirectory,
                "Counter.akbura");
            var globalUsingsPath = Path.Combine(
                applicationDirectory,
                "GlobalUsings.akbura");
            File.WriteAllText(stylesPath, stylesSource);
            File.WriteAllText(componentPath, componentSource);
            File.WriteAllText(
                globalUsingsPath,
                globalUsingsSource);

            using var workspace = new AkburaWorkspace();
            var libraryProject =
                workspace.AddOrUpdateProject(
                    libraryContext);
            workspace.OpenOrChangeDocumentContext(
                libraryProject.Id,
                new Uri(stylesPath),
                SourceText.From(stylesSource));
            var applicationProject =
                workspace.AddOrUpdateProject(
                    applicationContext);
            var componentText =
                SourceText.From(componentSource);
            var componentContext =
                workspace.OpenOrChangeDocumentContext(
                    applicationProject.Id,
                    new Uri(componentPath),
                    componentText);

            void AssertUtilityDefinition(
                string utilityText,
                string declarationName)
            {
                var utilityStart = componentSource.IndexOf(
                    utilityText,
                    StringComparison.Ordinal);
                for (var offset = 0;
                     offset < utilityText.Length;
                     offset++)
                {
                    var definition =
                        workspace.LanguageServices.Definition
                            .GetDefinition(
                                componentContext,
                                utilityStart + offset);

                    Assert.NotNull(definition);
                    Assert.Null(definition!.TargetText);
                    Assert.Equal(
                        Path.GetFullPath(stylesPath),
                        definition.TargetFilePath);
                    Assert.Equal(
                        declarationName,
                        GetTargetText(definition));
                }
            }

            AssertUtilityDefinition("gap-4", "gap");
            AssertUtilityDefinition(
                "border-slate-800",
                "border");

            workspace.OpenOrChangeDocumentContext(
                applicationProject.Id,
                new Uri(globalUsingsPath),
                SourceText.From(globalUsingsSource));
            componentContext =
                workspace.OpenOrChangeDocumentContext(
                    applicationProject.Id,
                    new Uri(componentPath),
                    componentText);

            AssertUtilityDefinition("gap-4", "gap");
            AssertUtilityDefinition(
                "border-slate-800",
                "border");

            const string changedStylesSource = """
                @using Avalonia.Controls;

                @utilities {
                    StackPanel.space-(double value) {
                    }
                }
                """;
            workspace.OpenOrChangeDocumentContext(
                libraryProject.Id,
                new Uri(stylesPath),
                SourceText.From(changedStylesSource));
            componentContext =
                workspace.OpenOrChangeDocumentContext(
                    applicationProject.Id,
                    new Uri(componentPath),
                    componentText);

            Assert.Null(
                workspace.LanguageServices.Definition
                    .GetDefinition(
                        componentContext,
                        componentSource.IndexOf(
                            "gap-4",
                            StringComparison.Ordinal)));
            Assert.Null(
                workspace.LanguageServices.Definition
                    .GetDefinition(
                        componentContext,
                        componentSource.IndexOf(
                            "border-slate-800",
                            StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WithWorkspace(
        Action<
            AkburaWorkspace,
            AkburaDocumentContext,
            SourceText> assertion)
    {
        const string stylesSource = """
            @utilities {
                .gap-(double value) {
                }
            }
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceDefinitionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var libraryReference = CreateLibraryReference(
                directory,
                stylesSource);
            var compilation = CreateApplicationCompilation(
                libraryReference);
            var context = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: directory,
                rootNamespace: string.Empty,
                compilation,
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(context);
            var text = SourceText.From(ComponentSource);
            var documentContext = workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory, "Counter.akbura")),
                text);

            assertion(workspace, documentContext, text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string GetTargetText(
        AkburaDefinition definition)
    {
        var text = definition.TargetText ??
            SourceText.From(
                File.ReadAllText(
                    definition.TargetFilePath));
        var startLine = text.Lines[
            definition.TargetLineSpan.Start.Line];
        var endLine = text.Lines[
            definition.TargetLineSpan.End.Line];
        var span = TextSpan.FromBounds(
            startLine.Start +
                definition.TargetLineSpan.Start.Character,
            endLine.Start +
                definition.TargetLineSpan.End.Character);
        return text.ToString(span);
    }

    private static PortableExecutableReference CreateLibraryReference(
        string directory,
        string stylesSource)
    {
        const string librarySource = """
            using System;

            [assembly: Akbura.CompilerAnotations.AkcssModuleReference(
                typeof(Library.Generated.StylesModule))]

            namespace Akbura.CompilerAnotations
            {
                [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class AkcssModuleReferenceAttribute : Attribute
                {
                    public AkcssModuleReferenceAttribute(Type moduleType)
                    {
                    }
                }

                [AttributeUsage(AttributeTargets.Class)]
                public sealed class AkcssModuleAttribute : Attribute
                {
                    public AkcssModuleAttribute(string path)
                    {
                    }

                    public string MetadataName { get; set; } = "";

                    public int FormatVersion { get; set; }
                }

                [AttributeUsage(AttributeTargets.Class)]
                public sealed class AkcssSymbolAttribute : Attribute
                {
                    public string Name { get; set; } = "";

                    public string MetadataName { get; set; } = "";

                    public AkcssSymbolKind Kind { get; set; }

                    public int RuntimeStyleIndex { get; set; }
                }

                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public sealed class AkcssUtilityParameterAttribute : Attribute
                {
                    public int Ordinal { get; set; }

                    public string Name { get; set; } = "";

                    public Type Type { get; set; } = typeof(object);
                }

                public enum AkcssSymbolKind
                {
                    Style,
                    Utility,
                    Intercept,
                }
            }

            namespace Library.Generated
            {
                [Akbura.CompilerAnotations.AkcssModule(
                    "Styles.akcss",
                    MetadataName = "Library.Styles.akcss",
                    FormatVersion = 3)]
                public sealed class StylesModule
                {
                    [Akbura.CompilerAnotations.AkcssSymbol(
                        Name = "gap",
                        MetadataName = "gap",
                        Kind = Akbura.CompilerAnotations.AkcssSymbolKind.Utility,
                        RuntimeStyleIndex = 0)]
                    [Akbura.CompilerAnotations.AkcssUtilityParameter(
                        Ordinal = 0,
                        Name = "value",
                        Type = typeof(double))]
                    public sealed class GapUtility
                    {
                    }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                }

                public sealed class StackPanel : Control
                {
                }
            }
            """;
        var libraryCompilation = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(librarySource)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var manifest = AkburaModuleManifestBuilder.Build(
            "Library",
            "Library",
            [new AkburaModuleSourceText(
                "Styles.akcss",
                stylesSource)],
            libraryCompilation);
        using var manifestStream = new MemoryStream();
        AkburaModuleManifestSerializer.Write(
            manifestStream,
            manifest);

        var resources = new[]
        {
            CreateResource(
                AkburaModuleManifest.ResourceName,
                manifestStream.ToArray()),
            CreateEmbeddedSourceResource(
                "Styles.akcss",
                stylesSource),
        };
        var assemblyPath = Path.Combine(
            directory,
            "Library.dll");
        var result = libraryCompilation.Emit(
            assemblyPath,
            manifestResources: resources);
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromFile(
            assemblyPath);
    }

    private static CSharpCompilation CreateApplicationCompilation(
        MetadataReference libraryReference)
    {
        const string componentTypes = """
            public partial class Counter : Akbura.AkburaControl
            {
            }
            """;
        return CreateApplicationCompilation(
            libraryReference,
            componentTypes);
    }

    private static CSharpCompilation CreateApplicationCompilation(
        MetadataReference libraryReference,
        string componentTypes)
    {
        const string baseSource = """
            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }
            """;
        return CSharpCompilation.Create(
            "Application",
            [CSharpSyntaxTree.ParseText(
                baseSource + Environment.NewLine + componentTypes)],
            CreatePlatformReferences()
                .Append(libraryReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
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

    private static ResourceDescription CreateEmbeddedSourceResource(
        string name,
        string content)
    {
        var preamble = Encoding.Unicode.GetPreamble();
        var text = Encoding.Unicode.GetBytes(content);
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
            () => new MemoryStream(
                content,
                writable: false),
            isPublic: true);
    }
}
