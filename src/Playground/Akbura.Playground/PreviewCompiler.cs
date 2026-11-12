using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Playground;

/// <summary>
/// Compiles a component and its inline AKCSS into a preview assembly.
/// </summary>
internal sealed class PreviewCompiler
{
    private const string PreviewAssemblyName = "Akbura.Playground.Preview";
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
    private static long _browserAssemblyRevision;

    private readonly CSharpCompilation _compilation;
    private readonly string _rootNamespace;
    private readonly string _projectDirectory;
    private CompilationSnapshot? _lastSuccessfulCompilation;

    public PreviewCompiler(
        ImmutableArray<MetadataReference> references,
        string rootNamespace = "Akbura.Playground",
        string projectDirectory = "")
    {
        _rootNamespace = rootNamespace;
        _projectDirectory = projectDirectory;
        _compilation = CSharpCompilation.Create(
            PreviewAssemblyName,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public PreviewCompilation Compile(SourceText source, string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = Volatile.Read(ref _lastSuccessfulCompilation);
        var tree = previous != null && previous.Tree.FilePath == filePath
            ? UpdateTree(previous.Tree, source, cancellationToken)
            : ComponentSyntaxTree.ParseText(source, filePath, cancellationToken);
        using var messages = ImmutableArrayBuilder<string>.Rent();

        if (tree.GetRoot().ContainsDiagnostics)
        {
            var syntaxDiagnostics = AkburaDiagnosticEngine.Collect(tree, includeSemantic: false,
                cancellationToken: cancellationToken);
            if (AppendDiagnostics(syntaxDiagnostics, messages))
            {
                return new PreviewCompilation(messages.ToImmutable());
            }

            messages.Clear();
        }

        var catalog = AkburaGenerationCatalogBuilder.Create(
            _compilation, [tree], _rootNamespace, _projectDirectory, previous?.Compilation, cancellationToken);
        var model = catalog.Compilation.GetSemanticModel(tree);
        var diagnostics = AkburaDiagnosticEngine.Collect(tree, model, cancellationToken: cancellationToken);
        if (AppendDiagnostics(diagnostics, messages))
        {
            return new PreviewCompilation(messages.ToImmutable());
        }

        if (catalog.Components.Length != 1)
        {
            messages.Add("The preview needs one Akbura component.");
            return new PreviewCompilation(messages.ToImmutable());
        }

        var input = catalog.Components[0];
        var componentSource = ComponentDocumentWriter.Generate(input.Component, input.SemanticModel,
            input.SourcePath, catalog.AkcssModuleTypeNames, cancellationToken);

        using var generatedTrees = ImmutableArrayBuilder<SyntaxTree>.Rent(1 + catalog.InlineAkcssModules.Length);
        generatedTrees.Add(CSharpSyntaxTree.ParseText(componentSource, ParseOptions,
            ComponentDocumentWriter.GetHintName(input.Component, input.SourcePath), cancellationToken));
        foreach (var module in catalog.InlineAkcssModules)
        {
            var moduleSource = AkcssDocumentWriter.Generate(module, catalog.AkcssSourceMap, catalog.RootNamespace,
                cancellationToken);
            generatedTrees.Add(CSharpSyntaxTree.ParseText(moduleSource, ParseOptions,
                AkcssDocumentWriter.GetHintName(module), cancellationToken));
        }

        var compilation = _compilation.AddSyntaxTrees(generatedTrees.ToImmutable());
        if (OperatingSystem.IsBrowser())
        {
            compilation = compilation.WithAssemblyName($"{PreviewAssemblyName}.{Interlocked.Increment(ref _browserAssemblyRevision)}");
        }

        using var image = new MemoryStream();
        var emit = compilation.Emit(image, cancellationToken: cancellationToken);
        foreach (var diagnostic in emit.Diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            {
                messages.Add(diagnostic.ToString());
            }
        }

        var results = messages.ToImmutable();
        if (!emit.Success)
        {
            return new PreviewCompilation(results);
        }

        var preview = new PreviewCompilation(results, image.ToArray(), input.Component.MetadataName);
        cancellationToken.ThrowIfCancellationRequested();
        // A concurrent compile may have already replaced the snapshot we started from.
        Interlocked.CompareExchange(ref _lastSuccessfulCompilation, new CompilationSnapshot(tree, catalog.Compilation), previous);
        return preview;
    }

    private static ComponentSyntaxTree UpdateTree(ComponentSyntaxTree previous, SourceText source, CancellationToken cancellationToken)
    {
        var oldText = previous.Text.ToString().AsSpan();
        var newText = source.ToString().AsSpan();
        var start = oldText.CommonPrefixLength(newText);
        if (start == oldText.Length && start == newText.Length)
        {
            return previous;
        }

        // The editor supplies independent text snapshots. Narrow the changed range
        // so the parser can keep syntax nodes outside the edit.
        var oldEnd = oldText.Length;
        var newEnd = newText.Length;
        while (oldEnd > start && newEnd > start && oldText[oldEnd - 1] == newText[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        var change = new TextChangeRange(TextSpan.FromBounds(start, oldEnd), newEnd - start);
        return previous.WithChangedText(source, [change], cancellationToken);
    }

    private static bool AppendDiagnostics(
        ImmutableArray<AkburaDiagnosticRecord> diagnostics, ImmutableArrayBuilder<string> messages)
    {
        var hasErrors = false;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is not (AkburaDiagnosticSeverity.Error or AkburaDiagnosticSeverity.Warning))
            {
                continue;
            }

            hasErrors |= diagnostic.Severity == AkburaDiagnosticSeverity.Error;
            var position = diagnostic.LineSpan.Start;
            messages.Add($"{Path.GetFileName(diagnostic.FilePath)}({position.Line + 1},{position.Character + 1}): " +
                $"{diagnostic.Id} {diagnostic.Message}");
        }

        return hasErrors;
    }

    private sealed record CompilationSnapshot(ComponentSyntaxTree Tree, AkburaCompilation Compilation);
}
