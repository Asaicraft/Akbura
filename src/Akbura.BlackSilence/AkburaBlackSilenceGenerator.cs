using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.BlackSilence;

[Generator(LanguageNames.CSharp)]
public sealed class AkburaBlackSilenceGenerator : IIncrementalGenerator
{
    private const string ProjectOptionsTrackingName = "BlackSilence.ProjectOptions";
    private const string SourceTextsTrackingName = "BlackSilence.SourceTexts";
    private const string SourceFilesTrackingName = "BlackSilence.SourceFiles";
    private const string SyntaxTreesTrackingName = "BlackSilence.SyntaxTrees";
    private const string GenerationCatalogTrackingName = "BlackSilence.GenerationCatalog";
    private const string GeneratedComponentsTrackingName = "BlackSilence.GeneratedComponents";
    private const string GeneratedExternalAkcssTrackingName = "BlackSilence.GeneratedExternalAkcss";
    private const string GeneratedInlineAkcssTrackingName = "BlackSilence.GeneratedInlineAkcss";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var projectOptions = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => GeneratorProjectOptions.Create(provider.GlobalOptions))
            .WithTrackingName(ProjectOptionsTrackingName);

        var sourceTexts = context.AdditionalTextsProvider
            .Where(static file => IsAkburaSourcePath(file.Path))
            .Select(static (file, cancellationToken) => ReadSourceText(file, cancellationToken))
            .Where(static sourceText => sourceText.HasValue)
            .Select(static (sourceText, _) => sourceText.GetValueOrDefault())
            .WithTrackingName(SourceTextsTrackingName);

        var sourceFiles = sourceTexts
            .Combine(projectOptions)
            .Select(static (input, _) => CreateSourceFile(input.Left, input.Right))
            .WithTrackingName(SourceFilesTrackingName);

        var syntaxTrees = sourceFiles
            .Select(static (sourceFile, cancellationToken) => ParseSyntaxTree(sourceFile, cancellationToken))
            .WithTrackingName(SyntaxTreesTrackingName);

        var generationCatalog = syntaxTrees
            .Collect()
            .Combine(context.CompilationProvider)
            .Combine(projectOptions)
            .Select(static (input, cancellationToken) => CreateGenerationCatalog(
                input.Left.Left,
                input.Left.Right,
                input.Right,
                cancellationToken))
            .WithTrackingName(GenerationCatalogTrackingName);

        var generatedComponents = generationCatalog
            .SelectMany(static (catalog, cancellationToken) => catalog != null
                ? GenerateComponents(catalog, cancellationToken)
                : [])
            .WithComparer(GeneratedSourceComparer.Instance)
            .WithTrackingName(GeneratedComponentsTrackingName);

        var generatedExternalAkcss = generationCatalog
            .SelectMany(static (catalog, cancellationToken) => catalog != null
                ? GenerateExternalAkcss(catalog, cancellationToken)
                : [])
            .WithComparer(GeneratedSourceComparer.Instance)
            .WithTrackingName(GeneratedExternalAkcssTrackingName);

        var generatedInlineAkcss = generationCatalog
            .SelectMany(static (catalog, cancellationToken) => catalog != null
                ? GenerateInlineAkcss(catalog, cancellationToken)
                : [])
            .WithComparer(GeneratedSourceComparer.Instance)
            .WithTrackingName(GeneratedInlineAkcssTrackingName);

        context.RegisterSourceOutput(
            generatedComponents,
            static (productionContext, source) => AddGeneratedSource(productionContext, source));

        context.RegisterSourceOutput(
            generatedExternalAkcss,
            static (productionContext, source) => AddGeneratedSource(productionContext, source));

        context.RegisterSourceOutput(
            generatedInlineAkcss,
            static (productionContext, source) => AddGeneratedSource(productionContext, source));
    }

    private static AkburaSourceText? ReadSourceText(
        AdditionalText file,
        CancellationToken cancellationToken)
    {
        var sourceText = file.GetText(cancellationToken);

        if (sourceText == null)
        {
            return null;
        }

        var extension = Path.GetExtension(file.Path);
        var kind = extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase)
            ? SyntaxTreeKind.Akcss
            : SyntaxTreeKind.Component;

        return new AkburaSourceText(kind, file.Path, sourceText);
    }

    private static AkburaSourceFile CreateSourceFile(
        AkburaSourceText sourceText,
        GeneratorProjectOptions projectOptions)
    {
        if (sourceText.Kind == SyntaxTreeKind.Component)
        {
            return new AkburaSourceFile(
                sourceText.Kind,
                sourceText.FilePath,
                string.Empty,
                sourceText.SourceText);
        }

        var sourcePath = GetProjectRelativeSourcePath(
            sourceText.FilePath,
            projectOptions.ProjectDirectory);

        var logicalName = AkcssGeneratedModuleNames.GetMetadataName(
            projectOptions.RootNamespace,
            sourcePath);

        return new AkburaSourceFile(
            sourceText.Kind,
            sourceText.FilePath,
            logicalName,
            sourceText.SourceText);
    }

    private static AkburaGenerationCatalog? CreateGenerationCatalog(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        Compilation compilation,
        GeneratorProjectOptions projectOptions,
        CancellationToken cancellationToken)
    {
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return null;
        }

        return AkburaGenerationCatalogBuilder.Create(
            csharpCompilation,
            syntaxTrees,
            projectOptions.RootNamespace,
            projectOptions.ProjectDirectory,
            cancellationToken);
    }

    private static ImmutableArray<GeneratedSource> GenerateComponents(
        AkburaGenerationCatalog catalog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inputs = catalog.Components;

        if (inputs.IsEmpty)
        {
            return [];
        }

        var results = new GeneratedSource[inputs.Length];

        Parallel.For(
            0,
            inputs.Length,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, ProcessorCountHelper.GetProcessorCount() / 4),
            },
            index =>
            {
                var input = inputs[index];

                var sourceText = ComponentDocumentWriter.Generate(
                    input.Component,
                    input.SemanticModel,
                    input.SourcePath,
                    catalog.AkcssModuleTypeNames,
                    cancellationToken);

                var hintName = ComponentDocumentWriter.GetHintName(
                    input.Component,
                    input.SourcePath);

                results[index] = new GeneratedSource(hintName, sourceText);
            });

        return results.ToImmutableArrayUnsafe();
    }

    private static ImmutableArray<GeneratedSource> GenerateExternalAkcss(
        AkburaGenerationCatalog catalog,
        CancellationToken cancellationToken)
    {
        return GenerateAkcss(catalog.ExternalAkcssModules, catalog, cancellationToken);
    }

    private static ImmutableArray<GeneratedSource> GenerateInlineAkcss(
        AkburaGenerationCatalog catalog,
        CancellationToken cancellationToken)
    {
        return GenerateAkcss(catalog.InlineAkcssModules, catalog, cancellationToken);
    }

    private static ImmutableArray<GeneratedSource> GenerateAkcss(
        ImmutableArray<AkcssGenerationInput> inputs,
        AkburaGenerationCatalog catalog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (inputs.IsEmpty)
        {
            return [];
        }

        var results = new GeneratedSource[inputs.Length];

        Parallel.For(
            0,
            inputs.Length,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, ProcessorCountHelper.GetProcessorCount() / 2),
            },
            index =>
            {
                var input = inputs[index];

                var sourceText = AkcssDocumentWriter.Generate(
                    input,
                    catalog.AkcssSourceMap,
                    catalog.RootNamespace,
                    cancellationToken);

                var hintName = AkcssDocumentWriter.GetHintName(input);

                results[index] = new GeneratedSource(hintName, sourceText);
            });

        return results.ToImmutableArrayUnsafe();
    }

    private static void AddGeneratedSource(SourceProductionContext context, GeneratedSource source)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.AddSource(source.HintName, source.SourceText);
    }

    private static AkburaSyntaxTree ParseSyntaxTree(
        AkburaSourceFile sourceFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cached = IncrementalParseCache.TryGet(
            sourceFile.Kind,
            sourceFile.FilePath,
            sourceFile.LogicalName,
            sourceFile.SourceText,
            out var hash);

        if (cached != null)
        {
            if (sourceFile.SourceText.ContentEquals(cached.SourceText))
            {
                return cached.SyntaxTree;
            }

            var syntaxTree = ParseIncrementally(cached.SyntaxTree, sourceFile, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            IncrementalParseCache.Add(
                sourceFile.Kind,
                sourceFile.FilePath,
                sourceFile.LogicalName,
                sourceFile.SourceText,
                syntaxTree,
                hash);

            return syntaxTree;
        }

        var fullSyntaxTree = ParseFull(sourceFile, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        IncrementalParseCache.Add(
            sourceFile.Kind,
            sourceFile.FilePath,
            sourceFile.LogicalName,
            sourceFile.SourceText,
            fullSyntaxTree,
            hash);

        return fullSyntaxTree;
    }

    private static AkburaSyntaxTree ParseIncrementally(
        AkburaSyntaxTree previousSyntaxTree,
        AkburaSourceFile sourceFile,
        CancellationToken cancellationToken)
    {
        return previousSyntaxTree switch
        {
            ComponentSyntaxTree componentSyntaxTree when sourceFile.Kind == SyntaxTreeKind.Component =>
                componentSyntaxTree.WithChangedText(
                    sourceFile.SourceText,
                    changes: null,
                    cancellationToken),

            AkcssSyntaxTree akcssSyntaxTree when sourceFile.Kind == SyntaxTreeKind.Akcss =>
                akcssSyntaxTree.WithChangedText(
                    sourceFile.SourceText,
                    changes: null,
                    cancellationToken),

            _ => ParseFull(sourceFile, cancellationToken),
        };
    }

    private static AkburaSyntaxTree ParseFull(
        AkburaSourceFile sourceFile,
        CancellationToken cancellationToken)
    {
        return sourceFile.Kind switch
        {
            SyntaxTreeKind.Component => ComponentSyntaxTree.ParseText(
                sourceFile.SourceText,
                sourceFile.FilePath,
                cancellationToken),

            SyntaxTreeKind.Akcss => AkcssSyntaxTree.ParseText(
                sourceFile.SourceText,
                sourceFile.FilePath,
                sourceFile.LogicalName,
                cancellationToken),

            _ => throw new InvalidOperationException(
                $"Unsupported syntax tree kind '{sourceFile.Kind}'."),
        };
    }

    private static string GetProjectRelativeSourcePath(string filePath, string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectDirectory) &&
            !string.IsNullOrWhiteSpace(filePath))
        {
            var projectPath = Path.GetFullPath(projectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var fullSourcePath = Path.GetFullPath(filePath);
            var projectPrefix = projectPath + Path.DirectorySeparatorChar;

            if (fullSourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return AkcssGeneratedModuleNames.NormalizeSourcePath(
                    fullSourcePath[projectPrefix.Length..]);
            }
        }

        return AkcssGeneratedModuleNames.NormalizeSourcePath(Path.GetFileName(filePath));
    }

    private static bool IsAkburaSourcePath(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase);
    }
}
