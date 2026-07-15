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
    private readonly ImmutableArray<AkburaSyntaxTree> _syntaxTrees;
    private readonly ImmutableArray<AkcssSyntaxTree> _akcssSyntaxTrees;
    private readonly ImmutableArray<AkburaReferencedModule> _referencedModules;

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
            syntaxTrees.IsDefault ? ImmutableArray<AkburaSyntaxTree>.Empty : syntaxTrees,
            akcssSyntaxTrees.IsDefault ? ImmutableArray<AkcssSyntaxTree>.Empty : akcssSyntaxTrees,
            GetReferencedModules(csharpCompilation, previousCompilation),
            rootNamespace,
            projectDirectory,
            previousCompilation)
    {
    }

    private AkburaCompilation(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        ImmutableArray<AkburaReferencedModule> referencedModules,
        string rootNamespace,
        string projectDirectory,
        AkburaCompilation? previousCompilation)
        : this(
            csharpCompilation,
            syntaxTrees,
            akcssSyntaxTrees,
            referencedModules,
            new SyntaxAndDeclarationManager(
                syntaxTrees.AddRange(GetReferencedComponentSyntaxTrees(referencedModules)),
                akcssSyntaxTrees.AddRange(GetReferencedAkcssSyntaxTrees(referencedModules)),
                previousCompilation?._syntaxAndDeclarations),
            rootNamespace,
            projectDirectory,
            previousCompilation)
    {
    }

    private AkburaCompilation(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        ImmutableArray<AkburaReferencedModule> referencedModules,
        SyntaxAndDeclarationManager syntaxAndDeclarations,
        string rootNamespace,
        string projectDirectory,
        AkburaCompilation? previousCompilation)
    {
        CSharpCompilation = csharpCompilation ?? throw new ArgumentNullException(nameof(csharpCompilation));
        _syntaxTrees = syntaxTrees;
        _akcssSyntaxTrees = akcssSyntaxTrees;
        _referencedModules = referencedModules;
        _syntaxAndDeclarations = syntaxAndDeclarations ?? throw new ArgumentNullException(nameof(syntaxAndDeclarations));
        RootNamespace = rootNamespace ?? string.Empty;
        ProjectDirectory = projectDirectory ?? string.Empty;
        PreviousCompilation = previousCompilation;
    }

    public CSharpCompilation CSharpCompilation { get; }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees => _syntaxTrees;

    public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees => _akcssSyntaxTrees;

    internal ImmutableArray<AkburaSyntaxTree> AllSyntaxTrees => _syntaxAndDeclarations.SyntaxTrees;

    internal ImmutableArray<AkcssSyntaxTree> AllAkcssSyntaxTrees => _syntaxAndDeclarations.AkcssSyntaxTrees;

    internal ImmutableArray<AkburaReferencedModule> ReferencedModules => _referencedModules;

    public string RootNamespace { get; }

    public string ProjectDirectory { get; }

    public AkburaCompilation? PreviousCompilation { get; }

    internal SyntaxAndDeclarationManager SyntaxAndDeclarations => _syntaxAndDeclarations;

    public DeclarationTable DeclarationTable => _syntaxAndDeclarations.DeclarationTable;

    public AkburaCompilation WithSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxTrees([.. syntaxTrees]);
    }

    public AkburaCompilation WithSyntaxTrees(ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        syntaxTrees = syntaxTrees.IsDefault ? [] : syntaxTrees;
        if (_syntaxTrees.SequenceEqual(syntaxTrees))
        {
            return this;
        }

        return new AkburaCompilation(
            CSharpCompilation,
            syntaxTrees,
            _akcssSyntaxTrees,
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
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault ? [] : akcssSyntaxTrees;
        if (_akcssSyntaxTrees.SequenceEqual(akcssSyntaxTrees))
        {
            return this;
        }

        return new AkburaCompilation(
            CSharpCompilation,
            _syntaxTrees,
            akcssSyntaxTrees,
            RootNamespace,
            ProjectDirectory,
            previousCompilation: this);
    }

    public AkburaCompilation AddSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        return WithSyntaxTrees(_syntaxTrees.AddRange(syntaxTrees));
    }

    public AkburaCompilation RemoveSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        var removeSet = syntaxTrees.ToImmutableHashSet();
        return removeSet.Count == 0
            ? this
            : WithSyntaxTrees(_syntaxTrees.RemoveAll(removeSet.Contains));
    }

    public AkburaCompilation ReplaceSyntaxTree(
        AkburaSyntaxTree oldTree,
        AkburaSyntaxTree newTree)
    {
        if (oldTree == null)
        {
            throw new ArgumentNullException(nameof(oldTree));
        }

        if (newTree == null)
        {
            throw new ArgumentNullException(nameof(newTree));
        }

        var index = _syntaxTrees.IndexOf(oldTree);
        if (index < 0)
        {
            throw new ArgumentException("Syntax tree is not part of this compilation.", nameof(oldTree));
        }

        return ReferenceEquals(oldTree, newTree)
            ? this
            : WithSyntaxTrees(_syntaxTrees.SetItem(index, newTree));
    }

    public AkburaCompilation AddAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        return WithAkcssSyntaxTrees(_akcssSyntaxTrees.AddRange(syntaxTrees));
    }

    public AkburaCompilation RemoveAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        var removeSet = syntaxTrees.ToImmutableHashSet();
        return removeSet.Count == 0
            ? this
            : WithAkcssSyntaxTrees(_akcssSyntaxTrees.RemoveAll(removeSet.Contains));
    }

    public AkburaCompilation ReplaceAkcssSyntaxTree(
        AkcssSyntaxTree oldTree,
        AkcssSyntaxTree newTree)
    {
        if (oldTree == null)
        {
            throw new ArgumentNullException(nameof(oldTree));
        }

        if (newTree == null)
        {
            throw new ArgumentNullException(nameof(newTree));
        }

        var index = _akcssSyntaxTrees.IndexOf(oldTree);
        if (index < 0)
        {
            throw new ArgumentException("AKCSS syntax tree is not part of this compilation.", nameof(oldTree));
        }

        return ReferenceEquals(oldTree, newTree)
            ? this
            : WithAkcssSyntaxTrees(_akcssSyntaxTrees.SetItem(index, newTree));
    }

    public AkburaCompilation WithCSharpCompilation(CSharpCompilation csharpCompilation)
    {
        if (ReferenceEquals(CSharpCompilation, csharpCompilation))
        {
            return this;
        }

        return new AkburaCompilation(
            csharpCompilation,
            _syntaxTrees,
            _akcssSyntaxTrees,
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

        if (!AllSyntaxTrees.Contains(syntaxTree))
        {
            throw new ArgumentException("Syntax tree is not part of this compilation.", nameof(syntaxTree));
        }

        return _semanticModels.GetOrAdd(
            syntaxTree,
            tree => new SyntaxTreeSemanticModel(this, tree));
    }

    internal ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(string logicalName)
    {
        var localMatches = _akcssSyntaxTrees
            .Where(tree => string.Equals(tree.LogicalName, logicalName, StringComparison.Ordinal))
            .ToImmutableArray();
        return localMatches.Length > 0
            ? localMatches
            : AllAkcssSyntaxTrees
                .Where(tree => string.Equals(tree.LogicalName, logicalName, StringComparison.Ordinal))
                .ToImmutableArray();
    }

    internal bool TryGetReferencedComponentDeclaration(
        AkburaSyntaxTree syntaxTree,
        out AkburaModuleDeclaration declaration)
    {
        foreach (var module in _referencedModules)
        {
            if (module.TryGetComponentDeclaration(syntaxTree, out declaration))
            {
                return true;
            }
        }

        declaration = null!;
        return false;
    }

    private static ImmutableArray<AkburaReferencedModule> GetReferencedModules(
        CSharpCompilation csharpCompilation,
        AkburaCompilation? previousCompilation)
    {
        return previousCompilation != null &&
               ReferenceEquals(csharpCompilation, previousCompilation.CSharpCompilation)
            ? previousCompilation._referencedModules
            : AkburaReferencedModule.Load(csharpCompilation);
    }

    private static ImmutableArray<AkburaSyntaxTree> GetReferencedComponentSyntaxTrees(
        ImmutableArray<AkburaReferencedModule> modules)
    {
        var builder = ImmutableArray.CreateBuilder<AkburaSyntaxTree>();
        foreach (var module in modules)
        {
            builder.AddRange(module.ComponentSyntaxTrees);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<AkcssSyntaxTree> GetReferencedAkcssSyntaxTrees(
        ImmutableArray<AkburaReferencedModule> modules)
    {
        var builder = ImmutableArray.CreateBuilder<AkcssSyntaxTree>();
        foreach (var module in modules)
        {
            builder.AddRange(module.AkcssSyntaxTrees);
        }

        return builder.ToImmutable();
    }
}
