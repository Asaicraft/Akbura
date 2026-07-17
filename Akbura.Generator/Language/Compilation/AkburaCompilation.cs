using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language;

internal sealed partial class AkburaCompilation
{
    private readonly ConcurrentDictionary<AkburaSyntaxTree, AkburaSemanticModel> _semanticModels = new();
    private readonly SyntaxAndDeclarationManager _syntaxAndDeclarations;
    private readonly ReferenceManager _referenceManager;

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
        AkburaCompilation? previousCompilation = null,
        ImmutableArray<AkburaCompilationReference> compilationReferences = default)
        : this(
            csharpCompilation,
            new SyntaxAndDeclarationManager(
                syntaxTrees.IsDefault ? ImmutableArray<AkburaSyntaxTree>.Empty : syntaxTrees,
                akcssSyntaxTrees.IsDefault ? ImmutableArray<AkcssSyntaxTree>.Empty : akcssSyntaxTrees,
                previousCompilation?._syntaxAndDeclarations),
            ReferenceManager.Create(
                csharpCompilation,
                compilationReferences.IsDefault
                    ? previousCompilation?._referenceManager.CompilationReferences ??
                      ImmutableArray<AkburaCompilationReference>.Empty
                    : compilationReferences,
                previousCompilation?.CSharpCompilation,
                previousCompilation?._referenceManager),
            rootNamespace,
            projectDirectory,
            previousCompilation)
    {
    }

    private AkburaCompilation(
        CSharpCompilation csharpCompilation,
        SyntaxAndDeclarationManager syntaxAndDeclarations,
        ReferenceManager referenceManager,
        string rootNamespace,
        string projectDirectory,
        AkburaCompilation? previousCompilation)
    {
        CSharpCompilation = csharpCompilation ?? throw new ArgumentNullException(nameof(csharpCompilation));
        _syntaxAndDeclarations = syntaxAndDeclarations ?? throw new ArgumentNullException(nameof(syntaxAndDeclarations));
        _referenceManager = referenceManager ?? throw new ArgumentNullException(nameof(referenceManager));
        RootNamespace = rootNamespace ?? string.Empty;
        ProjectDirectory = projectDirectory ?? string.Empty;
        PreviousCompilation = previousCompilation;
    }

    public CSharpCompilation CSharpCompilation { get; }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees => _syntaxAndDeclarations.SyntaxTrees;

    public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees => _syntaxAndDeclarations.AkcssSyntaxTrees;

    internal ImmutableArray<AkburaReferencedModule> ReferencedModules => _referenceManager.Modules;

    internal ImmutableArray<AkburaCompilationReference> CompilationReferences =>
        _referenceManager.CompilationReferences;

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
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.WithSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation WithAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        return WithAkcssSyntaxTrees(akcssSyntaxTrees.ToImmutableArray());
    }

    public AkburaCompilation WithAkcssSyntaxTrees(ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.WithAkcssSyntaxTrees(akcssSyntaxTrees));
    }

    public AkburaCompilation AddSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.AddSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation RemoveSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.RemoveSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation ReplaceSyntaxTree(
        AkburaSyntaxTree oldTree,
        AkburaSyntaxTree newTree)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.ReplaceSyntaxTree(oldTree, newTree));
    }

    public AkburaCompilation AddAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.AddAkcssSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation RemoveAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.RemoveAkcssSyntaxTrees(syntaxTrees));
    }

    public AkburaCompilation ReplaceAkcssSyntaxTree(
        AkcssSyntaxTree oldTree,
        AkcssSyntaxTree newTree)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.ReplaceAkcssSyntaxTree(oldTree, newTree));
    }

    public AkburaCompilationReference ToReference()
    {
        return new AkburaCompilationReference(this);
    }

    public AkburaCompilation WithCompilationReferences(
        IEnumerable<AkburaCompilationReference> references)
    {
        if (references == null)
        {
            throw new ArgumentNullException(nameof(references));
        }

        return WithCompilationReferences([.. references]);
    }

    public AkburaCompilation WithCompilationReferences(
        ImmutableArray<AkburaCompilationReference> references)
    {
        references = references.IsDefault
            ? ImmutableArray<AkburaCompilationReference>.Empty
            : references;
        var referenceManager = ReferenceManager.Create(
            CSharpCompilation,
            references,
            CSharpCompilation,
            _referenceManager);
        return ReferenceEquals(referenceManager, _referenceManager)
            ? this
            : new AkburaCompilation(
                CSharpCompilation,
                _syntaxAndDeclarations,
                referenceManager,
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
            _syntaxAndDeclarations,
            ReferenceManager.Create(
                csharpCompilation,
                _referenceManager.CompilationReferences,
                CSharpCompilation,
                _referenceManager),
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

        if (SyntaxTrees.Contains(syntaxTree))
        {
            return _semanticModels.GetOrAdd(
                syntaxTree,
                tree => new SyntaxTreeSemanticModel(this, tree));
        }

        if (_referenceManager.TryGetSemanticModel(syntaxTree, out var referencedModel))
        {
            return referencedModel;
        }

        if (!_referenceManager.ContainsComponentSyntaxTree(syntaxTree))
        {
            throw new ArgumentException("Syntax tree is not part of this compilation.", nameof(syntaxTree));
        }

        return _semanticModels.GetOrAdd(
            syntaxTree,
            tree => new SyntaxTreeSemanticModel(this, tree));
    }

    internal bool ContainsComponentSyntaxTree(AkburaSyntaxTree syntaxTree)
    {
        return SyntaxTrees.Contains(syntaxTree) ||
            _referenceManager.ContainsComponentSyntaxTree(syntaxTree);
    }

    internal bool TryGetSemanticModel(
        AkburaSyntaxTree syntaxTree,
        out AkburaSemanticModel semanticModel)
    {
        if (!ContainsComponentSyntaxTree(syntaxTree))
        {
            semanticModel = null!;
            return false;
        }

        semanticModel = GetSemanticModel(syntaxTree);
        return true;
    }

    internal IEnumerable<IAkburaComponentSymbol> GetReferencedComponentSymbols(string metadataName)
    {
        foreach (var symbol in _referenceManager.GetComponentSymbols(metadataName))
        {
            yield return symbol;
        }
    }

    internal ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(string logicalName)
    {
        var localMatches = AkcssSyntaxTrees
            .Where(tree => string.Equals(tree.LogicalName, logicalName, StringComparison.Ordinal))
            .ToImmutableArray();
        return localMatches.Length > 0
            ? localMatches
            : _referenceManager.GetAkcssSyntaxTreesByLogicalName(logicalName);
    }

    internal bool TryGetReferencedComponentDeclaration(
        AkburaSyntaxTree syntaxTree,
        out AkburaModuleDeclaration declaration)
    {
        return _referenceManager.TryGetComponentDeclaration(syntaxTree, out declaration);
    }

    internal bool TryGetDeclaration(
        AkburaSyntax syntax,
        out Declaration declaration)
    {
        return DeclarationTable.TryGetDeclaration(syntax, out declaration) ||
               _referenceManager.TryGetDeclaration(syntax, out declaration);
    }

    internal bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<Declaration> path)
    {
        return DeclarationTable.TryGetDeclarationPath(syntax, out path) ||
               _referenceManager.TryGetDeclarationPath(syntax, out path);
    }

    internal bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<Declaration> path)
    {
        return DeclarationTable.TryGetDeclarationPath(syntax, position, out path) ||
               _referenceManager.TryGetDeclarationPath(syntax, position, out path);
    }

    private AkburaCompilation WithSyntaxAndDeclarations(
        SyntaxAndDeclarationManager syntaxAndDeclarations)
    {
        return ReferenceEquals(_syntaxAndDeclarations, syntaxAndDeclarations)
            ? this
            : new AkburaCompilation(
                CSharpCompilation,
                syntaxAndDeclarations,
                _referenceManager,
                RootNamespace,
                ProjectDirectory,
                previousCompilation: this);
    }
}
