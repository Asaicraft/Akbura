using Akbura.Pools;
using Akbura.Language.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Declarations;

internal sealed class AkburaDeclarationTable
{
    private static readonly ObjectPool<Stack<AkburaDeclaration>> s_declarationStack =
        new(() => new Stack<AkburaDeclaration>(), 16);

    private AkburaDeclarationTable(
        ImmutableArray<AkburaDeclaration> components,
        ImmutableArray<AkburaDeclaration> akcssModules)
    {
        Components = components.IsDefault
            ? ImmutableArray<AkburaDeclaration>.Empty
            : components;
        AkcssModules = akcssModules.IsDefault
            ? ImmutableArray<AkburaDeclaration>.Empty
            : akcssModules;
        Roots = Components.AddRange(AkcssModules);
    }

    public ImmutableArray<AkburaDeclaration> Roots { get; }

    public ImmutableArray<AkburaDeclaration> Components { get; }

    public ImmutableArray<AkburaDeclaration> AkcssModules { get; }

    public static AkburaDeclarationTable Create(AkburaCompilation compilation)
    {
        return Create(
            compilation.SyntaxTrees,
            compilation.AkcssSyntaxTrees,
            compilation.PreviousCompilation?.DeclarationTable);
    }

    public static AkburaDeclarationTable Create(
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        AkburaDeclarationTable? previous)
    {
        syntaxTrees = syntaxTrees.IsDefault
            ? ImmutableArray<AkburaSyntaxTree>.Empty
            : syntaxTrees;
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? ImmutableArray<AkcssSyntaxTree>.Empty
            : akcssSyntaxTrees;

        var components = syntaxTrees
            .Select(tree => TryReuseComponent(previous, tree, out var declaration)
                ? declaration
                : AkburaDeclarationCollector.Collect(tree))
            .ToImmutableArray();

        var akcssModules = akcssSyntaxTrees
            .Select(tree => TryReuseAkcssModule(previous, tree, out var declaration)
                ? declaration
                : AkburaDeclarationCollector.Collect(tree))
            .ToImmutableArray();

        return new AkburaDeclarationTable(components, akcssModules);
    }

    public bool TryGetDeclaration(Akbura.Language.Syntax.AkburaSyntax syntax, out AkburaDeclaration declaration)
    {
        var stack = s_declarationStack.Allocate();
        try
        {
            for (var index = Roots.Length - 1; index >= 0; index--)
            {
                stack.Push(Roots[index]);
            }

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (ReferenceEquals(current.Syntax, syntax) ||
                    ReferenceEquals(current.Syntax.Green, syntax.Green))
                {
                    declaration = current;
                    return true;
                }

                for (var index = current.Children.Length - 1; index >= 0; index--)
                {
                    stack.Push(current.Children[index]);
                }
            }

            declaration = null!;
            return false;
        }
        finally
        {
            stack.Clear();
            s_declarationStack.Free(stack);
        }
    }

    private static bool TryReuseComponent(
        AkburaDeclarationTable? previous,
        AkburaSyntaxTree tree,
        out AkburaDeclaration declaration)
    {
        declaration = null!;
        if (previous == null)
        {
            return false;
        }

        foreach (var candidate in previous.Components)
        {
            if (candidate.SyntaxTree?.FilePath == tree.FilePath &&
                ReferenceEquals(candidate.Syntax.Green, tree.GreenRoot))
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryReuseAkcssModule(
        AkburaDeclarationTable? previous,
        AkcssSyntaxTree tree,
        out AkburaDeclaration declaration)
    {
        declaration = null!;
        if (previous == null)
        {
            return false;
        }

        foreach (var candidate in previous.AkcssModules)
        {
            if (candidate.AkcssSyntaxTree?.FilePath == tree.FilePath &&
                candidate.AkcssSyntaxTree?.LogicalName == tree.LogicalName &&
                ReferenceEquals(candidate.Syntax.Green, tree.GreenRoot))
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

}
