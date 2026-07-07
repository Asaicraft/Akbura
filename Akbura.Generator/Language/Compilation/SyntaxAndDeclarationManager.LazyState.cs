using Akbura.Language.Declarations;
using System;
using System.Collections.Immutable;
using System.Threading;

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
        var declarationTable = AkburaDeclarationTable.Create(
            syntaxTrees,
            akcssSyntaxTrees,
            previousTable);
        return new State(
            syntaxTrees,
            akcssSyntaxTrees,
            syntaxOrdinalMap,
            akcssOrdinalMap,
            declarationTable);
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

    internal sealed class State
    {
        public State(
            ImmutableArray<AkburaSyntaxTree> syntaxTrees,
            ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
            ImmutableDictionary<AkburaSyntaxTree, int> syntaxOrdinalMap,
            ImmutableDictionary<AkcssSyntaxTree, int> akcssOrdinalMap,
            AkburaDeclarationTable declarationTable)
        {
            SyntaxTrees = syntaxTrees;
            AkcssSyntaxTrees = akcssSyntaxTrees;
            SyntaxOrdinalMap = syntaxOrdinalMap;
            AkcssOrdinalMap = akcssOrdinalMap;
            DeclarationTable = declarationTable ?? throw new ArgumentNullException(nameof(declarationTable));
        }

        public ImmutableArray<AkburaSyntaxTree> SyntaxTrees { get; }

        public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees { get; }

        public ImmutableDictionary<AkburaSyntaxTree, int> SyntaxOrdinalMap { get; }

        public ImmutableDictionary<AkcssSyntaxTree, int> AkcssOrdinalMap { get; }

        public AkburaDeclarationTable DeclarationTable { get; }
    }
}
