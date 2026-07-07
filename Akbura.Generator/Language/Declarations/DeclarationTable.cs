// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.Language;

internal sealed partial class DeclarationTable
{
    public static readonly DeclarationTable Empty = new(
        allOlderRootDeclarations: [],
        latestLazyRootDeclaration: null,
        cache: null);

    private readonly ImmutableArray<Lazy<Declaration>> _allOlderRootDeclarations;
    private readonly Lazy<Declaration> _latestLazyRootDeclaration;
    private readonly Cache _cache;

    private MergedNamespaceDeclaration _mergedRoot;
    private ImmutableArray<Declaration> _rootDeclarations;
    private ICollection<string> _declarationNames;

    private DeclarationTable(
        ImmutableArray<Lazy<Declaration>> allOlderRootDeclarations,
        Lazy<Declaration> latestLazyRootDeclaration,
        Cache cache)
    {
        _allOlderRootDeclarations = allOlderRootDeclarations.IsDefault
            ? ImmutableArray<Lazy<Declaration>>.Empty
            : allOlderRootDeclarations;
        _latestLazyRootDeclaration = latestLazyRootDeclaration;
        _cache = cache ?? new Cache(this);
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
            return MergedNamespaceDeclaration.Create([_latestLazyRootDeclaration.Value]);
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
}
