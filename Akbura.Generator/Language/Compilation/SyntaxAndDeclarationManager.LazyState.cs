using Akbura.Collections;
using Akbura.Language.Declarations;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using BoxedMemberNames = System.Runtime.CompilerServices.StrongBox<Akbura.Collections.ImmutableSegmentedHashSet<string>>;

namespace Akbura.Language;

internal sealed partial class SyntaxAndDeclarationManager
{
    private readonly AkburaDeclarationTable? _previousDeclarationTable;
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
        AkburaDeclarationTable? previousTable)
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
        var declarationTable = AkburaDeclarationTable.Create(
            syntaxTrees,
            akcssSyntaxTrees,
            previousTable);
        var rootDeclarationTable = CreateRootDeclarationTable(
            syntaxTrees,
            akcssSyntaxTrees,
            rootNamespaces,
            akcssRootNamespaces);
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
            declarationTable,
            rootDeclarationTable);
    }

    private static State CreateState(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        AkburaDeclarationTable declarationTable,
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
        var rootDeclarationTable = CreateRootDeclarationTable(
            syntaxTrees,
            akcssSyntaxTrees,
            rootNamespaces,
            akcssRootNamespaces);
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
            declarationTable,
            rootDeclarationTable);
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
        public State(
            ImmutableArray<AkburaSyntaxTree> syntaxTrees,
            ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
            ImmutableDictionary<AkburaSyntaxTree, int> syntaxOrdinalMap,
            ImmutableDictionary<AkcssSyntaxTree, int> akcssOrdinalMap,
            ImmutableDictionary<AkburaSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> rootNamespaces,
            ImmutableDictionary<AkcssSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> akcssRootNamespaces,
            ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> lastComputedMemberNames,
            ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> lastComputedAkcssMemberNames,
            AkburaDeclarationTable declarationTable,
            DeclarationTable rootDeclarationTable)
        {
            SyntaxTrees = syntaxTrees;
            AkcssSyntaxTrees = akcssSyntaxTrees;
            SyntaxOrdinalMap = syntaxOrdinalMap;
            AkcssOrdinalMap = akcssOrdinalMap;
            RootNamespaces = rootNamespaces;
            AkcssRootNamespaces = akcssRootNamespaces;
            LastComputedMemberNames = lastComputedMemberNames;
            LastComputedAkcssMemberNames = lastComputedAkcssMemberNames;
            DeclarationTable = declarationTable ?? throw new ArgumentNullException(nameof(declarationTable));
            RootDeclarationTable = rootDeclarationTable ?? throw new ArgumentNullException(nameof(rootDeclarationTable));
        }

        public ImmutableArray<AkburaSyntaxTree> SyntaxTrees { get; }

        public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees { get; }

        public ImmutableDictionary<AkburaSyntaxTree, int> SyntaxOrdinalMap { get; }

        public ImmutableDictionary<AkcssSyntaxTree, int> AkcssOrdinalMap { get; }

        public ImmutableDictionary<AkburaSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> RootNamespaces { get; }

        public ImmutableDictionary<AkcssSyntaxTree, Lazy<RootSingleNamespaceDeclaration>> AkcssRootNamespaces { get; }

        public ImmutableDictionary<AkburaSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> LastComputedMemberNames { get; }

        public ImmutableDictionary<AkcssSyntaxTree, OneOrMany<WeakReference<BoxedMemberNames>>> LastComputedAkcssMemberNames { get; }

        public AkburaDeclarationTable DeclarationTable { get; }

        public DeclarationTable RootDeclarationTable { get; }
    }
}
