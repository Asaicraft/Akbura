using Akbura.Language.Declarations;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language;

internal sealed class AkburaCompilation
{
    private readonly Dictionary<AkburaSyntaxTree, AkburaSemanticModel> _semanticModels = new();
    private AkburaDeclarationTable? _declarationTable;

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        IEnumerable<AkburaSyntaxTree> syntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "")
        : this(csharpCompilation, syntaxTrees.ToImmutableArray(), ImmutableArray<AkcssSyntaxTree>.Empty, rootNamespace, projectDirectory)
    {
    }

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        IEnumerable<AkburaSyntaxTree> syntaxTrees,
        IEnumerable<AkcssSyntaxTree> akcssSyntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "")
        : this(csharpCompilation, syntaxTrees.ToImmutableArray(), akcssSyntaxTrees.ToImmutableArray(), rootNamespace, projectDirectory)
    {
    }

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "")
        : this(csharpCompilation, syntaxTrees, ImmutableArray<AkcssSyntaxTree>.Empty, rootNamespace, projectDirectory)
    {
    }

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "",
        AkburaCompilation? previousCompilation = null)
    {
        CSharpCompilation = csharpCompilation ?? throw new ArgumentNullException(nameof(csharpCompilation));
        SyntaxTrees = syntaxTrees.IsDefault ? ImmutableArray<AkburaSyntaxTree>.Empty : syntaxTrees;
        AkcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? ImmutableArray<AkcssSyntaxTree>.Empty
            : akcssSyntaxTrees;
        RootNamespace = rootNamespace ?? string.Empty;
        ProjectDirectory = projectDirectory ?? string.Empty;
        PreviousCompilation = previousCompilation;
    }

    public CSharpCompilation CSharpCompilation { get; }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees { get; }

    public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees { get; }

    public string RootNamespace { get; }

    public string ProjectDirectory { get; }

    public AkburaCompilation? PreviousCompilation { get; }

    public AkburaDeclarationTable DeclarationTable =>
        _declarationTable ??= AkburaDeclarationTable.Create(this);

    public AkburaCompilation WithSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxTrees(syntaxTrees.ToImmutableArray());
    }

    public AkburaCompilation WithSyntaxTrees(ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        syntaxTrees = syntaxTrees.IsDefault
            ? ImmutableArray<AkburaSyntaxTree>.Empty
            : syntaxTrees;
        if (SyntaxTrees.SequenceEqual(syntaxTrees))
        {
            return this;
        }

        return new AkburaCompilation(
            CSharpCompilation,
            syntaxTrees,
            AkcssSyntaxTrees,
            RootNamespace,
            ProjectDirectory,
            previousCompilation: this);
    }

    public AkburaCompilation WithAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        return WithAkcssSyntaxTrees(akcssSyntaxTrees.ToImmutableArray());
    }

    public AkburaCompilation WithAkcssSyntaxTrees(ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? ImmutableArray<AkcssSyntaxTree>.Empty
            : akcssSyntaxTrees;
        if (AkcssSyntaxTrees.SequenceEqual(akcssSyntaxTrees))
        {
            return this;
        }

        return new AkburaCompilation(
            CSharpCompilation,
            SyntaxTrees,
            akcssSyntaxTrees,
            RootNamespace,
            ProjectDirectory,
            previousCompilation: this);
    }

    public AkburaCompilation WithCSharpCompilation(CSharpCompilation csharpCompilation)
    {
        if (ReferenceEquals(CSharpCompilation, csharpCompilation))
        {
            return this;
        }

        return new AkburaCompilation(
            csharpCompilation,
            SyntaxTrees,
            AkcssSyntaxTrees,
            RootNamespace,
            ProjectDirectory,
            previousCompilation: this);
    }

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
