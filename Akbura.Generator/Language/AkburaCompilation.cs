using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language;

internal sealed class AkburaCompilation
{
    private readonly Dictionary<AkburaSyntaxTree, AkburaSemanticModel> _semanticModels = new();

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        IEnumerable<AkburaSyntaxTree> syntaxTrees)
        : this(csharpCompilation, syntaxTrees.ToImmutableArray())
    {
    }

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        CSharpCompilation = csharpCompilation ?? throw new ArgumentNullException(nameof(csharpCompilation));
        SyntaxTrees = syntaxTrees.IsDefault ? ImmutableArray<AkburaSyntaxTree>.Empty : syntaxTrees;
    }

    public CSharpCompilation CSharpCompilation { get; }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees { get; }

    public AkburaSemanticModel GetSemanticModel(AkburaSyntaxTree syntaxTree)
    {
        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }

        if (!SyntaxTrees.Contains(syntaxTree))
        {
            throw new ArgumentException("Syntax tree is not part of this compilation.", nameof(syntaxTree));
        }

        if (_semanticModels.TryGetValue(syntaxTree, out var semanticModel))
        {
            return semanticModel;
        }

        semanticModel = new AkburaSemanticModel(this, syntaxTree);
        _semanticModels.Add(syntaxTree, semanticModel);
        return semanticModel;
    }
}
