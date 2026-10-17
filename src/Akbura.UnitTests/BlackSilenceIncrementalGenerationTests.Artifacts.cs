using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    private const string ArtifactLeaf = "internal static class IsolatedLeaf { public static int Read() => 1; }";

    public static IEnumerable<object[]> ArtifactAdversaries()
    {
        const string call = "<TextBlock Text={global::Support.Read().ToString()} />";
        const string simple = "public static class Support { public static int Read() => 1; }";
        yield return ["overload", "public static class Support { public static int Pick(object value) => 1; }",
            "public static class Support { public static int Pick(object value) => 1; public static int Pick(string value) => 2; }",
            "<TextBlock Text={global::Support.Pick(\"x\").ToString()} />"];
        yield return ["extension", "namespace Lib; public static class Extensions { public static int Score(this object value) => 1; }",
            "namespace Lib; public static class Extensions { public static int Score(this object value) => 1; public static int Score(this string value) => 2; }",
            "using Lib; <TextBlock Text={\"x\".Score().ToString()} />"];
        yield return ["missing symbol", "public static class Support { }", simple, call];
        yield return ["alias", "using Alias = N.Left; namespace N { public class Left {} public class Right {} } public static class Support { public static Alias Read() => new(); }",
            "using Alias = N.Right; namespace N { public class Left {} public class Right {} } public static class Support { public static Alias Read() => new(); }", call];
        yield return ["constant", "public static class Support { public const int Number = 1; }",
            "public static class Support { public const int Number = 2; }", "<Border Width={global::Support.Number} />"];
        yield return ["base type", "namespace Demo; public class Target : Avalonia.Controls.Border {}",
            "namespace Demo; public class Target : Avalonia.Controls.TextBlock {}", "using Demo; <Target Width=\"10\" />"];
        yield return ["nullable", "#nullable enable\npublic static class Support { public static string? Read() => null; }",
            "#nullable enable\npublic static class Support { public static string Read() => \"x\"; }", "<TextBlock Text={global::Support.Read().Length.ToString()} />"];
        yield return ["constraint", "public static class Support { public static int Factory<T>() where T : class => 1; }",
            "public static class Support { public static int Factory<T>() where T : struct => 1; }", "<TextBlock Text={global::Support.Factory<string>().ToString()} />"];
        yield return ["attribute", simple, simple.Replace("public static int", "[System.Obsolete(\"changed\")] public static int"), call];
        yield return ["signature", simple, simple.Replace("int Read() => 1", "string Read() => \"x\""), call];
        yield return ["invalid syntax", simple, "public static class Support { public static int Read( => ??? }", call];
        yield return ["global using", "global using System.Text; " + simple, "global using System.Threading.Tasks; " + simple, call];
        yield return ["hook", "namespace Demo; public static class Hooks { [Akbura.CompilerAnotations.UseHook] public static Akbura.ComponentTree.State<int> useValue(object value) => null!; }",
            "namespace Demo; public static class Hooks { [Akbura.CompilerAnotations.UseHook] public static Akbura.ComponentTree.State<int> useValue(int value) => null!; }",
            "using Demo; state int value = useValue(1); <Border Width={value} />"];
        yield return ["isolated type becomes referenced", ArtifactLeaf, ArtifactLeaf.Replace("=> 1", "=> 2"),
            "<TextBlock Text={global::IsolatedLeaf.Read().ToString()} />"];
        yield return ["escaped type reference", ArtifactLeaf, ArtifactLeaf.Replace("=> 1", "=> 2"),
            "<TextBlock Text={global::IsolatedL\\u0065af.Read().ToString()} />"];
    }

    [Theory]
    [MemberData(nameof(ArtifactAdversaries))]
    public void DetachedArtifacts_AdversarialChangesAndReversionsRebindAndMatchFresh(string name, string before, string after, string markup)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component(markup));
        var tree = CSharpSyntaxTree.ParseText(before, project.Options.ParseOptions, "Support.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        foreach (var source in new[] { after, before })
        {
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            current = ArtifactRun(project, current.Driver, compilation);
            Assert.All(current.Request.Components, component => Assert.Null(component.Previous));
            Assert.All(current.Request.Diagnostics, diagnostic => Assert.Null(diagnostic.Previous));
            AssertArtifactsEqualToFresh(project, current, compilation, file);
        }
        GC.KeepAlive(name);
    }

    [Fact]
    public void DetachedArtifacts_OneReferencingDocumentFallsBackWhileAnotherReuses()
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Uses.akbura", Component("<TextBlock Text={global::IsolatedLeaf.Read().ToString()} />")),
            project.File("Other.akbura", Component("<Border />")) };
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> 2"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Uses.akbura")).Previous);
        Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedArtifacts_GlobalImportsFromAnotherMarkupDocumentAreInEveryLookupUniverse(bool unknownGlobalDependency)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[]
        {
            project.File("Imports.akbura", Component(unknownGlobalDependency
                ? "global using System; <Border Width={1 + } />"
                : "global using static global::IsolatedLeaf; <Border />")),
            project.File("Page.akbura", Component(unknownGlobalDependency
                ? "<TextBlock Text={(1 + 2).ToString()} />"
                : "<TextBlock Text={Read().ToString()} />")),
        };
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
            ArtifactLeaf.Replace("=> 1", "=> 2"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.All(current.Request.Components, component => Assert.Null(component.Previous));
        if (unknownGlobalDependency)
        {
            Assert.All(current.Request.DetachedDocuments, document => Assert.False(document.Supported));
        }
        else
        {
            Assert.All(current.Request.DetachedDocuments, document => Assert.Contains("IsolatedLeaf", document.LookupNames));
        }
        AssertArtifactsEqualToFresh(project, current, compilation, files);
    }

    [Fact]
    public void DetachedArtifacts_MarkupEditReusesOnlyCurrentDetachedFactsAndUnchangedCharacters()
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Page.akbura", Component("<Border Width=\"10\" />")),
            project.File("Other.akbura", Component("<TextBlock Text=\"hello\" />")) };
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), project.Compilation);
        var previous = initial.Snapshot.DetachedArtifacts!;
        var changed = project.File("Page.akbura", Component("<Border Width=\"20\" />"));
        using var measurement = GenerationStatistics.BeginMeasurement();
        var current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(files[0], changed), project.Compilation);
        var artifacts = current.Snapshot.DetachedArtifacts!;
        Assert.Same(artifacts, current.Request.State.Artifacts.Latest);
        Assert.NotSame(previous, artifacts);
        Assert.NotSame(previous.Entries, artifacts.Entries);
        var identity = current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Identity;
        Assert.Same(previous.Entries[identity], artifacts.Entries[identity]);
        Assert.Same(previous.Documents.Single(document => document.Path.EndsWith("Other.akbura")),
            artifacts.Documents.Single(document => document.Path.EndsWith("Other.akbura")));
        Assert.NotSame(previous.Documents.Single(document => document.Path.EndsWith("Page.akbura")),
            artifacts.Documents.Single(document => document.Path.EndsWith("Page.akbura")));
#if STATS
        Assert.Equal(1, measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.DetachedSourceTextCreated));
#endif
        AssertArtifactsEqualToFresh(project, current, project.Compilation, changed, files[1]);
    }

    [Fact]
    public void DetachedArtifacts_ChangedGlobalMarkupInvalidatesAllMemoizedLookupFacts()
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Imports.akbura", Component("global using System; <Border />")),
            project.File("Page.akbura", Component("<Border />")) };
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var changed = project.File("Imports.akbura", Component("global using static global::IsolatedLeaf; <Border />"));
        var current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(files[0], changed), baseline);
        Assert.All(current.Request.DetachedDocuments, document => Assert.Contains("IsolatedLeaf", document.LookupNames));
        Assert.NotSame(initial.Request.DetachedDocuments.Single(document => document.Path.EndsWith("Page.akbura")),
            current.Request.DetachedDocuments.Single(document => document.Path.EndsWith("Page.akbura")));
        AssertArtifactsEqualToFresh(project, current, baseline, changed, files[1]);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
            ArtifactLeaf.Replace("=> 1", "=> 2"), project.Options.ParseOptions, tree.FilePath));
        current = ArtifactRun(project, current.Driver, compilation);
        Assert.All(current.Request.Components, component => Assert.Null(component.Previous));
        AssertArtifactsEqualToFresh(project, current, compilation, changed, files[1]);
    }

    [Fact]
    public void DetachedArtifacts_DiagnosticsCanRequireBindingWhileGeneratedArtifactReuses()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border Width={1 + 2} />"));
        var off = new TestOptions("Demo", project.Directory, diagnosticMode: "Off");
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, off.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(off, file), baseline);
        Assert.False(initial.Request.State.Artifacts.Latest!.DiagnosticsComputed);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> 2"), off.ParseOptions, tree.FilePath));
        using var measurement = GenerationStatistics.BeginMeasurement();
        var current = ArtifactRun(project, initial.Driver.WithUpdatedAnalyzerConfigOptions(project.Options), compilation);
        Assert.NotNull(current.Request.Components.Single().Previous);
        Assert.Null(current.Request.Diagnostics.Single().Previous);
#if STATS
        Assert.Equal(0, measurement.GetSnapshot().ComponentGeneratedCount);
        Assert.Equal(1, measurement.GetSnapshot().DiagnosticDocumentEvaluatedCount);
        Assert.True(measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.ProbeCompilationCreated) > 0);
#endif
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Theory]
    [InlineData("options")]
    [InlineData("reference")]
    [InlineData("add/remove")]
    [InlineData("custom resolver")]
    [InlineData("mapping")]
    [InlineData("akcss")]
    public void DetachedArtifacts_ProjectInputChangesUseConservativeFallback(string change)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Page.akbura", Component("using Demo.Styles.Base.akcss; <Border class=\"basic\" />")),
            project.File("Styles/Base.akcss", "@using Avalonia.Controls; Border.basic { Width: 10; }") };
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var compilation = change switch
        {
            "options" => baseline.WithOptions(baseline.Options.WithAllowUnsafe(true)),
            "reference" => baseline.ReplaceReference(baseline.References.First(), MetadataReference.CreateFromFile(baseline.References.First().Display!)),
            "add/remove" => baseline.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Added {}", project.Options.ParseOptions, "Added.cs")),
            "custom resolver" => baseline.WithOptions(baseline.Options.WithSourceReferenceResolver(new SourceFileResolver([], null))),
            _ => baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> 2"), project.Options.ParseOptions, tree.FilePath)),
        };
        var driver = current.Driver;
        if (change is "mapping" or "akcss")
        {
            var index = change == "mapping" ? 0 : 1;
            var old = files[index];
            files[index] = project.File(change == "mapping" ? "Page.akbura" : "Styles/Base.akcss",
                change == "mapping" ? "\r\n" + old.Text : old.Text.ToString().Replace("10", "20"));
            driver = driver.ReplaceAdditionalText(old, files[index]);
        }
        current = ArtifactRun(project, driver, compilation);
        Assert.All(current.Request.Components, component => Assert.Null(component.Previous));
        Assert.All(current.Request.ExternalAkcss, module => Assert.Null(module.Previous));
        AssertArtifactsEqualToFresh(project, current, compilation, files);
        if (change == "add/remove")
        {
            var removed = ArtifactRun(project, current.Driver, baseline);
            Assert.All(removed.Request.Components, component => Assert.Null(component.Previous));
            AssertArtifactsEqualToFresh(project, removed, baseline, files);
        }
    }

    [Fact]
    public void DetachedArtifacts_DisabledModeDoesNotCaptureOrReuseCrossCompilationArtifacts()
    {
        var project = new TestProject(publishDiagnostics: true);
        var options = new TestOptions("Demo", project.Directory, publishDiagnostics: true, crossCompilationReuse: false);
        var file = project.File("Page.akbura", Component("<Border />"));
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(options, file), baseline);
        Assert.Null(initial.Request.State.Artifacts.Latest);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> 2"), options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.ArtifactInputs);
        Assert.Null(current.Request.Components.Single().Previous);
        Assert.Null(current.Request.State.Artifacts.Latest);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("comment")]
    [InlineData("whitespace")]
    public void DetachedArtifacts_IsolatedLeafSkipsAllSemanticWorkAndMatchesFresh(string change)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Page.akbura", Component("<Border Width=\"10\" />")),
            project.File("Other.akbura", Component("<TextBlock Text=\"hello\" />")) };
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var compilation = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), compilation);
        var source = change == "body" ? ArtifactLeaf.Replace("=> 1", "=> 2") : ArtifactLeaf + (change == "comment" ? " // changed" : "   ");
        var currentCompilation = compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var current = ArtifactRun(project, initial.Driver, currentCompilation);
        Assert.True(current.Request.ArtifactInputs!.Supported, "The standard compiler inputs should be supported.");
        Assert.Equal(ArtifactReuseDecision.ValidatedEquivalent, current.Request.ArtifactValidation.Decision);
        Assert.Same(initial.Request.State.Artifacts, current.Request.State.Artifacts);
        Assert.NotSame(initial.Request.State, current.Request.State);
        Assert.All(current.Request.Components, component => Assert.NotNull(component.Previous));
        Assert.All(current.Request.Diagnostics, diagnostic => Assert.NotNull(diagnostic.Previous));
        Assert.Empty(current.Snapshot.SemanticStates);
        Assert.All(current.Request.Documents, document => Assert.False(current.Request.Index.Compilation.HasCreatedSemanticModel(document.SyntaxTree)));
#if STATS
        var statistics = measurement.GetSnapshot();
        Assert.Equal(2, statistics.GetCounterCount(GenerationStatisticCounter.CrossCompilationArtifactHit));
        Assert.Equal(0, statistics.GetStageInvocationCount(GenerationStatisticStage.SemanticBinding));
        Assert.Equal(0, statistics.SemanticModelCreatedCount);
        Assert.Equal(0, statistics.DiagnosticDocumentEvaluatedCount);
        Assert.Equal(0, statistics.GetCounterCount(GenerationStatisticCounter.ProbeCompilationCreated));
        Assert.Equal(0, statistics.ComponentGeneratedCount);
        Assert.Equal(0, statistics.GetCounterCount(GenerationStatisticCounter.DetachedSourceTextCreated));
#endif
        AssertArtifactsEqualToFresh(project, current, currentCompilation, files);
    }

    private static TestRun ArtifactRun(TestProject project, GeneratorDriver driver, CSharpCompilation compilation)
    {
        var result = RunProbeAllowingDiagnostics(project, driver, compilation);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AKBURA_GENERATOR_FAILURE");
        var request = ArtifactRequest(result.Driver);
        return new TestRun(result.Driver, result.Output, request, request.State.TryGetSnapshot(request.Options)!);
    }

    private static BlackSilenceGenerationRequest ArtifactRequest(GeneratorDriver driver) =>
        Assert.IsType<BlackSilenceGenerationRequest>(driver.GetRunResult().Results.Single().TrackedSteps[RequestsTrackingName]
            .SelectMany(static step => step.Outputs).Single().Value);

    private static void AssertArtifactsEqualToFresh(TestProject project, TestRun current, CSharpCompilation compilation, params TestFile[] files)
    {
        using var isolatedMeasurement = GenerationStatistics.BeginMeasurement();
        var fresh = ArtifactRun(project, CreateDriver(project.Options, files), compilation);
        var expected = fresh.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(static item => item.HintName).ToArray();
        var actual = current.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(static item => item.HintName).ToArray();
        Assert.Equal(expected.Select(static item => (item.HintName, item.SourceText.ToString())),
            actual.Select(static item => (item.HintName, item.SourceText.ToString())));
        Assert.Equal(ArtifactDiagnosticKeys(fresh.Output.GetDiagnostics()), ArtifactDiagnosticKeys(current.Output.GetDiagnostics()));
        Assert.Equal(ArtifactDiagnosticKeys(fresh.Driver.GetRunResult().Diagnostics), ArtifactDiagnosticKeys(current.Driver.GetRunResult().Diagnostics));
        Assert.All(current.Driver.GetRunResult().Diagnostics.Where(static diagnostic => diagnostic.Properties.ContainsKey("akbura.document-version")), diagnostic =>
            Assert.InRange(long.Parse(diagnostic.Properties["akbura.document-version"]!, CultureInfo.InvariantCulture), 1, current.Request.Version));
    }

    private static string[] ArtifactDiagnosticKeys(IEnumerable<Diagnostic> diagnostics) => diagnostics.Select(Akbura.TestUtilities.DiagnosticParity.Key)
        .OrderBy(static key => key, StringComparer.Ordinal).ToArray();
}
