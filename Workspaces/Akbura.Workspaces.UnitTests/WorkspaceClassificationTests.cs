using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceClassificationTests
{
    [Fact]
    public void SyntacticDiagnostics_ReportSkippedTextWithoutSemanticContext()
    {
        const string source =
            "<HelloWorld !=1233fd3--- ><Button/></HelloWorld>";

        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Counter.akbura");
        var invalidText = "!=1233fd3---";
        var invalidSpan = new TextSpan(
            source.IndexOf(
                invalidText,
                StringComparison.Ordinal),
            invalidText.Length);

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetSyntacticDiagnostics(
                document,
                new TextSpan(0, text.Length));

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Severity ==
                    AkburaDiagnosticSeverity.Error &&
                diagnostic.Span.OverlapsWith(invalidSpan));
    }

    [Fact]
    public void SyntacticDiagnostics_PreserveMissingTokenPosition()
    {
        const string source =
            "<StackPanel gap-3\n<Button/></StackPanel>";

        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Counter.akbura");
        var missingClosePosition = source.IndexOf('\n');

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetSyntacticDiagnostics(
                document,
                new TextSpan(0, text.Length));

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Span ==
                    new TextSpan(missingClosePosition, 0));
    }

    [Fact]
    public void SyntacticDiagnostics_InvalidAttributeReportsExpectedAttribute()
    {
        const string source = "<StackPanel gap-3 312132/>";

        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Counter.akbura");
        var invalidSpan = new TextSpan(
            source.IndexOf("312132", StringComparison.Ordinal),
            "312132".Length);

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetSyntacticDiagnostics(
                document,
                new TextSpan(0, text.Length));

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Code == ErrorCodes.ERR_SyntaxError &&
                diagnostic.Span.OverlapsWith(invalidSpan) &&
                diagnostic.Message ==
                    "Syntax error; 'attribute' expected.");
        Assert.DoesNotContain(
            diagnostics,
            static diagnostic =>
                diagnostic.Message.Contains(
                    "System.Object[]",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void SyntacticDiagnostics_DoNotRequireProjectSemanticReferences()
    {
        const string source = """
            state int count = 0;

            <StackPanel gap-3>
                <TextBlock Text={count}/>
            </StackPanel>
            """;

        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Counter.akbura");

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetSyntacticDiagnostics(
                document,
                new TextSpan(0, text.Length));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic =>
                diagnostic.Code.StartsWith(
                    "AKBURA_SEMANTIC_",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            static diagnostic =>
                diagnostic.Message.Contains(
                    "not defined or imported",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticDiagnostics_ReportUnknownComponentName()
    {
        const string source = """
            using Avalonia.Controls;

            <UnknownControl/>
            """;

        using var workspace = CreateSemanticWorkspace();
        var text = SourceText.From(source);
        var filePath = Path.GetFullPath("Counter.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(filePath),
            text);
        var componentStart = source.IndexOf(
            "UnknownControl",
            StringComparison.Ordinal);

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetDiagnostics(
                context,
                new TextSpan(0, text.Length));

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Code ==
                    "AKBURA_SEMANTIC_MarkupComponentNotFound" &&
                diagnostic.Span == new TextSpan(
                    componentStart,
                    "UnknownControl".Length));
    }

    [Fact]
    public void SyntacticClassification_DoesNotRequireSemanticContext()
    {
        const string source = """
            state int count = 0;

            <UnknownControl UnknownProperty={missing + 1}/>
            """;

        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Counter.akbura");

        var classifications =
            workspace.LanguageServices.Classification
                .GetSyntacticClassifications(
                    document,
                    new TextSpan(0, text.Length));

        AssertClassification(
            text,
            classifications,
            "state",
            AkburaClassificationKind.Keyword);

        AssertClassification(
            text,
            classifications,
            "UnknownControl",
            AkburaClassificationKind.Component);

        AssertClassification(
            text,
            classifications,
            "UnknownProperty",
            AkburaClassificationKind.Attribute);

        AssertClassification(
            text,
            classifications,
            "missing",
            AkburaClassificationKind.Identifier);

        AssertClassification(
            text,
            classifications,
            "1",
            AkburaClassificationKind.Number);
    }

    [Fact]
    public void SyntacticClassification_UsesAkcssParserFromFilePath()
    {
        const string source = """
            @utilities {
                UnknownControl.card {
                    UnknownProperty: 10;
                }
            }
            """;

        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);

        var classifications =
            workspace.LanguageServices.Classification
                .GetSyntacticClassifications(
                    text,
                    "Styles.akcss",
                    new TextSpan(0, text.Length));

        AssertClassification(
            text,
            classifications,
            "utilities",
            AkburaClassificationKind.Directive);

        AssertClassification(
            text,
            classifications,
            "UnknownControl",
            AkburaClassificationKind.Identifier);

        AssertClassification(
            text,
            classifications,
            "card",
            AkburaClassificationKind.Utility);

        AssertClassification(
            text,
            classifications,
            "10",
            AkburaClassificationKind.Number);
    }

    [Fact]
    public void SemanticClassification_RefinesSyntacticClassification()
    {
        const string source = """
            using System;

            state int count = 0;

            Console.WriteLine(count);

            <UnknownControl/>
            """;

        using var workspace = CreateSemanticWorkspace();
        var text = SourceText.From(source);
        var filePath = Path.GetFullPath("Counter.akbura");
        var requestedSpan = new TextSpan(0, text.Length);
        var referenceStart = source.IndexOf(
            "Console",
            StringComparison.Ordinal);
        var referenceSpan = new TextSpan(
            referenceStart,
            "Console".Length);

        var syntactic =
            workspace.LanguageServices.Classification
                .GetSyntacticClassifications(
                    text,
                    filePath,
                    requestedSpan);

        var context =
            workspace.OpenOrChangeDocumentContext(
                new Uri(filePath),
                text);

        var semantic =
            workspace.LanguageServices.Classification
                .GetClassifications(
                    context,
                    requestedSpan);

        Assert.Contains(
            syntactic,
            classification =>
                classification.Span == referenceSpan &&
                classification.Kind ==
                    AkburaClassificationKind.Identifier);

        Assert.Contains(
            semantic,
            classification =>
                classification.Span == referenceSpan &&
                classification.Kind ==
                    AkburaClassificationKind.ClassName);

        Assert.DoesNotContain(
            semantic,
            classification =>
                classification.Span == referenceSpan &&
                classification.Kind ==
                    AkburaClassificationKind.Identifier);
    }

    [Fact]
    public void SemanticWorkspace_ComponentAfterGlobalUsingsCanBeEditedRepeatedly()
    {
        using var workspace = CreateSemanticWorkspace();
        var globalUsingsPath = Path.GetFullPath("GlobalUsings.akbura");
        var componentPath = Path.GetFullPath("Counter.akbura");

        workspace.OpenOrChangeDocumentContext(
            new Uri(globalUsingsPath),
            SourceText.From("global using Avalonia.Controls;"));

        var initialText = SourceText.From(
            "state string text = \"\";\n\n<Button/>");
        var initialContext = workspace.OpenOrChangeDocumentContext(
            new Uri(componentPath),
            initialText);
        _ = workspace.LanguageServices.Classification.GetClassifications(
            initialContext,
            new TextSpan(0, initialText.Length));

        var firstEdit = SourceText.From(
            "state string text = \"б\";\n\n<Button/>");
        var firstContext = workspace.OpenOrChangeDocumentContext(
            new Uri(componentPath),
            firstEdit);

        var secondEdit = SourceText.From(
            "state string text = \"ба\";\n\n<Button/>");
        var secondContext = workspace.OpenOrChangeDocumentContext(
            new Uri(componentPath),
            secondEdit);

        Assert.True(firstContext.Document.Text.ContentEquals(firstEdit));
        Assert.True(secondContext.Document.Text.ContentEquals(secondEdit));
        Assert.NotEmpty(
            workspace.LanguageServices.Classification.GetClassifications(
                secondContext,
                new TextSpan(0, secondEdit.Length)));
    }

    [Fact]
    public void SemanticClassification_ImportedAkcssUtilityUsesOwningSemanticModel()
    {
        const string stylesSource = """
            @utilities {
                .probe {
                    Width: Amx.DynamicResource<double>("--probe");
                }
            }
            """;
        const string componentSource = """
            using System;
            using Avalonia.Controls;
            using Styles.akcss;

            <Button Value={Console.Out} probe/>
            """;

        using var workspace = CreateSemanticWorkspace();
        var stylesPath = Path.GetFullPath("Styles.akcss");
        var componentPath = Path.GetFullPath("Counter.akbura");

        workspace.OpenOrChangeDocumentContext(
            new Uri(stylesPath),
            SourceText.From(stylesSource));

        var componentText = SourceText.From(componentSource);
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(componentPath),
            componentText);

        var classifications =
            workspace.LanguageServices.Classification
                .GetClassifications(
                    context,
                    new TextSpan(0, componentText.Length));

        AssertClassification(
            componentText,
            classifications,
            "Console",
            AkburaClassificationKind.ClassName);

        var componentTree = ComponentSyntaxTree.ParseText(
            componentText,
            componentPath);
        var stylesTree = AkcssSyntaxTree.ParseText(
            stylesSource,
            stylesPath);
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [componentTree],
            [stylesTree]);
        var semanticModel = compilation.GetSemanticModel(componentTree);
        var utilityAttribute = Assert.Single(
            componentTree
                .GetRoot()
                .DescendantNodes()
                .OfType<TailwindFlagAttributeSyntax>());

        var operation =
            Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                semanticModel.GetOperation(utilityAttribute));

        Assert.NotNull(operation.Utility);
        Assert.Single(operation.Utility!.Operations);

        var utilityDeclaration = Assert.Single(
            Assert.Single(
                    stylesTree.GetRoot().Members
                        .OfType<AkcssUtilitiesSectionSyntax>())
                .Utilities);
        var declaredUtility = compilation
            .GetSemanticModel(stylesTree)
            .GetDeclaredSymbol(utilityDeclaration);

        Assert.Same(declaredUtility, operation.Utility);
    }

    [Fact]
    public void SemanticDiagnostics_DiamondReferencesDoNotDuplicateAkcssUtility()
    {
        const string stylesSource = """
            @utilities {
                .gap-(double value) {
                }
            }
            """;
        const string componentSource = """
            using Avalonia.Controls;
            using Library.Styles.akcss;

            <Button gap-3/>
            """;

        var libraryCSharpCompilation =
            CreateCSharpCompilation().WithAssemblyName("Library");
        var bridgeCSharpCompilation =
            CreateCSharpCompilation().WithAssemblyName("Bridge");
        var applicationCSharpCompilation =
            CreateCSharpCompilation().WithAssemblyName("Application");
        var stylesTree = AkcssSyntaxTree.ParseText(
            stylesSource,
            "Styles.akcss",
            "Library.Styles.akcss");
        var componentTree = ComponentSyntaxTree.ParseText(
            componentSource,
            "Counter.akbura");
        var libraryCompilation = new AkburaCompilation(
            libraryCSharpCompilation,
            ImmutableArray<AkburaSyntaxTree>.Empty,
            [stylesTree]);
        var bridgeCompilation = new AkburaCompilation(
            bridgeCSharpCompilation,
            ImmutableArray<AkburaSyntaxTree>.Empty,
            ImmutableArray<AkcssSyntaxTree>.Empty,
            compilationReferences: [libraryCompilation.ToReference()]);
        var applicationCompilation = new AkburaCompilation(
            applicationCSharpCompilation,
            [componentTree],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            compilationReferences:
            [
                libraryCompilation.ToReference(),
                bridgeCompilation.ToReference(),
            ]);
        var semanticModel = applicationCompilation.GetSemanticModel(
            componentTree);
        var utilityAttribute = Assert.Single(
            componentTree.GetRoot()
                .DescendantNodes()
                .OfType<TailwindFullAttributeSyntax>());

        var operation =
            Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                semanticModel.GetOperation(utilityAttribute));
        var diagnostics = semanticModel.GetSemanticDiagnostics(
            componentTree.GetRoot());

        Assert.NotNull(operation.Utility);
        Assert.DoesNotContain(
            diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                    ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityAmbiguous);
    }

    [Fact]
    public void SemanticClassification_ImportedEmbeddedAkcssUtilityUsesOwningSemanticModel()
    {
        const string stylesSource = """
            @utilities {
                .probe {
                    Width: Amx.DynamicResource<double>("--probe");
                }
            }
            """;
        const string componentSource = """
            using System;
            using Avalonia.Controls;
            using Library.Styles.akcss;

            <Button Value={Console.Out} probe/>
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceClassificationTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var libraryReference = CreateEmbeddedAkcssReference(
                directory,
                stylesSource);
            var csharpCompilation = CreateCSharpCompilation(
                [libraryReference]);
            using var workspace = new AkburaWorkspace(
                CreateSemanticProjectContext(csharpCompilation));
            var componentPath = Path.GetFullPath("Counter.akbura");
            var componentText = SourceText.From(componentSource);
            var context = workspace.OpenOrChangeDocumentContext(
                new Uri(componentPath),
                componentText);

            var classifications =
                workspace.LanguageServices.Classification
                    .GetClassifications(
                        context,
                        new TextSpan(0, componentText.Length));

            AssertClassification(
                componentText,
                classifications,
                "Console",
                AkburaClassificationKind.ClassName);

            var componentTree = ComponentSyntaxTree.ParseText(
                componentText,
                componentPath);
            var compilation = new AkburaCompilation(
                csharpCompilation,
                [componentTree]);
            var referencedModule = Assert.Single(
                compilation.ReferencedModules);
            Assert.False(
                referencedModule.IsSyntaxTreeMaterialized(
                    "Styles.akcss"));

            var utilityAttribute = Assert.Single(
                componentTree
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<TailwindFlagAttributeSyntax>());
            var operation =
                Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                    compilation
                        .GetSemanticModel(componentTree)
                        .GetOperation(utilityAttribute));

            Assert.NotNull(operation.Utility);
            Assert.Single(operation.Utility!.Operations);
            Assert.True(
                referencedModule.IsSyntaxTreeMaterialized(
                    "Styles.akcss"));

            var declaration =
                Assert.IsType<AkcssUtilityDeclarationSyntax>(
                    operation.Utility.DeclarationSyntax);
            var owningTree = Assert.Single(
                referencedModule.GetAkcssSyntaxTreesByLogicalName(
                    "Library.Styles.akcss"));
            var owningModel = compilation.GetSemanticModel(owningTree);

            Assert.Same(
                owningModel.GetDeclaredSymbol(declaration),
                operation.Utility);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertClassification(
        SourceText text,
        IEnumerable<AkburaClassifiedSpan> classifications,
        string expectedText,
        AkburaClassificationKind expectedKind)
    {
        Assert.Contains(
            classifications,
            classification =>
                classification.Kind == expectedKind &&
                string.Equals(
                    text.ToString(classification.Span),
                    expectedText,
                    StringComparison.Ordinal));
    }

    private static AkburaWorkspace CreateSemanticWorkspace()
    {
        return new AkburaWorkspace(
            CreateSemanticProjectContext());
    }

    private static ProjectContext CreateSemanticProjectContext(
        CSharpCompilation? compilation = null)
    {
        compilation ??= CreateCSharpCompilation();

        return new ProjectContext(
            ProjectId.CreateNewId(),
            projectFilePath: string.Empty,
            projectDirectory: Environment.CurrentDirectory,
            rootNamespace: string.Empty,
            compilation,
            ImmutableArray<ProjectReference>.Empty);
    }

    private static CSharpCompilation CreateCSharpCompilation(
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        const string csharpSource = """
            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                    public double Width { get; set; }
                }

                public sealed class Button : Control
                {
                    public object? Value { get; set; }
                }
            }

            public partial class Counter : Akbura.AkburaControl
            {
            }
            """;

        var references = CreatePlatformReferences();
        if (additionalReferences != null)
        {
            references = references
                .Concat(additionalReferences)
                .ToArray();
        }

        return CSharpCompilation.Create(
            "WorkspaceClassificationTests",
            [CSharpSyntaxTree.ParseText(csharpSource)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static PortableExecutableReference CreateEmbeddedAkcssReference(
        string directory,
        string stylesSource)
    {
        var manifest = AkburaModuleManifestBuilder.Build(
            "Library",
            "Library",
            [new AkburaModuleSourceText("Styles.akcss", stylesSource)],
            CreateCSharpCompilation());
        using var manifestStream = new MemoryStream();
        AkburaModuleManifestSerializer.Write(manifestStream, manifest);

        var resources = new[]
        {
            CreateResource(
                AkburaModuleManifest.ResourceName,
                manifestStream.ToArray()),
            CreateEmbeddedSourceResource(
                "Styles.akcss",
                stylesSource),
        };
        var library = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(
                "public sealed class LibraryMarker { }")],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var assemblyPath = Path.Combine(directory, "Library.dll");
        var emitResult = library.Emit(
            assemblyPath,
            manifestResources: resources);

        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));

        return MetadataReference.CreateFromFile(assemblyPath);
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
            () => new MemoryStream(content, writable: false),
            isPublic: true);
    }
}
