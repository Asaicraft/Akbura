using Akbura.Language;
using Akbura.Language.CodeGeneration;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.Benchmarks;

[Config(typeof(FeatureGalleryStageBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("SourceGenerator", "FeatureGallery", "Catalog")]
public class FeatureGalleryCatalogBenchmarks
{
    private FeatureGalleryStageInputs _inputs = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _inputs = FeatureGalleryStageInputs.Create();
    }

    [Benchmark]
    public object CreateCompilation()
    {
        return new AkburaCompilation(
            _inputs.Project.Compilation,
            _inputs.ComponentTrees,
            _inputs.AkcssTrees,
            _inputs.Project.RootNamespace,
            _inputs.Snapshot.ProjectDirectory);
    }

    [Benchmark]
    public int CreateCatalog()
    {
        var catalog = _inputs.CreateCatalog();

        return catalog.Components.Length +
            catalog.ExternalAkcssModules.Length +
            catalog.InlineAkcssModules.Length;
    }
}

[Config(typeof(FeatureGalleryStageBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("SourceGenerator", "FeatureGallery", "ComponentBackend")]
public class FeatureGalleryComponentBackendBenchmarks
{
    private AkburaGenerationCatalog _catalog = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var inputs = FeatureGalleryStageInputs.Create();
        _catalog = inputs.CreateCatalog();

        // Resolve lazy semantic inputs before timing the repeated planner + writer path.
        inputs.ValidateAndWarmBackend(_catalog);
    }

    [Benchmark]
    public long PlanAndWriteComponents()
    {
        long generatedLength = 0;

        foreach (var input in _catalog.Components)
        {
            generatedLength += ComponentDocumentWriter.Generate(
                input.Component,
                input.SemanticModel,
                input.SourcePath,
                _catalog.AkcssModuleTypeNames).Length;
        }

        return generatedLength;
    }
}

[Config(typeof(FeatureGalleryStageBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("SourceGenerator", "FeatureGallery", "AkcssBackend")]
public class FeatureGalleryAkcssBackendBenchmarks
{
    private AkburaGenerationCatalog _catalog = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var inputs = FeatureGalleryStageInputs.Create();
        _catalog = inputs.CreateCatalog();
        inputs.ValidateAndWarmBackend(_catalog);
    }

    [Benchmark]
    public long PlanAndWriteAkcss()
    {
        return Generate(_catalog.ExternalAkcssModules) + Generate(_catalog.InlineAkcssModules);
    }

    private long Generate(ImmutableArray<AkcssGenerationInput> inputs)
    {
        long generatedLength = 0;

        foreach (var input in inputs)
        {
            generatedLength += AkcssDocumentWriter.Generate(
                input,
                _catalog.AkcssSourceMap,
                _catalog.RootNamespace).Length;
        }

        return generatedLength;
    }
}

public sealed class FeatureGalleryStageBenchmarkConfig : ManualConfig
{
    public FeatureGalleryStageBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithId("FeatureGalleryStages")
            .WithLaunchCount(1)
            .WithWarmupCount(3)
            .WithIterationCount(8)
            .WithInvocationCount(1)
            .WithUnrollFactor(1));
    }
}

internal sealed class FeatureGalleryStageInputs
{
    private static readonly Lazy<FeatureGalleryBenchmarkProject> s_project =
        new(FeatureGalleryBenchmarkProject.Load);

    private FeatureGalleryStageInputs(
        FeatureGalleryBenchmarkProject project,
        FeatureGalleryBenchmarkSnapshot snapshot,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        Project = project;
        Snapshot = snapshot;
        SyntaxTrees = syntaxTrees;
        ComponentTrees = syntaxTrees.OfType<ComponentSyntaxTree>().Cast<AkburaSyntaxTree>().ToImmutableArray();
        AkcssTrees = syntaxTrees.OfType<AkcssSyntaxTree>().ToImmutableArray();
    }

    public FeatureGalleryBenchmarkProject Project { get; }

    public FeatureGalleryBenchmarkSnapshot Snapshot { get; }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees { get; }

    public ImmutableArray<AkburaSyntaxTree> ComponentTrees { get; }

    public ImmutableArray<AkcssSyntaxTree> AkcssTrees { get; }

    public static FeatureGalleryStageInputs Create()
    {
        var project = s_project.Value;
        var snapshot = project.CreateSnapshot();
        var syntaxTrees = ImmutableArray.CreateBuilder<AkburaSyntaxTree>(snapshot.AdditionalTexts.Length);

        foreach (var additionalText in snapshot.AdditionalTexts)
        {
            var sourceText = additionalText.GetText() ??
                throw new InvalidOperationException("A FeatureGallery AdditionalFile did not provide source text.");

            if (Path.GetExtension(additionalText.Path).Equals(".akcss", StringComparison.OrdinalIgnoreCase))
            {
                var sourcePath = Path.GetRelativePath(snapshot.ProjectDirectory, additionalText.Path)
                    .Replace('\\', '/');

                syntaxTrees.Add(AkcssSyntaxTree.ParseText(
                    sourceText,
                    additionalText.Path,
                    AkcssGeneratedModuleNames.GetMetadataName(project.RootNamespace, sourcePath)));
            }
            else
            {
                syntaxTrees.Add(ComponentSyntaxTree.ParseText(sourceText, additionalText.Path));
            }
        }

        return new FeatureGalleryStageInputs(project, snapshot, syntaxTrees.MoveToImmutable());
    }

    public AkburaGenerationCatalog CreateCatalog()
    {
        return AkburaGenerationCatalogBuilder.Create(
            Project.Compilation,
            SyntaxTrees,
            Project.RootNamespace,
            Snapshot.ProjectDirectory);
    }

    public void ValidateAndWarmBackend(AkburaGenerationCatalog catalog)
    {
        var generatedTrees = new List<SyntaxTree>();

        foreach (var input in catalog.Components)
        {
            var sourceText = ComponentDocumentWriter.Generate(
                input.Component,
                input.SemanticModel,
                input.SourcePath,
                catalog.AkcssModuleTypeNames);

            generatedTrees.Add(CSharpSyntaxTree.ParseText(
                sourceText,
                Project.ParseOptions,
                ComponentDocumentWriter.GetHintName(input.Component, input.SourcePath)));
        }

        foreach (var input in catalog.ExternalAkcssModules.Concat(catalog.InlineAkcssModules))
        {
            var sourceText = AkcssDocumentWriter.Generate(input, catalog.AkcssSourceMap, catalog.RootNamespace);

            generatedTrees.Add(CSharpSyntaxTree.ParseText(
                sourceText,
                Project.ParseOptions,
                AkcssDocumentWriter.GetHintName(input)));
        }

        if (generatedTrees.Count == 0)
        {
            throw new InvalidOperationException("The FeatureGallery backend did not produce any documents.");
        }

        FeatureGalleryStageMeasurements.ValidateCompilation(Project.Compilation.AddSyntaxTrees(generatedTrees));
    }
}
