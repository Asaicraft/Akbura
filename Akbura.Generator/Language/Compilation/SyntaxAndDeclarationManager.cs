using Akbura.Language.Declarations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language;

internal sealed partial class SyntaxAndDeclarationManager
{
    private readonly ImmutableArray<AkburaSyntaxTree> _syntaxTrees;
    private readonly ImmutableArray<AkcssSyntaxTree> _akcssSyntaxTrees;

    public SyntaxAndDeclarationManager(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        SyntaxAndDeclarationManager? previous = null)
        : this(
            syntaxTrees,
            akcssSyntaxTrees,
            state: null,
            previousDeclarationTable: previous?._lazyState?.DeclarationTable)
    {
    }

    private SyntaxAndDeclarationManager(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        State? state,
        AkburaDeclarationTable? previousDeclarationTable = null)
    {
        _syntaxTrees = syntaxTrees.IsDefault
            ? ImmutableArray<AkburaSyntaxTree>.Empty
            : syntaxTrees;
        _akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? ImmutableArray<AkcssSyntaxTree>.Empty
            : akcssSyntaxTrees;
        _lazyState = state;
        _previousDeclarationTable = previousDeclarationTable;
    }

    public ImmutableArray<AkburaSyntaxTree> SyntaxTrees => _syntaxTrees;

    public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees => _akcssSyntaxTrees;

    public AkburaDeclarationTable DeclarationTable => GetLazyState().DeclarationTable;

    public SyntaxAndDeclarationManager WithSyntaxTrees(ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        syntaxTrees = syntaxTrees.IsDefault
            ? []
            : syntaxTrees;
        return _syntaxTrees.SequenceEqual(syntaxTrees)
            ? this
            : WithTrees(syntaxTrees, _akcssSyntaxTrees);
    }

    public SyntaxAndDeclarationManager WithAkcssSyntaxTrees(ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? []
            : akcssSyntaxTrees;
        return _akcssSyntaxTrees.SequenceEqual(akcssSyntaxTrees)
            ? this
            : WithTrees(_syntaxTrees, akcssSyntaxTrees);
    }

    public SyntaxAndDeclarationManager AddSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        var addedTrees = syntaxTrees.ToImmutableArray();
        if (addedTrees.IsEmpty)
        {
            return this;
        }

        var newSyntaxTrees = _syntaxTrees.AddRange(addedTrees);
        var state = _lazyState;
        return state == null
            ? new SyntaxAndDeclarationManager(newSyntaxTrees, _akcssSyntaxTrees)
            : new SyntaxAndDeclarationManager(
                newSyntaxTrees,
                _akcssSyntaxTrees,
                AddSyntaxTrees(state, newSyntaxTrees, addedTrees));
    }

    public SyntaxAndDeclarationManager RemoveSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        var removeSet = syntaxTrees.ToImmutableHashSet();
        if (removeSet.Count == 0)
        {
            return this;
        }

        var newSyntaxTrees = _syntaxTrees.RemoveAll(removeSet.Contains);
        if (newSyntaxTrees.Length == _syntaxTrees.Length)
        {
            return this;
        }

        var state = _lazyState;
        return state == null
            ? new SyntaxAndDeclarationManager(newSyntaxTrees, _akcssSyntaxTrees)
            : new SyntaxAndDeclarationManager(
                newSyntaxTrees,
                _akcssSyntaxTrees,
                RemoveSyntaxTrees(state, newSyntaxTrees, removeSet));
    }

    public SyntaxAndDeclarationManager ReplaceSyntaxTree(
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
            throw new ArgumentException("Syntax tree is not part of this manager.", nameof(oldTree));
        }

        if (ReferenceEquals(oldTree, newTree))
        {
            return this;
        }

        var newSyntaxTrees = _syntaxTrees.SetItem(index, newTree);
        var state = _lazyState;
        return state == null
            ? new SyntaxAndDeclarationManager(newSyntaxTrees, _akcssSyntaxTrees)
            : new SyntaxAndDeclarationManager(
                newSyntaxTrees,
                _akcssSyntaxTrees,
                ReplaceSyntaxTree(state, newSyntaxTrees, index, newTree));
    }

    public SyntaxAndDeclarationManager AddAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        var addedTrees = syntaxTrees.ToImmutableArray();
        if (addedTrees.IsEmpty)
        {
            return this;
        }

        var newAkcssSyntaxTrees = _akcssSyntaxTrees.AddRange(addedTrees);
        var state = _lazyState;
        return state == null
            ? new SyntaxAndDeclarationManager(_syntaxTrees, newAkcssSyntaxTrees)
            : new SyntaxAndDeclarationManager(
                _syntaxTrees,
                newAkcssSyntaxTrees,
                AddAkcssSyntaxTrees(state, newAkcssSyntaxTrees, addedTrees));
    }

    public SyntaxAndDeclarationManager RemoveAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        var removeSet = syntaxTrees.ToImmutableHashSet();
        if (removeSet.Count == 0)
        {
            return this;
        }

        var newAkcssSyntaxTrees = _akcssSyntaxTrees.RemoveAll(removeSet.Contains);
        if (newAkcssSyntaxTrees.Length == _akcssSyntaxTrees.Length)
        {
            return this;
        }

        var state = _lazyState;
        return state == null
            ? new SyntaxAndDeclarationManager(_syntaxTrees, newAkcssSyntaxTrees)
            : new SyntaxAndDeclarationManager(
                _syntaxTrees,
                newAkcssSyntaxTrees,
                RemoveAkcssSyntaxTrees(state, newAkcssSyntaxTrees, removeSet));
    }

    public SyntaxAndDeclarationManager ReplaceAkcssSyntaxTree(
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
            throw new ArgumentException("AKCSS syntax tree is not part of this manager.", nameof(oldTree));
        }

        if (ReferenceEquals(oldTree, newTree))
        {
            return this;
        }

        var newAkcssSyntaxTrees = _akcssSyntaxTrees.SetItem(index, newTree);
        var state = _lazyState;
        return state == null
            ? new SyntaxAndDeclarationManager(_syntaxTrees, newAkcssSyntaxTrees)
            : new SyntaxAndDeclarationManager(
                _syntaxTrees,
                newAkcssSyntaxTrees,
                ReplaceAkcssSyntaxTree(state, newAkcssSyntaxTrees, index, newTree));
    }

    private SyntaxAndDeclarationManager WithTrees(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        var state = _lazyState;
        if (state == null)
        {
            return new SyntaxAndDeclarationManager(syntaxTrees, akcssSyntaxTrees);
        }

        return new SyntaxAndDeclarationManager(
            syntaxTrees,
            akcssSyntaxTrees,
            CreateState(
                syntaxTrees,
                akcssSyntaxTrees,
                AkburaDeclarationTable.Create(
                    syntaxTrees,
                    akcssSyntaxTrees,
                    state.DeclarationTable),
                state.LastComputedMemberNames,
                state.LastComputedAkcssMemberNames));
    }

    private static State AddSyntaxTrees(
        State state,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkburaSyntaxTree> addedTrees)
    {
        var components = state.DeclarationTable.Components.ToBuilder();
        foreach (var tree in addedTrees)
        {
            components.Add(AkburaDeclarationCollector.Collect(tree));
        }

        return CreateState(
            syntaxTrees,
            state.AkcssSyntaxTrees,
            AkburaDeclarationTable.Create(
                components.ToImmutable(),
                state.DeclarationTable.AkcssModules),
            state.LastComputedMemberNames,
            state.LastComputedAkcssMemberNames);
    }

    private static State RemoveSyntaxTrees(
        State state,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        IImmutableSet<AkburaSyntaxTree> removeSet)
    {
        var components = state.DeclarationTable.Components
            .RemoveAll(declaration => declaration.SyntaxTree != null &&
                                      removeSet.Contains(declaration.SyntaxTree));

        return CreateState(
            syntaxTrees,
            state.AkcssSyntaxTrees,
            AkburaDeclarationTable.Create(
                components,
                state.DeclarationTable.AkcssModules),
            state.LastComputedMemberNames,
            state.LastComputedAkcssMemberNames);
    }

    private static State ReplaceSyntaxTree(
        State state,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        int index,
        AkburaSyntaxTree newTree)
    {
        var components = state.DeclarationTable.Components.SetItem(
            index,
            AkburaDeclarationCollector.Collect(newTree));

        var previousMemberNames = state.LastComputedMemberNames;
        var oldTree = state.SyntaxTrees[index];
        if (previousMemberNames.TryGetValue(oldTree, out var memberNames))
        {
            previousMemberNames = previousMemberNames
                .Remove(oldTree)
                .SetItem(newTree, memberNames);
        }

        return CreateState(
            syntaxTrees,
            state.AkcssSyntaxTrees,
            AkburaDeclarationTable.Create(
                components,
                state.DeclarationTable.AkcssModules),
            previousMemberNames,
            state.LastComputedAkcssMemberNames);
    }

    private static State AddAkcssSyntaxTrees(
        State state,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        ImmutableArray<AkcssSyntaxTree> addedTrees)
    {
        var akcssModules = state.DeclarationTable.AkcssModules.ToBuilder();
        foreach (var tree in addedTrees)
        {
            akcssModules.Add(AkburaDeclarationCollector.Collect(tree));
        }

        return CreateState(
            state.SyntaxTrees,
            akcssSyntaxTrees,
            AkburaDeclarationTable.Create(
                state.DeclarationTable.Components,
                akcssModules.ToImmutable()),
            state.LastComputedMemberNames,
            state.LastComputedAkcssMemberNames);
    }

    private static State RemoveAkcssSyntaxTrees(
        State state,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        IImmutableSet<AkcssSyntaxTree> removeSet)
    {
        var akcssModules = state.DeclarationTable.AkcssModules
            .RemoveAll(declaration => declaration.AkcssSyntaxTree != null &&
                                      removeSet.Contains(declaration.AkcssSyntaxTree));

        return CreateState(
            state.SyntaxTrees,
            akcssSyntaxTrees,
            AkburaDeclarationTable.Create(
                state.DeclarationTable.Components,
                akcssModules),
            state.LastComputedMemberNames,
            state.LastComputedAkcssMemberNames);
    }

    private static State ReplaceAkcssSyntaxTree(
        State state,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        int index,
        AkcssSyntaxTree newTree)
    {
        var akcssModules = state.DeclarationTable.AkcssModules.SetItem(
            index,
            AkburaDeclarationCollector.Collect(newTree));

        var previousMemberNames = state.LastComputedAkcssMemberNames;
        var oldTree = state.AkcssSyntaxTrees[index];
        if (previousMemberNames.TryGetValue(oldTree, out var memberNames))
        {
            previousMemberNames = previousMemberNames
                .Remove(oldTree)
                .SetItem(newTree, memberNames);
        }

        return CreateState(
            state.SyntaxTrees,
            akcssSyntaxTrees,
            AkburaDeclarationTable.Create(
                state.DeclarationTable.Components,
                akcssModules),
            state.LastComputedMemberNames,
            previousMemberNames);
    }
}
