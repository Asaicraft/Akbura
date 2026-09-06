using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class CSharpEnvironmentSnapshotTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void IncrementalDriver_UnrelatedCSharpEditsReuseCompletedSourcesWithoutRunningWriters()
    {
        var documents = CreateDocuments();
        var original = CreateCompilation();
        var unrelated = Parse("namespace Demo; internal sealed class Unrelated { }", "Unrelated.cs");
        var extended = original.AddSyntaxTrees(unrelated);
        var commented = extended.ReplaceSyntaxTree(
            unrelated,
            Parse("namespace Demo; internal sealed class Unrelated { /* comment */ }", "Unrelated.cs"));
        var initial = RunAndValidate(original, CreateDriver(documents));
        var previous = initial;

        foreach (var compilation in new[] { extended, commented, original })
        {
#if STATS
            using var measurement = BlackSilenceGenerationStatistics.BeginMeasurement();
#endif
            var updated = RunAndValidate(compilation, previous.Driver);

#if STATS
            var statistics = measurement.GetSnapshot();
            Assert.Equal(0, statistics.CompilationCreatedCount);
            Assert.Equal(0, statistics.ComponentGeneratedCount);
            Assert.Equal(0, statistics.AkcssGeneratedCount);
            Assert.Equal(0, statistics.GeneratedSourceTextCreatedCount);
#endif
            Assert.Same(initial.Request.State, updated.Request.State);
            Assert.Same(initial.Snapshot, updated.Snapshot);

            foreach (var pair in initial.Snapshot.Entries)
            {
                var entry = updated.Snapshot.Entries[pair.Key];
                Assert.Same(pair.Value, entry);
                Assert.Same(pair.Value.Source.SourceText, entry.Source.SourceText);
            }

            AssertSameSources(GenerateAndValidate(compilation, documents), updated.Sources);
            previous = updated;
        }
    }

    [Fact]
    public void IncrementalDriver_PartialControlPropertyTypeChangeRegeneratesAndMatchesFreshOutput()
    {
        var originalType = Parse(
            "namespace Demo; public partial class CustomControl : Avalonia.Controls.Control { public double Value { get; set; } }",
            "CustomControl.cs");
        var original = CreateCompilation().AddSyntaxTrees(originalType);
        var updatedCompilation = original.ReplaceSyntaxTree(originalType, Parse(
            "namespace Demo; public partial class CustomControl : Avalonia.Controls.Control { public string Value { get; set; } = string.Empty; }",
            "CustomControl.cs"));
        var documents = CreateDocuments("using Demo;\r\n<CustomControl Value=\"10\" />");
        var initial = RunAndValidate(original, CreateDriver(documents));

#if STATS
        using var measurement = BlackSilenceGenerationStatistics.BeginMeasurement();
#endif
        var updated = RunAndValidate(updatedCompilation, initial.Driver);

#if STATS
        var statistics = measurement.GetSnapshot();
        Assert.Equal(1, statistics.CompilationCreatedCount);
        Assert.Equal(1, statistics.ComponentGeneratedCount);
        Assert.Equal(0, statistics.AkcssGeneratedCount);
        Assert.Equal(1, statistics.GeneratedSourceTextCreatedCount);
#endif
        Assert.NotSame(initial.Request.State, updated.Request.State);
        Assert.NotSame(initial.Snapshot, updated.Snapshot);
        var originalEntry = Assert.Single(initial.Snapshot.Entries).Value;
        var updatedEntry = Assert.Single(updated.Snapshot.Entries).Value;
        Assert.NotSame(originalEntry, updatedEntry);
        Assert.NotSame(originalEntry.Source.SourceText, updatedEntry.Source.SourceText);
        Assert.False(originalEntry.Source.SourceText.ContentEquals(updatedEntry.Source.SourceText));
        AssertSameSources(GenerateAndValidate(updatedCompilation, documents), updated.Sources);
    }

    [Fact]
    public void UnrelatedEmptyClass_AddRemoveAndCommentEditPreserveFreshGeneratedOutput()
    {
        var documents = CreateDocuments();
        var original = CreateCompilation();
        var unrelated = Parse("namespace Demo; internal sealed class Unrelated { }", "Unrelated.cs");
        var extended = original.AddSyntaxTrees(unrelated);
        var commented = extended.ReplaceSyntaxTree(
            unrelated,
            Parse("namespace Demo; internal sealed class Unrelated { /* unrelated comment */ }", "Unrelated.cs"));

        AssertEquivalent(
            CSharpEnvironmentSnapshot.Create(original, documents),
            CSharpEnvironmentSnapshot.Create(extended, documents));
        AssertEquivalent(
            CSharpEnvironmentSnapshot.Create(extended, documents),
            CSharpEnvironmentSnapshot.Create(commented, documents));
        AssertEquivalent(
            CSharpEnvironmentSnapshot.Create(extended, documents),
            CSharpEnvironmentSnapshot.Create(extended.RemoveSyntaxTrees(unrelated), documents));

        var originalOutput = GenerateAndValidate(original, documents);
        var extendedOutput = GenerateAndValidate(extended, documents);
        var commentedOutput = GenerateAndValidate(commented, documents);

        Assert.Equal(originalOutput.Keys, extendedOutput.Keys);
        Assert.Equal(originalOutput.Keys, commentedOutput.Keys);

        foreach (var pair in originalOutput)
        {
            Assert.True(pair.Value.ContentEquals(extendedOutput[pair.Key]));
            Assert.True(pair.Value.ContentEquals(commentedOutput[pair.Key]));
        }
    }

    [Fact]
    public void NewlyReferencedAdditionalTextNameRestoresPreviouslyIgnoredTree()
    {
        var compilation = CreateCompilation().AddSyntaxTrees(
            Parse("namespace Demo; internal sealed class Unrelated { }", "Unrelated.cs"));
        var unrelatedDocuments = CreateDocuments();
        var referencingDocuments = CreateDocuments(
            "using Avalonia.Controls;\r\nusing Demo;\r\n" +
            "state object value = new Unrelated();\r\n<ContentControl Content={value} />");

        var original = CSharpEnvironmentSnapshot.Create(compilation, unrelatedDocuments);
        var referenced = CSharpEnvironmentSnapshot.Create(compilation, referencingDocuments);

        AssertNotEquivalent(original, referenced);
        var output = GenerateAndValidate(compilation, referencingDocuments);
        Assert.Contains(output.Values, static source => source.ToString().Contains(
            "new Unrelated()",
            StringComparison.Ordinal));
    }

    [Fact]
    public void ImplicitComponentFileNameCannotBeIgnoredAsAnUnusedType()
    {
        var compilation = CreateCompilation();
        var documents = CreateDocuments(filePath: "Unrelated.akbura");
        var extended = compilation.AddSyntaxTrees(
            Parse("internal sealed class Unrelated { }", "Unrelated.cs"));

        AssertNotEquivalent(
            CSharpEnvironmentSnapshot.Create(compilation, documents),
            CSharpEnvironmentSnapshot.Create(extended, documents));
    }

    [Theory]
    [InlineData("Future", "PlannerView.akbura", "internal sealed class Future { }")]
    [InlineData("Demo", "Views/PlannerView.akbura", "namespace Demo; internal sealed class Views { }")]
    [InlineData("Demo", "@Views/PlannerView.akbura", "namespace Demo; internal sealed class Views { }")]
    public void GeneratedNamespaceSegmentsCannotBeIgnoredAsUnusedTypes(string rootNamespace, string path, string source)
    {
        var original = CreateCompilation();
        var documents = CreateDocuments(filePath: path);
        var extended = original.AddSyntaxTrees(Parse(source, "NamespaceCollision.cs"));
        var options = new GeneratorProjectOptions(rootNamespace, string.Empty);
        var updated = CSharpEnvironmentSnapshot.Create(extended, documents, options);

        Assert.Equal(0, updated.IgnoredSyntaxTreeCount);
        AssertNotEquivalent(CSharpEnvironmentSnapshot.Create(original, documents, options), updated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalDriver_ProjectOptionsExposePreviouslyUnrelatedNamespaceCollision(bool changeDirectory)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NamespaceCollisionTests", Guid.NewGuid().ToString("N"));
        var viewsDirectory = Path.Combine(directory, "Views");
        var documents = CreateDocuments(filePath: Path.Combine(viewsDirectory, "PlannerView.akbura"));
        var original = CreateCompilation();
        var extended = original.AddSyntaxTrees(Parse(
            changeDirectory
                ? "namespace Demo; internal sealed class Views { }"
                : "internal sealed class Future { }",
            "NamespaceCollision.cs"));
        var initialOptions = new TestOptions(changeDirectory ? "Demo" : "Before", viewsDirectory);
        var changedOptions = new TestOptions(changeDirectory ? "Demo" : "Future", changeDirectory ? directory : viewsDirectory);
        var initial = RunAndValidate(original, CreateDriver(documents, initialOptions));
        var unrelated = RunAndValidate(extended, initial.Driver);

        Assert.Same(initial.Request.State, unrelated.Request.State);
        Assert.Same(original, unrelated.Request.State.CSharpCompilation);

        // The C# edit was safely ignored under the old options. The new namespace
        // must restore that actual compilation, including its namespace/type error.
        var updatedDriver = unrelated.Driver.WithUpdatedAnalyzerConfigOptions(changedOptions)
            .RunGeneratorsAndUpdateCompilation(extended, out var updatedCompilation, out var updatedDiagnostics);
        var freshDriver = CreateDriver(documents, changedOptions)
            .RunGeneratorsAndUpdateCompilation(extended, out var freshCompilation, out var freshDiagnostics);
        var updated = Assert.Single(updatedDriver.GetRunResult().Results);
        var fresh = Assert.Single(freshDriver.GetRunResult().Results);
        Assert.Null(updated.Exception);
        Assert.Null(fresh.Exception);
        var request = GetRequest(updated);
        Assert.NotSame(initial.Request.State, request.State);
        Assert.Same(extended, request.State.CSharpCompilation);
        Assert.Contains(updatedCompilation.GetDiagnostics(), static diagnostic => diagnostic.Id == "CS0101");
        AssertSameSources(
            fresh.GeneratedSources.ToDictionary(static source => source.HintName, static source => source.SourceText),
            updated.GeneratedSources.ToDictionary(static source => source.HintName, static source => source.SourceText));
        Assert.Equal(
            GetDiagnosticDescriptions(freshDiagnostics.Concat(fresh.Diagnostics).Concat(freshCompilation.GetDiagnostics())),
            GetDiagnosticDescriptions(updatedDiagnostics.Concat(updated.Diagnostics).Concat(updatedCompilation.GetDiagnostics())));
    }

    [Theory]
    [InlineData("namespace Demo; public class Unrelated { }")]
    [InlineData("namespace Demo; internal partial class Unrelated { }")]
    [InlineData("namespace Demo; internal class Unrelated<T> { }")]
    [InlineData("namespace Demo; internal class Unrelated(int value) { }")]
    [InlineData("namespace Demo; [System.Obsolete] internal class Unrelated { }")]
    [InlineData("namespace Demo; internal class Unrelated : Avalonia.Controls.Control { }")]
    [InlineData("namespace Demo; internal class Unrelated { public double Value { get; set; } }")]
    [InlineData("namespace Demo; internal class Unrelated { public event System.Action? Changed; }")]
    [InlineData("namespace Demo; internal class Unrelated { public Unrelated() { } }")]
    [InlineData("namespace Demo; internal class Unrelated : System.ComponentModel.TypeConverter { }")]
    [InlineData("namespace Demo; internal static class Unrelated { public static void Extend(this object value) { } }")]
    [InlineData("namespace Demo; internal static class Unrelated { [Akbura.CompilerAnotations.UseHook] public static void useEffect() { } }")]
    [InlineData("global using System; namespace Demo; internal class Unrelated { }")]
    [InlineData("extern alias external; namespace Demo; internal class Unrelated { }")]
    [InlineData("namespace Demo { using System; internal class Unrelated { } }")]
    [InlineData("[assembly: System.CLSCompliant(true)] namespace Demo; internal class Unrelated { }")]
    [InlineData("#nullable enable\r\nnamespace Demo; internal class Unrelated { }")]
    [InlineData("namespace Demo; internal class Unrelated {")]
    [InlineData("namespace Demo; internal class __AkburaAkcssModule_12345678 { }")]
    [InlineData("namespace Demo; internal class Generated { }")]
    public void PotentialSemanticChangesAreNotNarrowed(string source)
    {
        var original = CreateCompilation();
        var documents = CreateDocuments();
        var updated = original.AddSyntaxTrees(Parse(source, "Changed.cs"));

        AssertNotEquivalent(
            CSharpEnvironmentSnapshot.Create(original, documents),
            CSharpEnvironmentSnapshot.Create(updated, documents));
    }

    [Fact]
    public void NewNamespaceIsRetainedEvenWhenItsEmptyTypeIsUnused()
    {
        var original = CreateCompilation();
        var documents = CreateDocuments();
        var updated = original.AddSyntaxTrees(
            Parse("namespace NewlyAvailable.Library; internal class Unrelated { }", "Unrelated.cs"));

        AssertNotEquivalent(
            CSharpEnvironmentSnapshot.Create(original, documents),
            CSharpEnvironmentSnapshot.Create(updated, documents));
    }

    [Fact]
    public void EmptyTypeCannotShadowReferencedRuntimeTypeOrNamespace()
    {
        var original = CreateCompilation().AddSyntaxTrees(
            Parse("namespace Avalonia.Controls { public sealed class LocalMarker { } }", "Namespace.cs"));
        var documents = CreateDocuments();

        foreach (var source in new[]
        {
            "namespace Avalonia.Controls; internal sealed class Control { }",
            "internal sealed class System { }",
        })
        {
            var updated = original.AddSyntaxTrees(Parse(source, "Shadow.cs"));

            AssertNotEquivalent(
                CSharpEnvironmentSnapshot.Create(original, documents),
                CSharpEnvironmentSnapshot.Create(updated, documents));
        }
    }

    [Fact]
    public void RetainedCSharpReferencesProtectAnOtherwiseEmptyClass()
    {
        var original = CreateCompilation().AddSyntaxTrees(Parse(
            "namespace Demo; public static class Consumer { public static string Read() => nameof(Unrelated); }",
            "Consumer.cs"));
        var updated = original.AddSyntaxTrees(Parse(
            "namespace Demo; internal sealed class Unrelated { }",
            "Unrelated.cs"));
        var documents = CreateDocuments();

        AssertNotEquivalent(
            CSharpEnvironmentSnapshot.Create(original, documents),
            CSharpEnvironmentSnapshot.Create(updated, documents));
    }

    [Fact]
    public void RetainedMethodBodyChangeIsConservativelyInvalidated()
    {
        var original = CreateCompilation();
        var originalTree = Assert.Single(original.SyntaxTrees);
        var updated = original.ReplaceSyntaxTree(originalTree, Parse(
            "namespace Demo; public static class Marker { public static int Value() => 2; }",
            originalTree.FilePath));
        var documents = CreateDocuments();

        AssertNotEquivalent(
            CSharpEnvironmentSnapshot.Create(original, documents),
            CSharpEnvironmentSnapshot.Create(updated, documents));
    }

    [Fact]
    public void ReferencesOptionsAssemblyIdentityAndParseOptionsRemainExactInputs()
    {
        var original = CreateCompilation();
        var documents = CreateDocuments();
        var baseline = CSharpEnvironmentSnapshot.Create(original, documents);
        var alternatives = new[]
        {
            original.WithAssemblyName("Renamed"),
            original.WithOptions(original.Options.WithNullableContextOptions(NullableContextOptions.Disable)),
            original.WithReferences(original.References.Reverse()),
            original.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "namespace Demo; internal class Unrelated { }",
                s_parseOptions.WithPreprocessorSymbols("DIFFERENT_ENVIRONMENT"),
                "Unrelated.cs")),
        };

        foreach (var alternative in alternatives)
        {
            AssertNotEquivalent(baseline, CSharpEnvironmentSnapshot.Create(alternative, documents));
        }

        var sourceTree = Assert.Single(original.SyntaxTrees);
        var reparsed = original.ReplaceSyntaxTree(sourceTree, Parse(sourceTree.GetText().ToString(), sourceTree.FilePath));
        AssertEquivalent(baseline, CSharpEnvironmentSnapshot.Create(reparsed, documents));
    }

    [Fact]
    public void IncompleteAdditionalTextDisablesNarrowingAndCancellationPublishesNothing()
    {
        var compilation = CreateCompilation();
        var incomplete = CreateDocuments("state object value = ");
        var extended = compilation.AddSyntaxTrees(Parse(
            "namespace Demo; internal class Unrelated { }",
            "Unrelated.cs"));

        Assert.False(Assert.Single(incomplete).CanReuse);
        AssertNotEquivalent(
            CSharpEnvironmentSnapshot.Create(compilation, incomplete),
            CSharpEnvironmentSnapshot.Create(extended, incomplete));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            CSharpEnvironmentSnapshot.Create(compilation, CreateDocuments(), cancellation.Token));
    }

    private static CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create(
            "CSharpEnvironmentSnapshotTests",
            syntaxTrees: [Parse("namespace Demo; public static class Marker { public static int Value() => 1; }", "Marker.cs")],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static SyntaxTree Parse(string source, string path)
    {
        return CSharpSyntaxTree.ParseText(source, s_parseOptions, path);
    }

    private static ImmutableArray<DocumentSyntaxVersion> CreateDocuments(
        string source = "using Avalonia.Controls;\r\n<Border />",
        string filePath = "PlannerView.akbura")
    {
        return [DocumentSyntaxVersion.Create(ComponentSyntaxTree.ParseText(source, filePath))];
    }

    private static Dictionary<string, SourceText> GenerateAndValidate(
        CSharpCompilation compilation,
        ImmutableArray<DocumentSyntaxVersion> documents)
    {
        return RunAndValidate(compilation, CreateDriver(documents)).Sources;
    }

    private static GeneratorDriver CreateDriver(
        ImmutableArray<DocumentSyntaxVersion> documents,
        AnalyzerConfigOptionsProvider? options = null)
    {
        return CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: documents.Select(static document =>
                (AdditionalText)new TestAdditionalText(document.FilePath, document.SyntaxTree.Text)),
            parseOptions: s_parseOptions,
            optionsProvider: options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    private static GeneratorRun RunAndValidate(CSharpCompilation compilation, GeneratorDriver driver)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.NotEmpty(result.GeneratedSources);

        var problems = diagnostics.Concat(result.Diagnostics).Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            problems.Length == 0,
            string.Join(Environment.NewLine, problems.Select(static diagnostic => diagnostic.ToString())));

        var request = GetRequest(result);
        var snapshot = Assert.IsType<BlackSilenceProjectSnapshot>(request.State.TryGetSnapshot(request.Options));

        return new GeneratorRun(
            driver,
            request,
            snapshot,
            result.GeneratedSources.ToDictionary(static source => source.HintName, static source => source.SourceText));
    }

    private static BlackSilenceGenerationRequest GetRequest(GeneratorRunResult result)
    {
        var steps = result.TrackedSteps["BlackSilence.GenerationRequests"];
        return Assert.IsType<BlackSilenceGenerationRequest>(Assert.Single(steps.SelectMany(static step => step.Outputs)).Value);
    }

    private static string[] GetDiagnosticDescriptions(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Select(static diagnostic => diagnostic.ToString()).Order(StringComparer.Ordinal).ToArray();
    }

    private static void AssertSameSources(Dictionary<string, SourceText> expected, Dictionary<string, SourceText> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));

        foreach (var pair in expected)
        {
            Assert.True(pair.Value.ContentEquals(actual[pair.Key]), pair.Key);
        }
    }

    private static void AssertEquivalent(CSharpEnvironmentSnapshot left, CSharpEnvironmentSnapshot right)
    {
        var comparer = CSharpEnvironmentSnapshotComparer.Instance;
        Assert.True(comparer.Equals(left, right));
        Assert.True(comparer.Equals(right, left));
        Assert.Equal(comparer.GetHashCode(left), comparer.GetHashCode(right));
    }

    private static void AssertNotEquivalent(CSharpEnvironmentSnapshot left, CSharpEnvironmentSnapshot right)
    {
        Assert.False(CSharpEnvironmentSnapshotComparer.Instance.Equals(left, right));
        Assert.False(CSharpEnvironmentSnapshotComparer.Instance.Equals(right, left));
    }

    private sealed record GeneratorRun(
        GeneratorDriver Driver,
        BlackSilenceGenerationRequest Request,
        BlackSilenceProjectSnapshot Snapshot,
        Dictionary<string, SourceText> Sources);

    private sealed class TestOptions(string rootNamespace, string projectDirectory) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_empty = new Values(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = rootNamespace,
            ["build_property.ProjectDir"] = projectDirectory,
        });

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_empty;
    }

    private sealed class Values(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            return values.TryGetValue(key, out value!);
        }
    }

    private sealed class TestAdditionalText(string path, SourceText text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => text;
    }
}
