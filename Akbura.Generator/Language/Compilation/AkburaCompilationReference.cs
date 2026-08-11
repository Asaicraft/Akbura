using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language;

/// <summary>
/// References an in-memory Akbura project snapshot without crossing the PE/module boundary.
/// </summary>
internal sealed class AkburaCompilationReference
{
    private readonly ConcurrentDictionary<string, IAkburaComponentSymbol> _componentSymbols =
        new(StringComparer.Ordinal);

    private ImmutableArray<string> _componentMetadataNames;

    public AkburaCompilationReference(AkburaCompilation compilation)
        : this(
            compilation,
            compilation?.CSharpProbeCompilation.ToMetadataReference() ??
            throw new ArgumentNullException(nameof(compilation)))
    {
    }

    private AkburaCompilationReference(
        AkburaCompilation compilation,
        MetadataReference csharpReference)
    {
        Compilation = compilation;
        CSharpReference = csharpReference;
    }

    public AkburaCompilation Compilation { get; }

    public MetadataReference CSharpReference { get; }

    internal int CachedComponentSymbolCount =>
        _componentSymbols.Count;

    public AkburaCompilationReference WithCompilation(AkburaCompilation compilation)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (ReferenceEquals(Compilation, compilation))
        {
            return this;
        }

        var csharpReference = ReferenceEquals(
                Compilation.CSharpCompilation,
                compilation.CSharpCompilation)
            ? CSharpReference
            : compilation.CSharpCompilation.ToMetadataReference();
        return new AkburaCompilationReference(compilation, csharpReference);
    }

    internal bool TryGetComponentSymbol(
        string metadataName,
        out IAkburaComponentSymbol symbol)
    {
        if (_componentSymbols.TryGetValue(metadataName, out symbol!))
        {
            return true;
        }

        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            var semanticModel = Compilation.GetSemanticModel(syntaxTree);
            if (!string.Equals(
                    semanticModel.GetAkburaComponentMetadataName(syntaxTree),
                    metadataName,
                    StringComparison.Ordinal) ||
                semanticModel.GetDeclaredSymbol(
                    syntaxTree.GetRoot()) is not IAkburaComponentSymbol candidate)
            {
                continue;
            }

            symbol = _componentSymbols.GetOrAdd(metadataName, candidate);
            return true;
        }

        foreach (var candidate in Compilation.GetReferencedComponentSymbols(metadataName))
        {
            symbol = _componentSymbols.GetOrAdd(metadataName, candidate);
            return true;
        }

        symbol = null!;
        return false;
    }

    internal ImmutableArray<string> GetComponentMetadataNames(
        CancellationToken cancellationToken = default)
    {
        if (!_componentMetadataNames.IsDefault)
        {
            return _componentMetadataNames;
        }

        var names = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = Compilation.GetSemanticModel(syntaxTree);
            var metadataName = semanticModel
                .GetAkburaComponentMetadataName(syntaxTree);
            if (metadataName.Length > 0 && seen.Add(metadataName))
            {
                names.Add(metadataName);
            }
        }

        foreach (var reference in Compilation.CompilationReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var metadataName in
                     reference.GetComponentMetadataNames(
                         cancellationToken))
            {
                if (seen.Add(metadataName))
                {
                    names.Add(metadataName);
                }
            }
        }

        var result = names.ToImmutable();
        ImmutableInterlocked.InterlockedInitialize(
            ref _componentMetadataNames,
            result);
        return _componentMetadataNames;
    }

    internal IEnumerable<IAkburaComponentSymbol> GetComponentSymbols()
    {
        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            var semanticModel = Compilation.GetSemanticModel(syntaxTree);
            if (semanticModel.GetDeclaredSymbol(
                    syntaxTree.GetRoot()) is IAkburaComponentSymbol symbol)
            {
                yield return symbol;
            }
        }

        foreach (var symbol in Compilation.GetReferencedComponentSymbols())
        {
            yield return symbol;
        }
    }

    internal ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(
        string logicalName)
    {
        return Compilation.GetAkcssSyntaxTreesByLogicalName(logicalName);
    }

    internal ImmutableArray<IAkcssModuleSymbol> GetAkcssModuleSymbolsByLogicalName(
        string logicalName)
    {
        return Compilation.GetExportedAkcssModuleSymbolsByLogicalName(logicalName);
    }

    internal bool ContainsComponentSyntaxTree(AkburaSyntaxTree syntaxTree)
    {
        return Compilation.ContainsComponentSyntaxTree(syntaxTree);
    }

    internal bool ContainsAkcssSyntaxTree(AkcssSyntaxTree syntaxTree)
    {
        return Compilation.ContainsAkcssSyntaxTree(syntaxTree);
    }

    internal bool TryGetSemanticModel(
        AkburaSyntaxTree syntaxTree,
        out AkburaSemanticModel semanticModel)
    {
        return Compilation.TryGetSemanticModel(syntaxTree, out semanticModel);
    }

    internal bool TryGetDeclaration(
        AkburaSyntax syntax,
        out Declaration declaration)
    {
        return Compilation.TryGetDeclaration(syntax, out declaration);
    }

    internal bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<Declaration> path)
    {
        return Compilation.TryGetDeclarationPath(syntax, out path);
    }

    internal bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<Declaration> path)
    {
        return Compilation.TryGetDeclarationPath(syntax, position, out path);
    }
}
