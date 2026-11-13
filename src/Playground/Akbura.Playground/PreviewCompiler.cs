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
/// Compiles an Akbura project into a preview assembly.
/// </summary>
internal sealed class PreviewCompiler
{
    private const string PreviewAssemblyName = "Akbura.Playground.Preview";
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
    private static readonly StringComparer FilePathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
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
        _projectDirectory = Path.GetFullPath(string.IsNullOrEmpty(projectDirectory) ? "." : projectDirectory);
        _compilation = CSharpCompilation.Create(
            PreviewAssemblyName,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public PreviewCompilation Compile(
        ImmutableArray<(string FilePath, SourceText Text)> sources,
        string entryFilePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = Volatile.Read(ref _lastSuccessfulCompilation);
        var entryPath = Path.GetFullPath(entryFilePath, _projectDirectory);
        var sourceTrees = ImmutableDictionary.CreateBuilder<string, AkburaSyntaxTree>(FilePathComparer);
        using var trees = ImmutableArrayBuilder<AkburaSyntaxTree>.Rent(sources.Length);
        using var messages = ImmutableArrayBuilder<string>.Rent();
        var hasSyntaxErrors = false;

        foreach (var (filePath, source) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(filePath, _projectDirectory);
            var extension = Path.GetExtension(path);
            var isAkcss = extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase);
            if (!isAkcss && !extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase))
            {
                return new PreviewCompilation([$"'{filePath}' must be an .akbura or .akcss file."]);
            }
            if (sourceTrees.ContainsKey(path))
            {
                return new PreviewCompilation([$"The preview project contains '{filePath}' more than once."]);
            }

            var tree = previous != null && previous.Trees.TryGetValue(path, out var oldTree)
                ? UpdateTree(oldTree, source, cancellationToken)
                : ParseTree(source, path, isAkcss, cancellationToken);
            trees.Add(tree);
            sourceTrees.Add(path, tree);
            if (tree.GetRootSyntax().ContainsDiagnostics)
            {
                var syntaxDiagnostics = AkburaDiagnosticEngine.Collect(tree, includeSemantic: false,
                    cancellationToken: cancellationToken);
                hasSyntaxErrors |= AppendDiagnostics(syntaxDiagnostics, messages);
            }
        }

        if (hasSyntaxErrors)
        {
            return new PreviewCompilation(messages.ToImmutable());
        }
        if (!sourceTrees.TryGetValue(entryPath, out var entryTree) || entryTree is not ComponentSyntaxTree ||
            GlobalUsings.IsComponentFile(entryTree))
        {
            return new PreviewCompilation([$"'{entryFilePath}' must name a component in the preview project."]);
        }

        messages.Clear();
        var catalog = AkburaGenerationCatalogBuilder.Create(
            _compilation, trees.ToImmutable(), _rootNamespace, _projectDirectory, previous?.Compilation, cancellationToken);

        using var diagnosticRecords = ImmutableArrayBuilder<AkburaDiagnosticRecord>.Rent();
        foreach (var tree in trees.WrittenSpan)
        {
            var model = GlobalUsings.IsComponentFile(tree) || GlobalUsings.IsAkcssFile(tree)
                ? null
                : catalog.Compilation.GetSemanticModel(tree);
            var diagnostics = AkburaDiagnosticEngine.Collect(tree, model, cancellationToken: cancellationToken);
            diagnosticRecords.AddRange(diagnostics.AsSpan());
        }
        if (diagnosticRecords.Count > 0 &&
            AppendDiagnostics(AkburaDiagnosticCollection.Deduplicate(diagnosticRecords.AsEnumerable()), messages))
        {
            return new PreviewCompilation(messages.ToImmutable());
        }

        var generatedFileCount = catalog.Components.Length + catalog.ExternalAkcssModules.Length + catalog.InlineAkcssModules.Length;
        using var generatedTrees = ImmutableArrayBuilder<SyntaxTree>.Rent(generatedFileCount);
        string? componentTypeName = null;
        foreach (var input in catalog.Components)
        {
            var componentSource = ComponentDocumentWriter.Generate(input.Component, input.SemanticModel,
                input.SourcePath, catalog.AkcssModuleTypeNames, cancellationToken);
            generatedTrees.Add(CSharpSyntaxTree.ParseText(componentSource, ParseOptions,
                ComponentDocumentWriter.GetHintName(input.Component, input.SourcePath), cancellationToken));
            if (ReferenceEquals(input.SemanticModel.SyntaxTree, entryTree))
            {
                componentTypeName = input.Component.MetadataName;
            }
        }
        if (componentTypeName == null)
        {
            messages.Add($"The preview could not find the component declared in '{entryFilePath}'.");
            return new PreviewCompilation(messages.ToImmutable());
        }

        foreach (var module in catalog.ExternalAkcssModules)
        {
            generatedTrees.Add(GenerateAkcss(module, catalog, cancellationToken));
        }
        foreach (var module in catalog.InlineAkcssModules)
        {
            generatedTrees.Add(GenerateAkcss(module, catalog, cancellationToken));
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

        var preview = new PreviewCompilation(results, image.ToArray(), componentTypeName);
        cancellationToken.ThrowIfCancellationRequested();
        // A concurrent compile may have already replaced the snapshot we started from.
        var snapshot = new CompilationSnapshot(sourceTrees.ToImmutable(), catalog.Compilation);
        Interlocked.CompareExchange(ref _lastSuccessfulCompilation, snapshot, previous);
        return preview;
    }

    private AkburaSyntaxTree ParseTree(SourceText source, string path, bool isAkcss, CancellationToken cancellationToken)
    {
        if (!isAkcss)
        {
            return ComponentSyntaxTree.ParseText(source, path, cancellationToken);
        }

        var sourcePath = Path.GetRelativePath(_projectDirectory, path);
        if (Path.IsPathRooted(sourcePath) || sourcePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            sourcePath = Path.GetFileName(path);
        }
        var logicalName = AkcssGeneratedModuleNames.GetMetadataName(_rootNamespace, sourcePath);
        return AkcssSyntaxTree.ParseText(source, path, logicalName, cancellationToken);
    }

    private static SyntaxTree GenerateAkcss(AkcssGenerationInput module, AkburaGenerationCatalog catalog,
        CancellationToken cancellationToken)
    {
        var moduleSource = AkcssDocumentWriter.Generate(module, catalog.AkcssSourceMap, catalog.RootNamespace, cancellationToken);
        return CSharpSyntaxTree.ParseText(moduleSource, ParseOptions, AkcssDocumentWriter.GetHintName(module), cancellationToken);
    }

    private static AkburaSyntaxTree UpdateTree(AkburaSyntaxTree previous, SourceText source, CancellationToken cancellationToken)
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
        return previous switch
        {
            ComponentSyntaxTree component => component.WithChangedText(source, [change], cancellationToken),
            AkcssSyntaxTree akcss => akcss.WithChangedText(source, [change], cancellationToken),
            _ => throw new InvalidOperationException("Unsupported preview syntax tree."),
        };
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

    private sealed record CompilationSnapshot(ImmutableDictionary<string, AkburaSyntaxTree> Trees, AkburaCompilation Compilation);
}
