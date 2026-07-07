using Akbura.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using BoxedMemberNames = System.Runtime.CompilerServices.StrongBox<Akbura.Collections.ImmutableSegmentedHashSet<string>>;

namespace Akbura.Language;

internal sealed partial class SyntaxAndDeclarationManager
{
    private readonly DeclarationTable? _previousDeclarationTable;
    private State? _lazyState;

    internal bool HasLazyState => _lazyState != null;

    internal State GetLazyState()
    {
        var state = _lazyState;
        if (state != null)
        {
            return state;
        }

        state = CreateState();
        var existing = Interlocked.CompareExchange(ref _lazyState, state, null);
        return existing ?? state;
    }

    private State CreateState()
    {
        return CreateState(
            _syntaxTrees,
            _akcssSyntaxTrees,
            _previousDeclarationTable);
    }

    private static State CreateState(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        DeclarationTable? previousTable)
    {
        syntaxTrees = syntaxTrees.IsDefault
            ? ImmutableArray<AkburaSyntaxTree>.Empty
            : syntaxTrees;
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? ImmutableArray<AkcssSyntaxTree>.Empty
            : akcssSyntaxTrees;
        var syntaxOrdinalMap = CreateOrdinalMap(syntaxTrees);
        var akcssOrdinalMap = CreateOrdinalMap(akcssSyntaxTrees);
        var previousMemberNames = ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>>.Empty;
        var previousAkcssMemberNames = ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>>.Empty;
        var rootNamespaces = CreateRootNamespaces(syntaxTrees, previousMemberNames);
        var akcssRootNamespaces = CreateAkcssRootNamespaces(akcssSyntaxTrees, previousAkcssMemberNames);
        var syntaxDeclarationTable = DeclarationTable.Create(
            syntaxTrees,
            akcssSyntaxTrees,
            previousTable);
        var declarationTable = CreateRootDeclarationTable(
                syntaxTrees,
                akcssSyntaxTrees,
                rootNamespaces,
                akcssRootNamespaces)
            .WithSyntaxDeclarations(
                syntaxDeclarationTable.Components,
                syntaxDeclarationTable.AkcssModules);
        var lastComputedMemberNames = CreateLastComputedMemberNames(rootNamespaces);
        var lastComputedAkcssMemberNames = CreateLastComputedMemberNames(akcssRootNamespaces);
        return new State(
            syntaxTrees,
            akcssSyntaxTrees,
            syntaxOrdinalMap,
            akcssOrdinalMap,
            rootNamespaces,
            akcssRootNamespaces,
            lastComputedMemberNames,
            lastComputedAkcssMemberNames,
            declarationTable);
    }

    private static State CreateState(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        DeclarationTable declarationTable,
        ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> previousMemberNames,
        ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> previousAkcssMemberNames)
    {
        syntaxTrees = syntaxTrees.IsDefault
            ? ImmutableArray<AkburaSyntaxTree>.Empty
            : syntaxTrees;
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? ImmutableArray<AkcssSyntaxTree>.Empty
            : akcssSyntaxTrees;
        previousMemberNames ??= ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>>.Empty;
        previousAkcssMemberNames ??= ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>>.Empty;

        var rootNamespaces = CreateRootNamespaces(syntaxTrees, previousMemberNames);
        var akcssRootNamespaces = CreateAkcssRootNamespaces(akcssSyntaxTrees, previousAkcssMemberNames);
        var combinedDeclarationTable = CreateRootDeclarationTable(
                syntaxTrees,
                akcssSyntaxTrees,
                rootNamespaces,
                akcssRootNamespaces)
            .WithSyntaxDeclarations(
                declarationTable.Components,
                declarationTable.AkcssModules);
        var lastComputedMemberNames = CreateLastComputedMemberNames(rootNamespaces);
        var lastComputedAkcssMemberNames = CreateLastComputedMemberNames(akcssRootNamespaces);

        return new State(
            syntaxTrees,
            akcssSyntaxTrees,
            CreateOrdinalMap(syntaxTrees),
            CreateOrdinalMap(akcssSyntaxTrees),
            rootNamespaces,
            akcssRootNamespaces,
            lastComputedMemberNames,
            lastComputedAkcssMemberNames,
            combinedDeclarationTable);
    }

    private static ImmutableDictionary<TTree, int> CreateOrdinalMap<TTree>(
        ImmutableArray<TTree> trees)
        where TTree : class
    {
        var builder = ImmutableDictionary.CreateBuilder<TTree, int>();
        for (var index = 0; index < trees.Length; index++)
        {
            builder[trees[index]] = index;
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<AkburaSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> CreateRootNamespaces(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> previousMemberNames)
    {
        var builder = ImmutableDictionary.CreateBuilder<AkburaSyntaxTree, Lazy<RootSingleNamespaceDeclaration>>();
        foreach (var tree in syntaxTrees)
        {
            previousMemberNames.TryGetValue(tree, out var memberNames);
            builder.Add(
                tree,
                new Lazy<RootSingleNamespaceDeclaration>(
                    () => DeclarationTreeBuilder.ForTree(tree, memberNames)));
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<AkcssSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> CreateAkcssRootNamespaces(
        ImmutableArray<AkcssSyntaxTree> syntaxTrees,
        ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> previousMemberNames)
    {
        var builder = ImmutableDictionary.CreateBuilder<AkcssSyntaxTree, Lazy<RootSingleNamespaceDeclaration>>();
        foreach (var tree in syntaxTrees)
        {
            previousMemberNames.TryGetValue(tree, out var memberNames);
            builder.Add(
                tree,
                new Lazy<RootSingleNamespaceDeclaration>(
                    () => DeclarationTreeBuilder.ForTree(tree, memberNames)));
        }

        return builder.ToImmutable();
    }

    private static DeclarationTable CreateRootDeclarationTable(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        ImmutableDictionary<AkburaSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> rootNamespaces,
        ImmutableDictionary<AkcssSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> akcssRootNamespaces)
    {
        var builder = Akbura.Language.DeclarationTable.Empty.ToBuilder();
        foreach (var tree in syntaxTrees)
        {
            builder.AddRootDeclaration(rootNamespaces[tree]);
        }

        foreach (var tree in akcssSyntaxTrees)
        {
            builder.AddRootDeclaration(akcssRootNamespaces[tree]);
        }

        return builder.ToDeclarationTableAndFree();
    }

    private static ImmutableDictionary<TTree, OneOrMany<WeakReference<BoxedMemberNames>>> CreateLastComputedMemberNames<TTree>(
        ImmutableDictionary<TTree, Lazy<RootSingleNamespaceDeclaration>> rootNamespaces)
        where TTree : class
    {
        var builder = ImmutableDictionary.CreateBuilder<TTree, OneOrMany<WeakReference<BoxedMemberNames>>>();
        foreach (var pair in rootNamespaces)
        {
            builder.Add(pair.Key, CreateLastComputedMemberNames(pair.Value.Value));
        }

        return builder.ToImmutable();
    }

    private static OneOrMany<WeakReference<BoxedMemberNames>> CreateLastComputedMemberNames(
        RootSingleNamespaceDeclaration root)
    {
        var builder = ImmutableArray.CreateBuilder<WeakReference<BoxedMemberNames>>();
        AddLastComputedMemberNames(root.Children, builder);
        return OneOrMany.Create(builder.ToImmutable());
    }

    private static void AddLastComputedMemberNames(
        ImmutableArray<SingleNamespaceOrTypeDeclaration> declarations,
        ImmutableArray<WeakReference<BoxedMemberNames>>.Builder builder)
    {
        foreach (var declaration in declarations)
        {
            if (declaration is SingleTypeDeclaration typeDeclaration)
            {
                if (DeclarationTreeBuilder.CachesComputedMemberNames(typeDeclaration))
                {
                    builder.Add(new WeakReference<BoxedMemberNames>(typeDeclaration.MemberNames));
                }

                AddLastComputedMemberNames(typeDeclaration.Children, builder);
            }
            else
            {
                AddLastComputedMemberNames(declaration.Children, builder);
            }
        }
    }

    internal sealed class State
    {
        internal readonly ImmutableArray<AkburaSyntaxTree> SyntaxTrees;
        internal readonly ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees;
        internal readonly ImmutableDictionary<AkburaSyntaxTree, int> SyntaxOrdinalMap;
        internal readonly ImmutableDictionary<AkcssSyntaxTree, int> AkcssOrdinalMap;
        internal readonly ImmutableDictionary<AkburaSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> RootNamespaces;
        internal readonly ImmutableDictionary<AkcssSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> AkcssRootNamespaces;

        /// <summary>
        /// Mapping from a syntax tree to the last fully computed member names for declaration containers in lexical order.
        /// </summary>
        /// <remarks>
        /// Member names often do not change for most edits, so keeping weak references lets the next state reuse the same
        /// immutable set when the old declaration is still alive.
        /// </remarks>
        internal readonly ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> LastComputedMemberNames;

        /// <summary>
        /// Mapping from an AKCSS syntax tree to the last fully computed member names for declaration containers in lexical order.
        /// </summary>
        internal readonly ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> LastComputedAkcssMemberNames;
        internal readonly DeclarationTable DeclarationTable;

        public State(
            ImmutableArray<AkburaSyntaxTree> syntaxTrees,
            ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
            ImmutableDictionary<AkburaSyntaxTree, int> syntaxOrdinalMap,
            ImmutableDictionary<AkcssSyntaxTree, int> akcssOrdinalMap,
            ImmutableDictionary<AkburaSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> rootNamespaces,
            ImmutableDictionary<AkcssSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> akcssRootNamespaces,
            ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> lastComputedMemberNames,
            ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> lastComputedAkcssMemberNames,
            DeclarationTable declarationTable)
        {
            Debug.Assert(syntaxTrees.All(tree => syntaxTrees[syntaxOrdinalMap[tree]] == tree));
            Debug.Assert(akcssSyntaxTrees.All(tree => akcssSyntaxTrees[akcssOrdinalMap[tree]] == tree));
            Debug.Assert(syntaxTrees.SetEquals(rootNamespaces.Keys.ToImmutableArray(), EqualityComparer<AkburaSyntaxTree>.Default));
            Debug.Assert(akcssSyntaxTrees.SetEquals(akcssRootNamespaces.Keys.ToImmutableArray(), EqualityComparer<AkcssSyntaxTree>.Default));

            SyntaxTrees = syntaxTrees;
            AkcssSyntaxTrees = akcssSyntaxTrees;
            SyntaxOrdinalMap = syntaxOrdinalMap;
            AkcssOrdinalMap = akcssOrdinalMap;
            RootNamespaces = rootNamespaces;
            AkcssRootNamespaces = akcssRootNamespaces;
            LastComputedMemberNames = lastComputedMemberNames;
            LastComputedAkcssMemberNames = lastComputedAkcssMemberNames;
            DeclarationTable = declarationTable ?? throw new ArgumentNullException(nameof(declarationTable));
        }
    }
}
