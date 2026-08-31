using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.IO;
using System.Threading;

namespace Akbura.BlackSilence;

[Generator(LanguageNames.CSharp)]
public sealed class AkburaBlackSilenceGenerator : IIncrementalGenerator
{
    public void Initialize(
        IncrementalGeneratorInitializationContext context)
    {
        var sourceFiles = context.AdditionalTextsProvider
            .Where(static file =>
                IsAkburaSourcePath(file.Path))
            .Select(static (file, cancellationToken) =>
                ReadSourceFile(
                    file,
                    cancellationToken))
            .Where(static sourceFile =>
                sourceFile != null)
            .Select(static (sourceFile, _) =>
                sourceFile!)
            .WithTrackingName(
                "BlackSilence.SourceFiles");

        var syntaxTrees = sourceFiles
            .Select(static (sourceFile, cancellationToken) =>
                ParseSyntaxTree(
                    sourceFile,
                    cancellationToken))
            .WithTrackingName(
                "BlackSilence.SyntaxTrees");

        // Incremental pipelines are evaluated through registered outputs.
        // This output intentionally emits no source code. It only connects
        // the live parsing pipeline to the Roslyn generator driver.
        context.RegisterSourceOutput(
            syntaxTrees,
            static (productionContext, _) =>
            {
                productionContext.CancellationToken
                    .ThrowIfCancellationRequested();
            });
    }

    private static AkburaSourceFile? ReadSourceFile(
        AdditionalText file,
        CancellationToken cancellationToken)
    {
        var sourceText =
            file.GetText(cancellationToken);

        if (sourceText == null)
        {
            return null;
        }

        var extension =
            Path.GetExtension(file.Path);

        if (extension.Equals(
                ".akbura",
                StringComparison.OrdinalIgnoreCase))
        {
            return new AkburaSourceFile(
                SyntaxTreeKind.Component,
                file.Path,
                logicalName: string.Empty,
                sourceText);
        }

        if (extension.Equals(
                ".akcss",
                StringComparison.OrdinalIgnoreCase))
        {
            return new AkburaSourceFile(
                SyntaxTreeKind.Akcss,
                file.Path,
                GetAkcssLogicalName(file.Path),
                sourceText);
        }

        return null;
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
            if (sourceFile.SourceText.ContentEquals(
                    cached.SourceText))
            {
                return cached.SyntaxTree;
            }

            var syntaxTree = ParseIncrementally(
                cached.SyntaxTree,
                sourceFile,
                cancellationToken);

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

        var fullSyntaxTree = ParseFull(
            sourceFile,
            cancellationToken);

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
            ComponentSyntaxTree componentSyntaxTree
                when sourceFile.Kind ==
                     SyntaxTreeKind.Component =>
                componentSyntaxTree.WithChangedText(
                    sourceFile.SourceText,
                    changes: null,
                    cancellationToken:
                        cancellationToken),

            AkcssSyntaxTree akcssSyntaxTree
                when sourceFile.Kind ==
                     SyntaxTreeKind.Akcss =>
                akcssSyntaxTree.WithChangedText(
                    sourceFile.SourceText,
                    changes: null,
                    cancellationToken:
                        cancellationToken),

            // A cache entry is only an optimization.
            // Any incompatible entry falls back to a full parse.
            _ => ParseFull(
                sourceFile,
                cancellationToken),
        };
    }

    private static AkburaSyntaxTree ParseFull(
        AkburaSourceFile sourceFile,
        CancellationToken cancellationToken)
    {
        return sourceFile.Kind switch
        {
            SyntaxTreeKind.Component =>
                ComponentSyntaxTree.ParseText(
                    sourceFile.SourceText,
                    sourceFile.FilePath,
                    cancellationToken),

            SyntaxTreeKind.Akcss =>
                AkcssSyntaxTree.ParseText(
                    sourceFile.SourceText,
                    sourceFile.FilePath,
                    sourceFile.LogicalName,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                $"Unsupported syntax tree kind " +
                $"'{sourceFile.Kind}'."),
        };
    }

    private static string GetAkcssLogicalName(string filePath)
    {
        return Path.GetFileName(filePath);
    }

    private static bool IsAkburaSourcePath(string filePath)
    {
        var extension =
            Path.GetExtension(filePath);

        return extension.Equals(
                   ".akbura",
                   StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(
                   ".akcss",
                   StringComparison.OrdinalIgnoreCase);
    }
}
