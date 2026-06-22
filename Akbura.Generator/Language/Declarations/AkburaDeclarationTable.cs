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
    }

    public ImmutableArray<AkburaDeclaration> Roots { get; }

    public ImmutableArray<AkburaDeclaration> Components { get; }

    public ImmutableArray<AkburaDeclaration> AkcssModules { get; }

    public static AkburaDeclarationTable Create(AkburaCompilation compilation)
    {
        var previous = compilation.PreviousCompilation?.DeclarationTable;

        var components = compilation.SyntaxTrees
            .Select(tree => TryReuseComponent(previous, tree, out var declaration)
                ? declaration
                : AkburaDeclarationCollector.Collect(tree))
            .ToImmutableArray();

        var akcssModules = compilation.AkcssSyntaxTrees
            .Select(tree => TryReuseAkcssModule(previous, tree, out var declaration)
                ? declaration
                : AkburaDeclarationCollector.Collect(tree))
            .ToImmutableArray();

        return new AkburaDeclarationTable(components, akcssModules);
    }

    public bool TryGetDeclaration(Akbura.Language.Syntax.AkburaSyntax syntax, out AkburaDeclaration declaration)
    {
        foreach (var root in Roots)
        {
            if (TryGetDeclaration(root, syntax, out declaration))
            {
                return true;
            }
        }

        declaration = null!;
        return false;
    }

    private static bool TryGetDeclaration(
        AkburaDeclaration current,
        Akbura.Language.Syntax.AkburaSyntax syntax,
        out AkburaDeclaration declaration)
    {
        if (ReferenceEquals(current.Syntax, syntax) ||
            ReferenceEquals(current.Syntax.Green, syntax.Green))
        {
            declaration = current;
            return true;
        }

        foreach (var child in current.Children)
        {
            if (TryGetDeclaration(child, syntax, out declaration))
            {
                return true;
            }
        }

        declaration = null!;
        return false;
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
