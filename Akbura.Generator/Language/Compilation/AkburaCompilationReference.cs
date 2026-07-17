using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Akbura.Language;

/// <summary>
/// References an in-memory Akbura project snapshot without crossing the PE/module boundary.
/// </summary>
internal sealed class AkburaCompilationReference
{
    private readonly ConcurrentDictionary<string, IAkburaComponentSymbol> _componentSymbols =
        new(StringComparer.Ordinal);

    public AkburaCompilationReference(AkburaCompilation compilation)
        : this(
            compilation,
            compilation?.CSharpCompilation.ToMetadataReference() ??
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
            if (semanticModel.GetDeclaredSymbol(syntaxTree.GetRoot()) is not IAkburaComponentSymbol candidate ||
                !string.Equals(candidate.MetadataName, metadataName, StringComparison.Ordinal))
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

    internal ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(
        string logicalName)
    {
        return Compilation.GetAkcssSyntaxTreesByLogicalName(logicalName);
    }

    internal bool ContainsComponentSyntaxTree(AkburaSyntaxTree syntaxTree)
    {
        return Compilation.ContainsComponentSyntaxTree(syntaxTree);
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
