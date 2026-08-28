using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceQuickInfoTests
{
    [Fact]
    public void QuickInfo_NativeAkcssReferencesUseAlignedSemanticSymbols()
    {
        const string source = """
            @using Avalonia.Controls;

            @utilities {
                Control.gap-(double value) {
                    Width: value;
                }

                Control.consumer {
                    @apply gap-4 missing gap-5;
                }
            }

            Control.card {
                Width: 120;
            }
            """;

        using var workspace = CreateWorkspace();
        var path = Path.GetFullPath("Styles.akcss");
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(path),
            SourceText.From(source));
        var service = workspace.LanguageServices.QuickInfo;

        AssertQuickInfo(
            "Width: 120",
            "Width",
            AkburaQuickInfoKind.Property,
            "double Control.Width { get; set; }");
        AssertQuickInfo(
            "Control.card",
            "card",
            AkburaQuickInfoKind.Style,
            "style Control.card");
        AssertQuickInfo(
            "Control.gap-",
            "gap",
            AkburaQuickInfoKind.Utility,
            "utility Control.gap(double value)");
        AssertQuickInfo(
            "double value",
            "value",
            AkburaQuickInfoKind.Parameter,
            "double value");
        AssertQuickInfo(
            "gap-4 missing",
            "gap-4",
            AkburaQuickInfoKind.Utility,
            "utility Control.gap(double value)");
        Assert.Null(service.GetQuickInfo(
            context,
            source.IndexOf("missing", StringComparison.Ordinal)));
        AssertQuickInfo(
            "missing gap-5",
            "gap-5",
            AkburaQuickInfoKind.Utility,
            "utility Control.gap(double value)");

        void AssertQuickInfo(
            string occurrence,
            string referenceText,
            AkburaQuickInfoKind kind,
            string signature)
        {
            var occurrenceStart = source.IndexOf(
                occurrence,
                StringComparison.Ordinal);
            Assert.True(occurrenceStart >= 0);
            var referenceStart = occurrenceStart + occurrence.IndexOf(
                referenceText,
                StringComparison.Ordinal);

            var quickInfo = service.GetQuickInfo(context, referenceStart);

            Assert.NotNull(quickInfo);
            Assert.Equal(kind, quickInfo!.Kind);
            Assert.Equal(signature, quickInfo.Signature);
            Assert.Equal(
                referenceText,
                source.Substring(
                    quickInfo.SourceSpan.Start,
                    quickInfo.SourceSpan.Length));
        }
    }

    [Fact]
    public void QuickInfoAndDefinition_LocalModuleImportUseModuleNameSpan()
    {
        const string sharedSource = """
            @using Avalonia.Controls;

            @utilities {
                Control.gap-(double value) {
                    Width: value;
                }
            }

            Control.card {
                Width: 1;
            }
            """;
        const string consumerSource = """
            @using Shared.akcss;
            @using Avalonia.Controls;

            Control.consumer {
                @apply card gap-4;
            }
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceQuickInfoTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var workspace = CreateWorkspace(directory);
            var sharedPath = Path.Combine(directory, "Shared.akcss");
            var consumerPath = Path.Combine(directory, "Consumer.akcss");
            workspace.OpenOrChangeDocumentContext(
                new Uri(sharedPath),
                SourceText.From(sharedSource));
            var context = workspace.OpenOrChangeDocumentContext(
                new Uri(consumerPath),
                SourceText.From(consumerSource));
            var importStart = consumerSource.IndexOf(
                "Shared.akcss",
                StringComparison.Ordinal);

            var quickInfo = workspace.LanguageServices.QuickInfo
                .GetQuickInfo(context, importStart);
            var definition = workspace.LanguageServices.Definition
                .GetDefinition(context, importStart);

            Assert.NotNull(quickInfo);
            Assert.Equal(AkburaQuickInfoKind.Module, quickInfo!.Kind);
            Assert.Equal("AKCSS module Shared.akcss", quickInfo.Signature);
            Assert.Contains("Styles: 1 · Utilities: 1", quickInfo.Details);
            Assert.Equal(
                "Shared.akcss",
                consumerSource.Substring(
                    quickInfo.SourceSpan.Start,
                    quickInfo.SourceSpan.Length));
            Assert.NotNull(definition);
            Assert.Equal(Path.GetFullPath(sharedPath), definition!.TargetFilePath);
            Assert.Equal(0, definition.TargetLineSpan.Start.Line);
            Assert.Equal(
                "Shared.akcss",
                consumerSource.Substring(
                    definition.SourceSpan.Start,
                    definition.SourceSpan.Length));

            var applyStart = consumerSource.IndexOf(
                "card gap-4",
                StringComparison.Ordinal);
            Assert.Equal(
                "style Control.card",
                workspace.LanguageServices.QuickInfo
                    .GetQuickInfo(context, applyStart)!
                    .Signature);
            Assert.Equal(
                "utility Control.gap(double value)",
                workspace.LanguageServices.QuickInfo
                    .GetQuickInfo(
                        context,
                        applyStart + "card ".Length)!
                    .Signature);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void QuickInfoAndDefinition_ProjectReferenceModuleUsePhysicalSource()
    {
        const string sharedSource = """
            @using Avalonia.Controls;

            @utilities {
                Control.gap-(double value) {
                    Width: value;
                }
            }
            """;
        const string consumerSource = """
            @using Library.Styles.akcss;
            @using Avalonia.Controls;

            Control.consumer {
                @apply gap-4;
            }
            """;
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceQuickInfoTests),
            Guid.NewGuid().ToString("N"));
        var libraryDirectory = Path.Combine(directory, "Library");
        var applicationDirectory = Path.Combine(directory, "Application");
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(applicationDirectory);

        try
        {
            var libraryId = ProjectId.CreateNewId("Library");
            var applicationId = ProjectId.CreateNewId("Application");
            var libraryCompilation = CreateCSharpCompilation("Library");
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
            var sharedPath = Path.Combine(libraryDirectory, "Styles.akcss");
            File.WriteAllText(sharedPath, sharedSource);
            workspace.OpenOrChangeDocumentContext(
                library.Id,
                new Uri(sharedPath),
                SourceText.From(sharedSource));
            var application = workspace.AddOrUpdateProject(new ProjectContext(
                applicationId,
                Path.Combine(applicationDirectory, "Application.csproj"),
                applicationDirectory,
                "Library",
                applicationCompilation,
                [new ProjectReference(libraryId)]));
            var consumerPath = Path.Combine(
                applicationDirectory,
                "Consumer.akcss");
            var context = workspace.OpenOrChangeDocumentContext(
                application.Id,
                new Uri(consumerPath),
                SourceText.From(consumerSource));
            var position = consumerSource.IndexOf(
                "Library.Styles.akcss",
                StringComparison.Ordinal);
            var applyPosition = consumerSource.IndexOf(
                "gap-4",
                StringComparison.Ordinal);

            var quickInfo = workspace.LanguageServices.QuickInfo
                .GetQuickInfo(context, position);
            var definition = workspace.LanguageServices.Definition
                .GetDefinition(context, position);

            Assert.NotNull(quickInfo);
            Assert.Equal(
                "AKCSS module Library.Styles.akcss",
                quickInfo!.Signature);
            Assert.NotNull(definition);
            Assert.Equal(
                Path.GetFullPath(sharedPath),
                definition!.TargetFilePath);
            Assert.Null(definition.TargetText);
            var projectApplyInfo = workspace.LanguageServices.QuickInfo
                .GetQuickInfo(context, applyPosition);
            var projectApplyDefinition = workspace.LanguageServices.Definition
                .GetDefinition(context, applyPosition);
            Assert.NotNull(projectApplyInfo);
            Assert.Equal(
                "utility Control.gap(double value)",
                projectApplyInfo!.Signature);
            Assert.NotNull(projectApplyDefinition);
            Assert.Equal(
                Path.GetFullPath(sharedPath),
                projectApplyDefinition!.TargetFilePath);
            Assert.Contains(
                workspace.LanguageServices.Classification.GetClassifications(
                    context,
                    new TextSpan(applyPosition, "gap-4".Length)),
                item => item.Span == projectApplyInfo.SourceSpan &&
                    item.Kind == AkburaClassificationKind.Utility);

            var localStylesPath = Path.Combine(
                applicationDirectory,
                "Styles.akcss");
            File.WriteAllText(localStylesPath, sharedSource);
            workspace.OpenOrChangeDocumentContext(
                application.Id,
                new Uri(localStylesPath),
                SourceText.From(sharedSource));
            context = workspace.OpenOrChangeDocumentContext(
                application.Id,
                new Uri(consumerPath),
                SourceText.From(consumerSource));

            var localDefinition = workspace.LanguageServices.Definition
                .GetDefinition(context, position);
            var localApplyDefinition = workspace.LanguageServices.Definition
                .GetDefinition(context, applyPosition);

            Assert.NotNull(localDefinition);
            Assert.Equal(
                Path.GetFullPath(localStylesPath),
                localDefinition!.TargetFilePath);
            Assert.NotNull(localApplyDefinition);
            Assert.Equal(
                Path.GetFullPath(localStylesPath),
                localApplyDefinition!.TargetFilePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void QuickInfoAndDefinition_AmbiguousOrMissingModuleImportReturnsNull()
    {
        const string moduleSource = """
            @using Avalonia.Controls;

            Control.card {
                Width: 1;
            }
            """;
        const string consumerSource = """
            @using A.B.akcss;
            @using Missing.akcss;
            """;
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceQuickInfoTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "A"));

        try
        {
            using var workspace = CreateWorkspace(directory);
            workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory, "A.B.akcss")),
                SourceText.From(moduleSource));
            workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory, "A", "B.akcss")),
                SourceText.From(moduleSource));
            var context = workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory, "Consumer.akcss")),
                SourceText.From(consumerSource));

            AssertNoReference("A.B.akcss");
            AssertNoReference("Missing.akcss");

            void AssertNoReference(string moduleName)
            {
                var position = consumerSource.IndexOf(
                    moduleName,
                    StringComparison.Ordinal);
                Assert.Null(workspace.LanguageServices.QuickInfo
                    .GetQuickInfo(context, position));
                Assert.Null(workspace.LanguageServices.Definition
                    .GetDefinition(context, position));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Classification_ApplyReferencesStayAlignedAndDoNotOverlap()
    {
        const string source = """
            @using Avalonia.Controls;

            @utilities {
                Control.gap-(double value) {
                    Width: value;
                }

                Control.consumer {
                    @apply gap-4 missing gap-5;
                }
            }
            """;

        using var workspace = CreateWorkspace();
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("Styles.akcss")),
            text);
        var apply = Assert.Single(
            context.Document.SyntaxTree.GetRootSyntax()
                .DescendantNodes()
                .OfType<AkcssApplyDirectiveSyntax>());
        var references = new AkcssReferenceResolver()
            .GetApplyReferences(context, apply);

        Assert.Equal(3, references.Length);
        Assert.NotNull(references[0].Symbol);
        Assert.Null(references[1].Symbol);
        Assert.NotNull(references[2].Symbol);
        Assert.Equal("gap-4", text.ToString(references[0].SourceSpan));
        Assert.Equal("missing", text.ToString(references[1].SourceSpan));
        Assert.Equal("gap-5", text.ToString(references[2].SourceSpan));

        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(
                context,
                apply.Span);
        foreach (var reference in references.Where(
                     static item => item.Symbol != null))
        {
            Assert.Contains(
                classifications,
                item => item.Span == reference.SourceSpan &&
                    item.Kind == AkburaClassificationKind.Utility);
            Assert.DoesNotContain(
                classifications,
                item => item.Span != reference.SourceSpan &&
                    reference.SourceSpan.Contains(item.Span));
        }

        var unresolvedReference = references[1];
        Assert.Contains(
            classifications,
            item => item.Span == unresolvedReference.SourceSpan &&
                item.Kind == AkburaClassificationKind.Identifier);
        Assert.DoesNotContain(
            classifications,
            item => item.Span == unresolvedReference.SourceSpan &&
                item.Kind == AkburaClassificationKind.Utility);
    }

    [Fact]
    public void QuickInfoAndClassification_WorkInsideInlineAkcss()
    {
        const string source = """
            using Avalonia.Controls;

            @akcss {
                @utilities {
                    Control.gap-(double value) {
                        Width: value;
                    }
                }

                Control.card {
                    Width: 1;
                    @apply gap-4;
                }
            }

            <Control/>
            """;

        using var workspace = CreateWorkspace();
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("Inline.akbura")),
            text);
        var propertyStart = source.IndexOf(
            "Width: 1",
            StringComparison.Ordinal);
        var applyStart = source.IndexOf(
            "gap-4",
            StringComparison.Ordinal);

        Assert.Equal(
            "double Control.Width { get; set; }",
            workspace.LanguageServices.QuickInfo
                .GetQuickInfo(context, propertyStart)!
                .Signature);
        var applyInfo = workspace.LanguageServices.QuickInfo
            .GetQuickInfo(context, applyStart);
        Assert.NotNull(applyInfo);
        Assert.Equal(
            "utility Control.gap(double value)",
            applyInfo!.Signature);

        var propertyDefinition = workspace.LanguageServices.Definition
            .GetDefinition(context, propertyStart);
        var applyDefinition = workspace.LanguageServices.Definition
            .GetDefinition(context, applyStart);
        Assert.NotNull(propertyDefinition);
        Assert.NotNull(applyDefinition);
        Assert.Equal(
            propertyStart,
            propertyDefinition!.SourceSpan.Start);
        Assert.Equal(
            applyInfo.SourceSpan,
            applyDefinition!.SourceSpan);

        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(context, new TextSpan(0, text.Length));
        Assert.Contains(
            classifications,
            item => item.Span == applyInfo.SourceSpan &&
                item.Kind == AkburaClassificationKind.Utility);
    }

    [Fact]
    public void QuickInfoAndDefinition_QualifiedPropertySeparateOwnerAndName()
    {
        const string source = """
            @using Avalonia.Controls;

            Control.card {
                Grid.Row: 1;
            }
            """;

        using var workspace = CreateWorkspace();
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("Styles.akcss")),
            SourceText.From(source));
        var ownerStart = source.IndexOf("Grid.Row", StringComparison.Ordinal);
        var propertyStart = ownerStart + "Grid.".Length;

        var ownerInfo = workspace.LanguageServices.QuickInfo
            .GetQuickInfo(context, ownerStart);
        var propertyInfo = workspace.LanguageServices.QuickInfo
            .GetQuickInfo(context, propertyStart);
        var ownerDefinition = workspace.LanguageServices.Definition
            .GetDefinition(context, ownerStart);
        var propertyDefinition = workspace.LanguageServices.Definition
            .GetDefinition(context, propertyStart);

        Assert.NotNull(ownerInfo);
        Assert.Equal(AkburaQuickInfoKind.Type, ownerInfo!.Kind);
        Assert.Equal("class Grid", ownerInfo.Signature);
        Assert.Equal("Grid", source.Substring(
            ownerInfo.SourceSpan.Start,
            ownerInfo.SourceSpan.Length));
        Assert.NotNull(propertyInfo);
        Assert.Equal(AkburaQuickInfoKind.Property, propertyInfo!.Kind);
        Assert.Equal(
            "int Grid.Row { get; set; }",
            propertyInfo.Signature);
        Assert.Contains("Avalonia attached property", propertyInfo.Details);
        Assert.Equal("Row", source.Substring(
            propertyInfo.SourceSpan.Start,
            propertyInfo.SourceSpan.Length));
        Assert.NotNull(ownerDefinition);
        Assert.NotNull(propertyDefinition);
        Assert.Equal(ownerInfo.SourceSpan, ownerDefinition!.SourceSpan);
        Assert.Equal(propertyInfo.SourceSpan, propertyDefinition!.SourceSpan);
    }

    private static AkburaWorkspace CreateWorkspace(
        string? projectDirectory = null)
    {
        projectDirectory ??= Environment.CurrentDirectory;
        return new AkburaWorkspace(new ProjectContext(
            ProjectId.CreateNewId(),
            projectFilePath: string.Empty,
            projectDirectory,
            rootNamespace: string.Empty,
            CreateCSharpCompilation("WorkspaceQuickInfoTests"),
            ImmutableArray<ProjectReference>.Empty));
    }

    private static CSharpCompilation CreateCSharpCompilation(
        string assemblyName)
    {
        const string csharpSource = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public double Width { get; set; }
                }

                public sealed class Grid : Control
                {
                    public static readonly Avalonia.AttachedProperty<int>
                        RowProperty = new();

                    public static int GetRow(Control control) => 0;

                    public static void SetRow(Control control, int value)
                    {
                    }
                }
            }

            namespace Avalonia
            {
                public sealed class AttachedProperty<T>
                {
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }

            public partial class Inline : Akbura.AkburaControl
            {
            }
            """;

        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(csharpSource)],
            trustedPlatformAssemblies.Select(static path =>
                MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> GetPlatformReferences()
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
        return trustedPlatformAssemblies.Select(static path =>
            MetadataReference.CreateFromFile(path));
    }
}
