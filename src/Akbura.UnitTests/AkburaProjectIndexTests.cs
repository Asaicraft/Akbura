using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkburaProjectIndexTests
{
    [Fact]
    public void LazyIndex_MatchesEagerCatalogIncludingImportedApplyAndInlineModules()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AkburaProjectIndexTests");
        var component = ComponentSyntaxTree.ParseText(
            SourceText.From(
                "using Avalonia.Controls;\r\nusing Demo.Styles.Derived.akcss;\r\n" +
                "@akcss { @using Avalonia.Controls; .local { Opacity: 0.5; } }\r\n" +
                "<Border class=\"local derived\" />\r\n"),
            Path.Combine(directory, "Views", "Example.akbura"));
        var shared = ParseAkcss(directory, "Shared", "@using Avalonia.Controls;\r\nBorder.shared { Width: 12; }\r\n");
        var derived = ParseAkcss(directory, "Derived",
            "@using Avalonia.Controls;\r\n@using Demo.Styles.Shared.akcss;\r\n" +
            "Border.derived { @apply shared; Height: 24; }\r\n");
        ImmutableArray<AkburaSyntaxTree> trees = [component, shared, derived];
        var compilation = CreateCompilation();
        var eager = AkburaGenerationCatalogBuilder.Create(compilation, trees, "Demo", directory);
        var index = AkburaProjectIndex.Create(compilation, trees, "Demo", directory);

        var descriptor = Assert.Single(index.ComponentDescriptors);
        Assert.Equal("Demo.Views.Example", descriptor.ComponentMetadataName);
        Assert.Equal("Views/Example.akbura", descriptor.SourcePath);
        var inline = Assert.Single(descriptor.InlineAkcssDescriptors);
        Assert.Same(inline, Assert.Single(index.InlineAkcssDescriptors));
        Assert.Same(descriptor.DocumentVersion, inline.DocumentVersion);
        Assert.Equal("Views/Example.akbura.inline.0.akcss", inline.ModuleIdentity);
        Assert.True(index.SourceTreeMap.TryGetSyntaxTree(inline.Root, out var owner));
        Assert.Same(component, owner);

        var actual = new Dictionary<string, SourceText>(StringComparer.Ordinal);

        // Generate the importer first: its @apply dependency has not been registered by a writer.
        foreach (var moduleDescriptor in index.ExternalAkcssDescriptors.Reverse().Concat(index.InlineAkcssDescriptors))
        {
            Assert.True(index.TryResolveAkcss(moduleDescriptor, out var input));
            actual.Add(AkcssDocumentWriter.GetHintName(input),
                AkcssDocumentWriter.Generate(input, index.AkcssSourceMap, index.RootNamespace));
        }

        Assert.True(index.TryResolveComponent(descriptor, out var componentInput));
        actual.Add(
            ComponentDocumentWriter.GetHintName(componentInput.Component, componentInput.SourcePath),
            ComponentDocumentWriter.Generate(
                componentInput.Component,
                componentInput.SemanticModel,
                componentInput.SourcePath,
                index.AkcssModuleTypeNames));

        foreach (var input in eager.ExternalAkcssModules.Concat(eager.InlineAkcssModules))
        {
            var expected = AkcssDocumentWriter.Generate(input, eager.AkcssSourceMap, eager.RootNamespace);
            Assert.Equal(expected.ToString(), actual[AkcssDocumentWriter.GetHintName(input)].ToString());
        }

        var eagerComponent = Assert.Single(eager.Components);
        var expectedComponent = ComponentDocumentWriter.Generate(
            eagerComponent.Component, eagerComponent.SemanticModel, eagerComponent.SourcePath, eager.AkcssModuleTypeNames);
        Assert.Equal(expectedComponent.ToString(), actual[
            ComponentDocumentWriter.GetHintName(eagerComponent.Component, eagerComponent.SourcePath)].ToString());

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var diagnostics = compilation.AddSyntaxTrees(actual.Select(pair =>
                CSharpSyntaxTree.ParseText(pair.Value, parseOptions, pair.Key)))
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .ToArray();
        Assert.True(diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    [Theory]
    [InlineData("component")]
    [InlineData("akcss")]
    [InlineData("csharp")]
    public void Create_WithReuseFrom_PreservesUnchangedDeclarations(string changedKind)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AkburaProjectIndexReuseTests");
        var first = ComponentSyntaxTree.ParseText(SourceText.From("state int count = 0;\r\n"), "First.akbura");
        var second = ComponentSyntaxTree.ParseText(SourceText.From("state int count = 1;\r\n"), "Second.akbura");
        var styles = ParseAkcss(directory, "Shared", "@using Avalonia.Controls;\r\nBorder.shared { Width: 12; }\r\n");
        var compilation = CreateCompilation();
        ImmutableArray<AkburaSyntaxTree> originalTrees = [first, second, styles];
        var previous = AkburaProjectIndex.Create(compilation, originalTrees, "Demo", directory);
        var previousTable = previous.Compilation.DeclarationTable;

        var currentFirst = changedKind == "component"
            ? ComponentSyntaxTree.ParseText(SourceText.From("state int count = 2;\r\n"), "First.akbura")
            : first;
        var currentStyles = changedKind == "akcss"
            ? ParseAkcss(directory, "Shared", "@using Avalonia.Controls;\r\nBorder.shared { Width: 24; }\r\n")
            : styles;
        var currentCSharp = changedKind == "csharp"
            ? compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal sealed class Unrelated { }\r\n"))
            : compilation;
        ImmutableArray<AkburaSyntaxTree> currentTrees = [currentFirst, second, currentStyles];
        var current = AkburaProjectIndex.Create(
            currentCSharp, currentTrees, "Demo", directory, previous.Compilation);
        var currentTable = current.Compilation.DeclarationTable;

        Assert.NotSame(previous.Compilation, current.Compilation);
        Assert.Same(previousTable.Components[1], currentTable.Components[1]);
        if (changedKind == "component")
        {
            Assert.NotSame(previousTable.Components[0], currentTable.Components[0]);
        }
        else
        {
            Assert.Same(previousTable.Components[0], currentTable.Components[0]);
        }

        if (changedKind == "akcss")
        {
            Assert.NotSame(previousTable.AkcssModules[0], currentTable.AkcssModules[0]);
        }
        else
        {
            Assert.Same(previousTable.AkcssModules[0], currentTable.AkcssModules[0]);
        }

        // The compatibility overload also passes the same infrastructure-reuse hint.
        var catalog = AkburaGenerationCatalogBuilder.Create(
            currentCSharp, currentTrees, "Demo", directory, previous.Compilation);
        Assert.Same(previousTable.Components[1], catalog.Compilation.DeclarationTable.Components[1]);
    }

    [Fact]
    public void CreateAndResolve_ObserveCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var tree = ComponentSyntaxTree.ParseText(SourceText.From("using Avalonia.Controls;\r\n<Border />\r\n"), "View.akbura");
        ImmutableArray<AkburaSyntaxTree> trees = [tree];
        var compilation = CreateCompilation();

        Assert.Throws<OperationCanceledException>(() => AkburaProjectIndex.Create(
            compilation, trees, "Demo", string.Empty, cancellationToken: cancellation.Token));

        var index = AkburaProjectIndex.Create(compilation, trees, "Demo", string.Empty);
        Assert.Throws<OperationCanceledException>(() => index.TryResolveComponent(
            Assert.Single(index.ComponentDescriptors), out _, cancellation.Token));
    }

#if STATS
    [Fact]
    public void Create_DoesNotConstructSemanticModelsUntilResolvingADocument()
    {
        ImmutableArray<AkburaSyntaxTree> trees =
        [
            ComponentSyntaxTree.ParseText(SourceText.From("using Avalonia.Controls;\r\n<Border />\r\n"), "First.akbura"),
            ComponentSyntaxTree.ParseText(SourceText.From("using Avalonia.Controls;\r\n<Border />\r\n"), "Second.akbura"),
        ];
        using var measurement = GenerationStatistics.BeginMeasurement();
        var index = AkburaProjectIndex.Create(CreateCompilation(), trees, "Demo", string.Empty);

        Assert.Equal(0, measurement.GetSnapshot().SemanticModelCreatedCount);
        Assert.True(index.TryResolveComponent(index.ComponentDescriptors[0], out _));
        Assert.Equal(1, measurement.GetSnapshot().SemanticModelCreatedCount);
        Assert.Equal(0, measurement.GetSnapshot().GeneratedSourceTextCreatedCount);
    }
#endif

    private static AkcssSyntaxTree ParseAkcss(string directory, string name, string source)
    {
        return AkcssSyntaxTree.ParseText(
            SourceText.From(source),
            Path.Combine(directory, "Styles", name + ".akcss"),
            "Demo.Styles." + name + ".akcss");
    }

    private static CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create(
            "AkburaProjectIndexTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }
}
