using Akbura.Language.Declarations;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language;

internal sealed class AkburaCompilation
{
    private readonly ConcurrentDictionary<AkburaSyntaxTree, AkburaSemanticModel> _semanticModels = new();
    private readonly SyntaxAndDeclarationManager _syntaxAndDeclarations;

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        IEnumerable<AkburaSyntaxTree> syntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "")
        : this(csharpCompilation, [.. syntaxTrees], ImmutableArray<AkcssSyntaxTree>.Empty, rootNamespace, projectDirectory)
    {
    }

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        IEnumerable<AkburaSyntaxTree> syntaxTrees,
        IEnumerable<AkcssSyntaxTree> akcssSyntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "")
        : this(csharpCompilation, [.. syntaxTrees], [.. akcssSyntaxTrees], rootNamespace, projectDirectory)
    {
    }

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "")
        : this(csharpCompilation, syntaxTrees, [], rootNamespace, projectDirectory)
    {
    }

    public AkburaCompilation(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        string rootNamespace = "",
        string projectDirectory = "",
        AkburaCompilation? previousCompilation = null)
        : this(
            csharpCompilation,
            new SyntaxAndDeclarationManager(
                syntaxTrees,
                akcssSyntaxTrees,
                previousCompilation?._syntaxAndDeclarations),
            rootNamespace,
            projectDirectory,
            previousCompilation)
    {
    }

    private AkburaCompilation(
        CSharpCompilation csharpCompilation,
        SyntaxAndDeclarationManager syntaxAndDeclarations,
        string rootNamespace,
        string projectDirectory,
        AkburaCompilation? previousCompilation)
    {
        CSharpCompilation = csharpCompilation ?? throw new ArgumentNullException(nameof(csharpCompilation));
        _syntaxAndDeclarations = syntaxAndDeclarations ?? throw new ArgumentNullException(nameof(syntaxAndDeclarations));
        RootNamespace = rootNamespace ?? string.Empty;
        ProjectDirectory = projectDirectory ?? string.Empty;
        PreviousCompilation = previousCompilation;
    }

    public CSharpCompilation CSharpCompilation { get; }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees => _syntaxAndDeclarations.SyntaxTrees;

    public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees => _syntaxAndDeclarations.AkcssSyntaxTrees;

    public string RootNamespace { get; }

    public string ProjectDirectory { get; }

    public AkburaCompilation? PreviousCompilation { get; }

    internal SyntaxAndDeclarationManager SyntaxAndDeclarations => _syntaxAndDeclarations;

    public AkburaDeclarationTable DeclarationTable => _syntaxAndDeclarations.DeclarationTable;

    public AkburaCompilation WithSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxTrees([.. syntaxTrees]);
    }

    public AkburaCompilation WithSyntaxTrees(ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        var syntaxAndDeclarations = _syntaxAndDeclarations.WithSyntaxTrees(syntaxTrees);
        if (ReferenceEquals(_syntaxAndDeclarations, syntaxAndDeclarations))
        {
            return this;
        }

        return new AkburaCompilation(
            CSharpCompilation,
            syntaxAndDeclarations,
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
        var syntaxAndDeclarations = _syntaxAndDeclarations.WithAkcssSyntaxTrees(akcssSyntaxTrees);
        if (ReferenceEquals(_syntaxAndDeclarations, syntaxAndDeclarations))
        {
            return this;
        }

        return new AkburaCompilation(
            CSharpCompilation,
            syntaxAndDeclarations,
            RootNamespace,
            ProjectDirectory,
            previousCompilation: this);
    }

    public AkburaCompilation AddSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(_syntaxAndDeclarations.AddSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation RemoveSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(_syntaxAndDeclarations.RemoveSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation ReplaceSyntaxTree(
        AkburaSyntaxTree oldTree,
        AkburaSyntaxTree newTree)
    {
        return WithSyntaxAndDeclarations(_syntaxAndDeclarations.ReplaceSyntaxTree(oldTree, newTree));
    }

    public AkburaCompilation AddAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(_syntaxAndDeclarations.AddAkcssSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation RemoveAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(_syntaxAndDeclarations.RemoveAkcssSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation ReplaceAkcssSyntaxTree(
        AkcssSyntaxTree oldTree,
        AkcssSyntaxTree newTree)
    {
        return WithSyntaxAndDeclarations(_syntaxAndDeclarations.ReplaceAkcssSyntaxTree(oldTree, newTree));
    }

    public AkburaCompilation WithCSharpCompilation(CSharpCompilation csharpCompilation)
    {
        if (ReferenceEquals(CSharpCompilation, csharpCompilation))
        {
            return this;
        }

        return new AkburaCompilation(
            csharpCompilation,
            _syntaxAndDeclarations,
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

        return _semanticModels.GetOrAdd(
            syntaxTree,
            tree => new AkburaSemanticModel(this, tree));
    }

    private AkburaCompilation WithSyntaxAndDeclarations(
        SyntaxAndDeclarationManager syntaxAndDeclarations)
    {
        return ReferenceEquals(_syntaxAndDeclarations, syntaxAndDeclarations)
            ? this
            : new AkburaCompilation(
                CSharpCompilation,
                syntaxAndDeclarations,
                RootNamespace,
                ProjectDirectory,
                previousCompilation: this);
    }
}
