using Akbura.Language;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Akbura.Furioso;

[Generator(LanguageNames.CSharp)]
public sealed class AkburaCsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxTrees = context.AdditionalTextsProvider
            .Where(static file => IsAkburaSourcePath(file.Path))
            .Select(static (file, cancellationToken) =>
                new AkburaAndAkcssFile(file.GetText(cancellationToken), file.Path))
            .Where(static file => file.SourceText != null)
            .Select(static (file, cancellationToken) => file.ToSyntaxTree(cancellationToken))
            .Collect();

        var projectOptions = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => GeneratorProjectOptions.Create(provider.GlobalOptions));

        var generationInput = syntaxTrees
            .Combine(context.CompilationProvider)
            .Combine(projectOptions);

        context.RegisterSourceOutput(
            generationInput,
            static (context, snapshot) => GenerateSources(
                context,
                snapshot.Left.Left,
                snapshot.Left.Right,
                snapshot.Right));
    }

    private static void GenerateSources(
        SourceProductionContext context,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        Compilation compilation,
        GeneratorProjectOptions projectOptions)
    {
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return;
        }

        var componentTrees = ImmutableArray.CreateBuilder<AkburaSyntaxTree>();
        var akcssTrees = ImmutableArray.CreateBuilder<AkcssSyntaxTree>();

        foreach (var syntaxTree in syntaxTrees)
        {
            switch (syntaxTree)
            {
                case ComponentSyntaxTree:
                    componentTrees.Add(syntaxTree);
                    break;
                case AkcssSyntaxTree akcssSyntaxTree:
                    akcssTrees.Add(akcssSyntaxTree);
                    break;
            }
        }

        var semanticHost = componentTrees.Count > 0
            ? componentTrees[0]
            : CreateSemanticHost(projectOptions.ProjectDirectory, context.CancellationToken);

        if (componentTrees.Count == 0)
        {
            componentTrees.Add(semanticHost);
        }

        var componentSyntaxTrees = componentTrees.ToImmutable();
        var akcssSyntaxTrees = akcssTrees.ToImmutable();
        var akburaCompilation = new AkburaCompilation(
            csharpCompilation,
            componentSyntaxTrees,
            akcssSyntaxTrees,
            projectOptions.RootNamespace,
            projectOptions.ProjectDirectory);
        var semanticModel = akburaCompilation.GetSemanticModel(semanticHost);
        var sourceMap = new AkcssGenerationSourceMap(akcssSyntaxTrees);
        var results = new GeneratedSource?[akcssSyntaxTrees.Length];

        Parallel.For(
            0,
            akcssSyntaxTrees.Length,
            new ParallelOptions
            {
                CancellationToken = context.CancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, EnvironmentProcessorCount.Count / 2),
            },
            index =>
            {
                var syntaxTree = akcssSyntaxTrees[index];
                var root = syntaxTree.GetRootSyntax();
                if (semanticModel.GetDeclaredSymbol(root) is not IAkcssModuleSymbol symbol)
                {
                    return;
                }

                var sourcePath = GetSourcePath(syntaxTree, projectOptions.ProjectDirectory);
                var source = AkcssGenerator.Generate(symbol, sourceMap, sourcePath);
                results[index] = new GeneratedSource(
                    AkcssGenerator.GetHintName(symbol, sourcePath),
                    SourceText.From(source, Encoding.UTF8));
            });

        foreach (var result in results)
        {
            if (result is { } generatedSource)
            {
                context.AddSource(generatedSource.HintName, generatedSource.SourceText);
            }
        }
    }

    private static ComponentSyntaxTree CreateSemanticHost(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(projectDirectory)
            ? "__AkburaGeneratorHost.akbura"
            : Path.Combine(projectDirectory, "__AkburaGeneratorHost.akbura");
        return ComponentSyntaxTree.ParseText(SourceText.From(string.Empty), path, cancellationToken);
    }

    private static string GetSourcePath(
        AkcssSyntaxTree syntaxTree,
        string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectDirectory) &&
            !string.IsNullOrWhiteSpace(syntaxTree.FilePath))
        {
            var projectPath = Path.GetFullPath(projectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sourcePath = Path.GetFullPath(syntaxTree.FilePath);
            var projectPrefix = projectPath + Path.DirectorySeparatorChar;
            if (sourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return AkcssGeneratedModuleNames.NormalizeSourcePath(
                    sourcePath.Substring(projectPrefix.Length));
            }
        }

        if (!string.IsNullOrWhiteSpace(syntaxTree.LogicalName))
        {
            return AkcssGeneratedModuleNames.NormalizeSourcePath(syntaxTree.LogicalName);
        }

        return AkcssGeneratedModuleNames.NormalizeSourcePath(
            Path.GetFileName(syntaxTree.FilePath));
    }

    private static bool IsAkburaSourcePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct AkburaAndAkcssFile(SourceText? SourceText, string Path)
{
    public AkburaSyntaxTree ToSyntaxTree(CancellationToken cancellationToken = default)
    {
        var sourceText = SourceText ?? throw new InvalidOperationException("The source text is unavailable.");
        var extension = System.IO.Path.GetExtension(Path);

        if (extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase))
        {
            return AkcssSyntaxTree.ParseText(sourceText, Path, cancellationToken);
        }

        if (extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase))
        {
            return AkburaSyntaxTree.ParseText(sourceText, Path, cancellationToken);
        }

        throw new UnreachableException();
    }
}

internal readonly record struct GeneratorProjectOptions(
    string RootNamespace,
    string ProjectDirectory)
{
    private const string RootNamespaceProperty = "build_property.RootNamespace";
    private const string ProjectDirectoryProperty = "build_property.ProjectDir";

    public static GeneratorProjectOptions Create(AnalyzerConfigOptions options)
    {
        options.TryGetValue(RootNamespaceProperty, out var rootNamespace);
        options.TryGetValue(ProjectDirectoryProperty, out var projectDirectory);
        return new GeneratorProjectOptions(
            rootNamespace ?? string.Empty,
            projectDirectory ?? string.Empty);
    }
}
