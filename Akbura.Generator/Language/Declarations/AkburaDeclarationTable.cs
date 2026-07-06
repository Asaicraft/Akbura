using Akbura.Collections;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Declarations;

internal sealed class AkburaDeclarationTable
{
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
        PathsBySyntax = CreatePathMap(Roots);
    }

    public ImmutableArray<AkburaDeclaration> Roots { get; }

    public ImmutableArray<AkburaDeclaration> Components { get; }

    public ImmutableArray<AkburaDeclaration> AkcssModules { get; }

    private Dictionary<AkburaSyntax, ImmutableArray<AkburaDeclaration>> PathsBySyntax { get; }

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
            ? []
            : syntaxTrees;
        akcssSyntaxTrees = akcssSyntaxTrees.IsDefault
            ? []
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

    internal static AkburaDeclarationTable Create(
        ImmutableArray<AkburaDeclaration> components,
        ImmutableArray<AkburaDeclaration> akcssModules)
    {
        return new AkburaDeclarationTable(components, akcssModules);
    }

    public bool TryGetDeclaration(AkburaSyntax syntax, out AkburaDeclaration declaration)
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
        out ImmutableArray<AkburaDeclaration> path)
    {
        if (syntax == null)
        {
            throw new System.ArgumentNullException(nameof(syntax));
        }

        return PathsBySyntax.TryGetValue(syntax, out path);
    }

    public bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<AkburaDeclaration> path)
    {
        if (syntax == null)
        {
            throw new System.ArgumentNullException(nameof(syntax));
        }

        var builder = ArrayBuilder<AkburaDeclaration>.GetInstance();
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
            if (ReferenceEquals(candidate.SyntaxTree, tree))
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
            if (ReferenceEquals(candidate.AkcssSyntaxTree, tree))
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    private static Dictionary<AkburaSyntax, ImmutableArray<AkburaDeclaration>> CreatePathMap(
        ImmutableArray<AkburaDeclaration> roots)
    {
        var map = new Dictionary<AkburaSyntax, ImmutableArray<AkburaDeclaration>>();
        var path = ArrayBuilder<AkburaDeclaration>.GetInstance();
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
        AkburaDeclaration declaration,
        ArrayBuilder<AkburaDeclaration> path,
        Dictionary<AkburaSyntax, ImmutableArray<AkburaDeclaration>> map)
    {
        path.Add(declaration);
        map[declaration.Syntax] = path.ToImmutable();

        foreach (var child in declaration.Children)
        {
            AddDeclarationPaths(child, path, map);
        }

        path.RemoveLast();
    }

    private static bool TryBuildDeclarationPath(
        AkburaDeclaration current,
        AkburaSyntax syntax,
        int position,
        ArrayBuilder<AkburaDeclaration> path)
    {
        if (!SemanticSyntaxIdentity.IsInSameTree(current.Syntax, syntax) ||
            !ContainsPosition(current.Syntax, position))
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
