using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;

namespace Akbura.BlackSilence;

[Generator(LanguageNames.CSharp)]
public sealed class AkburaBlackSilenceGenerator : IIncrementalGenerator
{
    private const string ProjectOptionsTrackingName = "BlackSilence.ProjectOptions";
    private const string SourceTextsTrackingName = "BlackSilence.SourceTexts";
    private const string SourceFilesTrackingName = "BlackSilence.SourceFiles";
    private const string SyntaxTreesTrackingName = "BlackSilence.SyntaxTrees";
    private const string GenerationRequestsTrackingName = "BlackSilence.GenerationRequests";
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

        // Combining a values provider directly can retain an empty cached removal
        // slot in Roslyn and drop a later document on the next unchanged run.
        // Combine the collected input, then let SelectMany compare each file.
        var sourceFiles = sourceTexts
            .Collect()
            .Combine(projectOptions)
            .SelectMany(static (input, cancellationToken) => CreateSourceFiles(input.Left, input.Right, cancellationToken))
            .WithTrackingName(SourceFilesTrackingName);

        var syntaxTrees = sourceFiles
            .Select(static (sourceFile, cancellationToken) => ParseSyntaxTree(sourceFile, cancellationToken))
            .WithTrackingName(SyntaxTreesTrackingName);

        var documents = syntaxTrees
            .Select(static (tree, cancellationToken) => DocumentSyntaxVersion.Create(tree, cancellationToken))
            .WithComparer(DocumentSyntaxVersionComparer.Instance)
            .WithTrackingName("BlackSilence.Documents");

        var collectedDocuments = documents.Collect();
        var csharpEnvironment = context.CompilationProvider
            .Combine(collectedDocuments)
            .Combine(projectOptions)
            .Select(static (input, cancellationToken) => CSharpEnvironmentSnapshot.Create(
                (CSharpCompilation)input.Left.Left,
                input.Left.Right,
                input.Right,
                cancellationToken))
            .WithComparer(CSharpEnvironmentSnapshotComparer.Instance)
            .WithTrackingName("BlackSilence.CSharpEnvironment");

        var projectState = csharpEnvironment
            .Select(static (environment, _) => new BlackSilenceProjectState(environment.Compilation))
            .WithTrackingName("BlackSilence.ProjectState");

        var generationRequests = collectedDocuments
            .Combine(projectState)
            .Combine(projectOptions)
            .Select(static (input, cancellationToken) => BlackSilenceGenerationRequestBuilder.Create(
                input.Left.Left,
                input.Left.Right,
                input.Right,
                cancellationToken))
            .WithComparer(GenerationRequestComparer.Instance)
            .WithTrackingName(GenerationRequestsTrackingName);

        var generated = generationRequests
            .Select(static (request, cancellationToken) => request == null
                ? null
                : BlackSilenceDocumentBatch.Generate(request, cancellationToken))
            .WithTrackingName("BlackSilence.GeneratedBatch");

        var generatedComponents = generated
            .SelectMany(static (batch, _) => batch?.Components ?? [])
            .WithComparer(GeneratedSourceComparer.Instance)
            .WithTrackingName(GeneratedComponentsTrackingName);

        var generatedExternalAkcss = generated
            .SelectMany(static (batch, _) => batch?.ExternalAkcss ?? [])
            .WithComparer(GeneratedSourceComparer.Instance)
            .WithTrackingName(GeneratedExternalAkcssTrackingName);

        var generatedInlineAkcss = generated
            .SelectMany(static (batch, _) => batch?.InlineAkcss ?? [])
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
#if STATS
        GenerationStatistics.Increment(GenerationStatisticCounter.ReadSourceText);
#endif
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

    private static ImmutableArray<AkburaSourceFile> CreateSourceFiles(
        ImmutableArray<AkburaSourceText> sourceTexts,
        GeneratorProjectOptions projectOptions,
        CancellationToken cancellationToken)
    {
        var sourceFiles = new AkburaSourceFile[sourceTexts.Length];
        for (var i = 0; i < sourceFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourceFiles[i] = CreateSourceFile(sourceTexts[i], projectOptions);
        }

        return sourceFiles.ToImmutableArrayUnsafe();
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


    private static void AddGeneratedSource(SourceProductionContext context, GeneratedSource source)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.AddSource(source.HintName, source.SourceText);
    }

    private static AkburaSyntaxTree ParseSyntaxTree(
        AkburaSourceFile sourceFile,
        CancellationToken cancellationToken)
    {
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.Parse);
#endif
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
#if STATS
        if (previousSyntaxTree is ComponentSyntaxTree && sourceFile.Kind == SyntaxTreeKind.Component ||
            previousSyntaxTree is AkcssSyntaxTree && sourceFile.Kind == SyntaxTreeKind.Akcss)
        {
            GenerationStatistics.Increment(GenerationStatisticCounter.IncrementalParse);
        }
#endif
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
#if STATS
        GenerationStatistics.Increment(GenerationStatisticCounter.FullParse);
#endif
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
