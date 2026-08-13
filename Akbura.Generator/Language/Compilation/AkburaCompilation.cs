using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal sealed partial class AkburaCompilation
{
    private readonly ConcurrentDictionary<AkburaSyntaxTree, AkburaSemanticModel> _semanticModels = new();
    private readonly SyntaxAndDeclarationManager _syntaxAndDeclarations;
    private readonly ReferenceManager _referenceManager;
    private ImmutableArray<UsingDirectiveSyntax> _lazyGlobalAkburaUsingDirectives;
    private ImmutableArray<AkcssUsingDirectiveSyntax> _lazyGlobalAkcssUsingDirectives;
    private ImmutableArray<CSharp.UsingDirectiveSyntax> _lazyGlobalCSharpUsingDirectives;
    private CSharpCompilation? _lazyCSharpProbeCompilation;

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
        AkburaCompilation? reuseFrom = null,
        ImmutableArray<AkburaCompilationReference>
            compilationReferences = default)
    : this(
        csharpCompilation,
        new SyntaxAndDeclarationManager(
            syntaxTrees.IsDefault
                ? ImmutableArray<AkburaSyntaxTree>.Empty
                : syntaxTrees,
            akcssSyntaxTrees.IsDefault
                ? ImmutableArray<AkcssSyntaxTree>.Empty
                : akcssSyntaxTrees,
            reuseFrom?._syntaxAndDeclarations),
        ReferenceManager.Create(
            csharpCompilation,
            compilationReferences.IsDefault
                ? reuseFrom?._referenceManager
                        .CompilationReferences ??
                    ImmutableArray<
                        AkburaCompilationReference>.Empty
                : compilationReferences,
            reuseFrom?.CSharpCompilation,
            reuseFrom?._referenceManager),
        rootNamespace,
        projectDirectory)
    {
    }

    private AkburaCompilation(
        CSharpCompilation csharpCompilation,
        SyntaxAndDeclarationManager syntaxAndDeclarations,
        ReferenceManager referenceManager,
        string rootNamespace,
        string projectDirectory)
    {
        CSharpCompilation = csharpCompilation ??
            throw new ArgumentNullException(
                nameof(csharpCompilation));

        _syntaxAndDeclarations = syntaxAndDeclarations ??
            throw new ArgumentNullException(
                nameof(syntaxAndDeclarations));

        _referenceManager = referenceManager ??
            throw new ArgumentNullException(
                nameof(referenceManager));

        RootNamespace = rootNamespace ?? string.Empty;
        ProjectDirectory = projectDirectory ?? string.Empty;
    }
    public CSharpCompilation CSharpCompilation { get; }

    internal CSharpCompilation CSharpProbeCompilation
    {
        get
        {
            var compilation = Volatile.Read(
                ref _lazyCSharpProbeCompilation);
            if (compilation != null)
            {
                return compilation;
            }

            compilation =
                AkburaComponentProbeCompilationBuilder.Build(
                    CSharpCompilation,
                    SyntaxTrees,
                    RootNamespace,
                    ProjectDirectory);
            return Interlocked.CompareExchange(
                       ref _lazyCSharpProbeCompilation,
                       compilation,
                       comparand: null) ??
                   compilation;
        }
    }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees => _syntaxAndDeclarations.SyntaxTrees;

    public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees => _syntaxAndDeclarations.AkcssSyntaxTrees;

    internal ImmutableArray<AkburaReferencedModule> ReferencedModules => _referenceManager.Modules;

    internal ImmutableArray<AkburaCompilationReference> CompilationReferences =>
        _referenceManager.CompilationReferences;

    public string RootNamespace { get; }

    public string ProjectDirectory { get; }

    internal SyntaxAndDeclarationManager SyntaxAndDeclarations => _syntaxAndDeclarations;

    public DeclarationTable DeclarationTable => _syntaxAndDeclarations.DeclarationTable;

    internal ImmutableArray<UsingDirectiveSyntax> GlobalAkburaUsingDirectives
    {
        get
        {
            if (_lazyGlobalAkburaUsingDirectives.IsDefault)
            {
                ImmutableInterlocked.InterlockedInitialize(
                    ref _lazyGlobalAkburaUsingDirectives,
                    CreateGlobalAkburaUsingDirectives());
            }

            return _lazyGlobalAkburaUsingDirectives;
        }
    }

    internal ImmutableArray<AkcssUsingDirectiveSyntax> GlobalAkcssUsingDirectives
    {
        get
        {
            if (_lazyGlobalAkcssUsingDirectives.IsDefault)
            {
                ImmutableInterlocked.InterlockedInitialize(
                    ref _lazyGlobalAkcssUsingDirectives,
                    CreateGlobalAkcssUsingDirectives());
            }

            return _lazyGlobalAkcssUsingDirectives;
        }
    }

    internal ImmutableArray<CSharp.UsingDirectiveSyntax> GlobalCSharpUsingDirectives
    {
        get
        {
            if (_lazyGlobalCSharpUsingDirectives.IsDefault)
            {
                ImmutableInterlocked.InterlockedInitialize(
                    ref _lazyGlobalCSharpUsingDirectives,
                    CreateGlobalCSharpUsingDirectives());
            }

            return _lazyGlobalCSharpUsingDirectives;
        }
    }

    public AkburaCompilation WithSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxTrees(syntaxTrees.ToImmutableArray());
    }

    public AkburaCompilation WithSyntaxTrees(ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        return WithSyntaxAndDeclarations(
            _syntaxAndDeclarations.WithSyntaxTrees(syntaxTrees));
    }

    private AkburaCompilation WithSyntaxAndDeclarations(SyntaxAndDeclarationManager syntaxAndDeclarations)
    {
        return ReferenceEquals(_syntaxAndDeclarations, syntaxAndDeclarations)
                ? this
                : new AkburaCompilation(
                    CSharpCompilation,
                    syntaxAndDeclarations,
                    _referenceManager,
                    RootNamespace,
                    ProjectDirectory);
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

    public AkburaCompilation ReplaceAkcssSyntaxTree(AkcssSyntaxTree oldTree, AkcssSyntaxTree newTree)
    {
        return WithSyntaxAndDeclarations(_syntaxAndDeclarations.ReplaceAkcssSyntaxTree(oldTree, newTree));
    }

    public AkburaCompilationReference ToReference()
    {
        return new AkburaCompilationReference(this);
    }

    public AkburaCompilation WithCompilationReferences(IEnumerable<AkburaCompilationReference> references)
    {
        if (references == null)
        {
            throw new ArgumentNullException(nameof(references));
        }

        return WithCompilationReferences([.. references]);
    }

    public AkburaCompilation WithCompilationReferences(ImmutableArray<AkburaCompilationReference> references)
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
                ProjectDirectory);
    }

    public AkburaCompilation WithCSharpCompilation(CSharpCompilation csharpCompilation)
    {
        if (ReferenceEquals(CSharpCompilation, csharpCompilation))
        {
            return this;
        }

        var referenceManager =
            ReferenceManager.Create(
                csharpCompilation,
                _referenceManager.CompilationReferences,
                CSharpCompilation,
                _referenceManager);

        return new AkburaCompilation(
            csharpCompilation,
            _syntaxAndDeclarations,
            referenceManager,
            RootNamespace,
            ProjectDirectory);
    }

    public AkburaSemanticModel GetSemanticModel(AkburaSyntaxTree syntaxTree)
    {
        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }

        if (SyntaxTrees.Contains(syntaxTree) ||
            syntaxTree is AkcssSyntaxTree akcssSyntaxTree &&
            AkcssSyntaxTrees.Contains(akcssSyntaxTree))
        {
            return _semanticModels.GetOrAdd(
                syntaxTree,
                tree => new SyntaxTreeSemanticModel(this, tree));
        }

        if (_referenceManager.TryGetSemanticModel(syntaxTree, out var referencedModel))
        {
            return referencedModel;
        }

        if (!_referenceManager.ContainsComponentSyntaxTree(syntaxTree) &&
            (syntaxTree is not AkcssSyntaxTree referencedAkcssTree ||
             !_referenceManager.ContainsAkcssSyntaxTree(referencedAkcssTree)))
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

    internal bool ContainsAkcssSyntaxTree(AkcssSyntaxTree syntaxTree)
    {
        return AkcssSyntaxTrees.Contains(syntaxTree) ||
            _referenceManager.ContainsAkcssSyntaxTree(syntaxTree);
    }

    internal bool TryGetSemanticModel(
        AkburaSyntaxTree syntaxTree,
        out AkburaSemanticModel semanticModel)
    {
        if (SyntaxTrees.Contains(syntaxTree) ||
            syntaxTree is AkcssSyntaxTree localAkcssTree &&
            AkcssSyntaxTrees.Contains(localAkcssTree))
        {
            semanticModel = GetSemanticModel(syntaxTree);
            return true;
        }

        if (_referenceManager.TryGetSemanticModel(
                syntaxTree,
                out semanticModel))
        {
            return true;
        }

        if (!_referenceManager.ContainsComponentSyntaxTree(syntaxTree) &&
            (syntaxTree is not AkcssSyntaxTree referencedAkcssTree ||
             !_referenceManager.ContainsAkcssSyntaxTree(referencedAkcssTree)))
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

    internal IEnumerable<IAkburaComponentSymbol> GetReferencedComponentSymbols()
    {
        foreach (var symbol in _referenceManager.GetComponentSymbols())
        {
            yield return symbol;
        }
    }

    internal ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(string logicalName)
    {
        var localMatches = GetLocalAkcssSyntaxTreesByLogicalName(logicalName);
        return localMatches.Length > 0
            ? localMatches
            : _referenceManager.GetAkcssSyntaxTreesByLogicalName(logicalName);
    }

    internal ImmutableArray<AkcssSyntaxTree> GetLocalAkcssSyntaxTreesByLogicalName(
        string logicalName)
    {
        return AkcssSyntaxTrees
            .Where(tree => string.Equals(tree.LogicalName, logicalName, StringComparison.Ordinal))
            .ToImmutableArray();
    }

    internal ImmutableArray<IAkcssModuleSymbol> GetAkcssModuleSymbolsByLogicalName(
        string logicalName)
    {
        return _referenceManager.GetAkcssModuleSymbolsByLogicalName(logicalName);
    }

    internal ImmutableArray<IAkcssModuleSymbol> GetExportedAkcssModuleSymbolsByLogicalName(
        string logicalName)
    {
        using var localModules = ImmutableArrayBuilder<IAkcssModuleSymbol>.Rent();
        foreach (var syntaxTree in AkcssSyntaxTrees)
        {
            if (!string.Equals(
                    syntaxTree.LogicalName,
                    logicalName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var semanticModel = GetSemanticModel(syntaxTree);
            if (semanticModel.GetDeclaredSymbol(syntaxTree.GetRoot()) is IAkcssModuleSymbol module)
            {
                localModules.Add(module);
            }
        }

        var local = localModules.ToImmutable();
        return local.Length > 0
            ? local
            : GetAkcssModuleSymbolsByLogicalName(logicalName);
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

    private ImmutableArray<UsingDirectiveSyntax> CreateGlobalAkburaUsingDirectives()
    {
        using var builder =
            ImmutableArrayBuilder<UsingDirectiveSyntax>.Rent();
        foreach (var syntaxTree in SyntaxTrees)
        {
            var isGlobalUsingsFile =
                GlobalUsings.IsComponentFile(syntaxTree);
            foreach (var member in syntaxTree.GetRoot().Members)
            {
                if (member is UsingDirectiveSyntax usingDirective &&
                    (isGlobalUsingsFile ||
                     usingDirective.GlobalKeyword.RawKind != 0))
                {
                    builder.Add(usingDirective);
                }
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<AkcssUsingDirectiveSyntax> CreateGlobalAkcssUsingDirectives()
    {
        using var builder =
            ImmutableArrayBuilder<AkcssUsingDirectiveSyntax>.Rent();
        foreach (var syntaxTree in AkcssSyntaxTrees)
        {
            if (!GlobalUsings.IsAkcssFile(syntaxTree))
            {
                continue;
            }

            foreach (var member in syntaxTree.GetRoot().Members)
            {
                if (member is AkcssUsingDirectiveSyntax usingDirective)
                {
                    builder.Add(usingDirective);
                }
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<CSharp.UsingDirectiveSyntax> CreateGlobalCSharpUsingDirectives()
    {
        using var builder =
            ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        foreach (var syntaxTree in CSharpCompilation.SyntaxTrees)
        {
            if (syntaxTree.GetRoot() is not CSharp.CompilationUnitSyntax root)
            {
                continue;
            }

            foreach (var usingDirective in root.Usings)
            {
                if (usingDirective.GlobalKeyword.RawKind != 0)
                {
                    builder.Add(usingDirective);
                }
            }
        }

        return builder.ToImmutable();
    }
}
