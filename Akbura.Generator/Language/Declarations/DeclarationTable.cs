// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Pools;
using Akbura.Collections;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.Language;

internal sealed partial class DeclarationTable
{
    public static readonly DeclarationTable Empty = new(
        allOlderRootDeclarations: ImmutableSetWithInsertionOrder<Lazy<RootSingleNamespaceDeclaration>>.Empty,
        latestLazyRootDeclaration: null,
        cache: null);

    private readonly ImmutableSetWithInsertionOrder<Lazy<RootSingleNamespaceDeclaration>> _allOlderRootDeclarations;
    private readonly Lazy<RootSingleNamespaceDeclaration> _latestLazyRootDeclaration;
    private readonly Cache _cache;
    private readonly ImmutableArray<Declaration> _components;
    private readonly ImmutableArray<Declaration> _akcssModules;

    private MergedNamespaceDeclaration _mergedRoot;
    private ImmutableArray<Declaration> _rootDeclarations;
    private ImmutableArray<Declaration> _roots;
    private ICollection<string> _declarationNames;
    private Dictionary<AkburaSyntax, ImmutableArray<Declaration>> _pathsBySyntax;

    private DeclarationTable(
        ImmutableSetWithInsertionOrder<Lazy<RootSingleNamespaceDeclaration>> allOlderRootDeclarations,
        Lazy<RootSingleNamespaceDeclaration> latestLazyRootDeclaration,
        Cache cache)
        : this(
            allOlderRootDeclarations,
            latestLazyRootDeclaration,
            cache,
            components: default,
            akcssModules: default)
    {
    }

    private DeclarationTable(
        ImmutableSetWithInsertionOrder<Lazy<RootSingleNamespaceDeclaration>> allOlderRootDeclarations,
        Lazy<RootSingleNamespaceDeclaration> latestLazyRootDeclaration,
        Cache cache,
        ImmutableArray<Declaration> components,
        ImmutableArray<Declaration> akcssModules)
    {
        _allOlderRootDeclarations = allOlderRootDeclarations ?? ImmutableSetWithInsertionOrder<Lazy<RootSingleNamespaceDeclaration>>.Empty;
        _latestLazyRootDeclaration = latestLazyRootDeclaration;
        _cache = cache ?? new Cache(this);
        _components = components.IsDefault
            ? ImmutableArray<Declaration>.Empty
            : components;
        _akcssModules = akcssModules.IsDefault
            ? ImmutableArray<Declaration>.Empty
            : akcssModules;
    }

    public Builder ToBuilder()
    {
        return Builder.GetInstance(this);
    }

    public ImmutableArray<Declaration> RootDeclarations
    {
        get
        {
            if (_rootDeclarations.IsDefault)
            {
                ImmutableInterlocked.InterlockedInitialize(
                    ref _rootDeclarations,
                    GetRootDeclarations());
            }

            return _rootDeclarations;
        }
    }

    public ImmutableArray<Declaration> Roots
    {
        get
        {
            if (_roots.IsDefault)
            {
                ImmutableInterlocked.InterlockedInitialize(
                    ref _roots,
                    Components.AddRange(AkcssModules));
            }

            return _roots;
        }
    }

    public ImmutableArray<Declaration> Components => _components;

    public ImmutableArray<Declaration> AkcssModules => _akcssModules;

    public MergedNamespaceDeclaration MergedRoot
    {
        get
        {
            if (_mergedRoot is null)
            {
                Interlocked.CompareExchange(
                    ref _mergedRoot,
                    GetMergedRoot(),
                    comparand: null);
            }

            return _mergedRoot;
        }
    }

    public ICollection<string> DeclarationNames
    {
        get
        {
            if (_declarationNames is null)
            {
                Interlocked.CompareExchange(
                    ref _declarationNames,
                    GetMergedDeclarationNames(),
                    comparand: null);
            }

            return _declarationNames;
        }
    }

    public static DeclarationTable Create(AkburaCompilation compilation)
    {
        return Create(
            compilation.SyntaxTrees,
            compilation.AkcssSyntaxTrees,
            compilation.PreviousCompilation?.DeclarationTable);
    }

    public static DeclarationTable Create(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        DeclarationTable previous)
    {
        syntaxTrees = syntaxTrees.IsDefault
            ? []
            : syntaxTrees;
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? []
            : akcssSyntaxTrees;

        var componentsBuilder = ImmutableArray.CreateBuilder<Declaration>(syntaxTrees.Length);
        foreach (var tree in syntaxTrees)
        {
            componentsBuilder.Add(TryReuseComponent(previous, tree, out var declaration)
                ? declaration
                : DeclarationTreeBuilder.ForSyntaxDeclaration(tree));
        }

        var akcssModulesBuilder = ImmutableArray.CreateBuilder<Declaration>(akcssSyntaxTrees.Length);
        foreach (var tree in akcssSyntaxTrees)
        {
            akcssModulesBuilder.Add(TryReuseAkcssModule(previous, tree, out var declaration)
                ? declaration
                : DeclarationTreeBuilder.ForSyntaxDeclaration(tree));
        }

        return Create(
            componentsBuilder.ToImmutable(),
            akcssModulesBuilder.ToImmutable());
    }

    internal static DeclarationTable Create(
        ImmutableArray<Declaration> components,
        ImmutableArray<Declaration> akcssModules)
    {
        return new DeclarationTable(
            ImmutableSetWithInsertionOrder<Lazy<RootSingleNamespaceDeclaration>>.Empty,
            latestLazyRootDeclaration: null,
            cache: null,
            components,
            akcssModules);
    }

    internal DeclarationTable WithSyntaxDeclarations(
        ImmutableArray<Declaration> components,
        ImmutableArray<Declaration> akcssModules)
    {
        return new DeclarationTable(
            _allOlderRootDeclarations,
            _latestLazyRootDeclaration,
            _cache,
            components,
            akcssModules);
    }

    public bool TryGetDeclaration(AkburaSyntax syntax, out Declaration declaration)
    {
        if (TryGetDeclarationPath(syntax, out var path))
        {
            declaration = path[path.Length - 1];
            return true;
        }

        declaration = null!;
        return false;
    }

    public bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<Declaration> path)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        return PathsBySyntax.TryGetValue(syntax, out path);
    }

    public bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<Declaration> path)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        var builder = ArrayBuilder<Declaration>.GetInstance();
        try
        {
            foreach (var root in Roots)
            {
                builder.Clear();
                if (TryBuildDeclarationPath(root, syntax, position, builder))
                {
                    path = builder.ToImmutableAndFree();
                    builder = null;
                    return true;
                }
            }

            path = default;
            return false;
        }
        finally
        {
            builder?.Free();
        }
    }

    private ImmutableArray<Declaration> GetRootDeclarations()
    {
        return MergedRoot.Declarations;
    }

    private MergedNamespaceDeclaration GetMergedRoot()
    {
        var olderRoots = _cache.RootDeclarations;
        if (_latestLazyRootDeclaration == null)
        {
            return _cache.MergedRoot;
        }

        if (olderRoots.IsDefaultOrEmpty)
        {
            return MergedNamespaceDeclaration.Create(
                ImmutableArray.Create<Declaration>(_latestLazyRootDeclaration.Value));
        }

        var builder = ArrayBuilder<Declaration>.GetInstance(olderRoots.Length + 1);
        builder.AddRange(olderRoots);
        builder.Add(_latestLazyRootDeclaration.Value);
        return MergedNamespaceDeclaration.Create(builder.ToImmutableAndFree());
    }

    private ICollection<string> GetMergedDeclarationNames()
    {
        var set = new HashSet<string>(_cache.DeclarationNames, StringComparer.Ordinal);
        if (_latestLazyRootDeclaration != null)
        {
            AddNames(_latestLazyRootDeclaration.Value, set);
        }

        return set;
    }

    private static ISet<string> GetNames(ImmutableArray<Declaration> declarations)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            AddNames(declaration, set);
        }

        return set;
    }

    private static void AddNames(
        Declaration declaration,
        ISet<string> set)
    {
        var stack = new Stack<Declaration>();
        stack.Push(declaration);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == null)
            {
                continue;
            }

            set.Add(current.Name);
            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }
    }

    private Dictionary<AkburaSyntax, ImmutableArray<Declaration>> PathsBySyntax
    {
        get
        {
            if (_pathsBySyntax == null)
            {
                Interlocked.CompareExchange(
                    ref _pathsBySyntax,
                    CreatePathMap(Roots),
                    comparand: null);
            }

            return _pathsBySyntax;
        }
    }

    private static bool TryReuseComponent(
        DeclarationTable previous,
        AkburaSyntaxTree tree,
        out Declaration declaration)
    {
        declaration = null!;
        if (previous == null)
        {
            return false;
        }

        foreach (var candidate in previous.Components)
        {
            if (ReferenceEquals(candidate.SyntaxTree, tree))
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryReuseAkcssModule(
        DeclarationTable previous,
        AkcssSyntaxTree tree,
        out Declaration declaration)
    {
        declaration = null!;
        if (previous == null)
        {
            return false;
        }

        foreach (var candidate in previous.AkcssModules)
        {
            if (ReferenceEquals(candidate.AkcssSyntaxTree, tree))
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    private static Dictionary<AkburaSyntax, ImmutableArray<Declaration>> CreatePathMap(
        ImmutableArray<Declaration> roots)
    {
        var map = new Dictionary<AkburaSyntax, ImmutableArray<Declaration>>();
        var path = ArrayBuilder<Declaration>.GetInstance();
        try
        {
            foreach (var root in roots)
            {
                AddDeclarationPaths(root, path, map);
            }

            return map;
        }
        finally
        {
            path.Free();
        }
    }

    private static void AddDeclarationPaths(
        Declaration declaration,
        ArrayBuilder<Declaration> path,
        Dictionary<AkburaSyntax, ImmutableArray<Declaration>> map)
    {
        var syntax = declaration.Syntax;
        if (syntax == null)
        {
            return;
        }

        path.Add(declaration);
        map[syntax] = path.ToImmutable();

        foreach (var child in declaration.Children)
        {
            AddDeclarationPaths(child, path, map);
        }

        path.RemoveLast();
    }

    private static bool TryBuildDeclarationPath(
        Declaration current,
        AkburaSyntax syntax,
        int position,
        ArrayBuilder<Declaration> path)
    {
        var currentSyntax = current.Syntax;
        if (currentSyntax == null ||
            !SemanticSyntaxIdentity.IsInSameTree(currentSyntax, syntax) ||
            !ContainsPosition(currentSyntax, position))
        {
            return false;
        }

        path.Add(current);
        foreach (var child in current.Children)
        {
            if (TryBuildDeclarationPath(child, syntax, position, path))
            {
                return true;
            }
        }

        return true;
    }

    private static bool ContainsPosition(AkburaSyntax syntax, int position)
    {
        return syntax.FullSpan.Contains(position) ||
               (syntax.FullWidth == 0 && position == syntax.Position);
    }
}
