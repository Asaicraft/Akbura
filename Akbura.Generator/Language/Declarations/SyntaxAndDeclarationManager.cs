using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Declarations;

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

        return WithSyntaxTrees(_syntaxTrees.AddRange(syntaxTrees));
    }

    public SyntaxAndDeclarationManager RemoveSyntaxTrees(IEnumerable<AkburaSyntaxTree> syntaxTrees)
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

        return ReferenceEquals(oldTree, newTree)
            ? this
            : WithSyntaxTrees(_syntaxTrees.SetItem(index, newTree));
    }

    public SyntaxAndDeclarationManager AddAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
    {
        if (syntaxTrees == null)
        {
            throw new ArgumentNullException(nameof(syntaxTrees));
        }

        return WithAkcssSyntaxTrees(_akcssSyntaxTrees.AddRange(syntaxTrees));
    }

    public SyntaxAndDeclarationManager RemoveAkcssSyntaxTrees(IEnumerable<AkcssSyntaxTree> syntaxTrees)
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

        return ReferenceEquals(oldTree, newTree)
            ? this
            : WithAkcssSyntaxTrees(_akcssSyntaxTrees.SetItem(index, newTree));
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
            CreateState(syntaxTrees, akcssSyntaxTrees, state.DeclarationTable));
    }
}
