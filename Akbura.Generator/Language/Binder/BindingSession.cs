using Akbura.Language.Declarations;
using Akbura.Language.Syntax;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed class BindingSession
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly ConcurrentDictionary<BinderCacheKey, Binder> _binderCache = new();

    public BindingSession(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        RootBinder = new CompilationBinder(semanticModel);
    }

    public CompilationBinder RootBinder { get; }

    public int CachedBinderCount => _binderCache.Count;

    public Binder GetBinder(AkburaSyntax syntax, BinderUsage usage = BinderUsage.Default)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (!TryFindDeclarationPath(syntax, out var path) ||
            path.Length == 0)
        {
            return RootBinder;
        }

        var flags = GetPathFlags(path);
        var declaration = path[path.Length - 1];
        var scopeDesignator = GetScopeDesignator(path);
        var nextScopeKey = GetNextScopeKey(path);
        var key = new BinderCacheKey(
            syntax.Green,
            usage,
            flags,
            declaration.Kind,
            scopeDesignator?.Green,
            nextScopeKey);

        var binder = CreateBinderChain(path, usage);
        return _binderCache.GetOrAdd(key, binder);
    }

    public CSharpProbeBinder GetCSharpProbeBinder(
        AkburaSyntax syntax,
        BinderUsage usage = BinderUsage.Expression)
    {
        var next = GetBinder(syntax, usage);
        return new CSharpProbeBinder(_semanticModel, next);
    }

    private bool TryFindDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<AkburaDeclaration> path)
    {
        foreach (var root in _semanticModel.Compilation.DeclarationTable.Roots)
        {
            var builder = ImmutableArray.CreateBuilder<AkburaDeclaration>();
            if (TryFindDeclarationPath(root, syntax, builder))
            {
                path = builder.ToImmutable();
                return true;
            }
        }

        path = default;
        return false;
    }

    private static bool TryFindDeclarationPath(
        AkburaDeclaration current,
        AkburaSyntax syntax,
        ImmutableArray<AkburaDeclaration>.Builder path)
    {
        path.Add(current);

        if (ReferenceEquals(current.Syntax, syntax) ||
            ReferenceEquals(current.Syntax.Green, syntax.Green))
        {
            return true;
        }

        foreach (var child in current.Children)
        {
            if (TryFindDeclarationPath(child, syntax, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private Binder CreateBinderChain(
        ImmutableArray<AkburaDeclaration> path,
        BinderUsage usage)
    {
        Binder current = RootBinder;
        for (var index = 0; index < path.Length; index++)
        {
            var declaration = path[index];
            current = declaration.Kind switch
            {
                AkburaDeclarationKind.Component => new ComponentBinder(
                    _semanticModel,
                    current,
                    declaration,
                    current.Flags | GetUsageFlags(usage)),

                AkburaDeclarationKind.MarkupRoot or AkburaDeclarationKind.MarkupElement => new MarkupBinder(
                    _semanticModel,
                    current,
                    declaration,
                    current.Flags | GetUsageFlags(usage)),

                AkburaDeclarationKind.AkcssModule => new AkcssModuleBinder(
                    _semanticModel,
                    current,
                    declaration,
                    current.Flags | GetUsageFlags(usage)),

                AkburaDeclarationKind.AkcssStyle or AkburaDeclarationKind.AkcssUtility => new AkcssStyleBinder(
                    _semanticModel,
                    current,
                    declaration,
                    current.Flags | GetUsageFlags(usage)),

                _ => current,
            };
        }

        return current;
    }

    private static AkburaBinderFlags GetPathFlags(ImmutableArray<AkburaDeclaration> path)
    {
        var flags = AkburaBinderFlags.None;
        foreach (var declaration in path)
        {
            flags |= declaration.Kind switch
            {
                AkburaDeclarationKind.Component => AkburaBinderFlags.InComponent,
                AkburaDeclarationKind.MarkupRoot or AkburaDeclarationKind.MarkupElement => AkburaBinderFlags.InMarkup,
                AkburaDeclarationKind.AkcssModule => AkburaBinderFlags.InAkcss,
                AkburaDeclarationKind.AkcssStyle => AkburaBinderFlags.InAkcss | AkburaBinderFlags.InAkcssStyle,
                AkburaDeclarationKind.AkcssUtility => AkburaBinderFlags.InAkcss | AkburaBinderFlags.InAkcssUtility,
                _ => AkburaBinderFlags.None,
            };
        }

        return flags;
    }

    private static AkburaBinderFlags GetUsageFlags(BinderUsage usage)
    {
        return usage switch
        {
            BinderUsage.Markup => AkburaBinderFlags.InMarkup,
            BinderUsage.Akcss => AkburaBinderFlags.InAkcss,
            _ => AkburaBinderFlags.None,
        };
    }

    private static AkburaSyntax? GetScopeDesignator(ImmutableArray<AkburaDeclaration> path)
    {
        for (var index = path.Length - 1; index >= 0; index--)
        {
            var declaration = path[index];
            switch (declaration.Kind)
            {
                case AkburaDeclarationKind.Component:
                case AkburaDeclarationKind.MarkupRoot:
                case AkburaDeclarationKind.MarkupElement:
                case AkburaDeclarationKind.AkcssModule:
                case AkburaDeclarationKind.AkcssStyle:
                case AkburaDeclarationKind.AkcssUtility:
                    return declaration.Syntax;
            }
        }

        return null;
    }

    private static string GetNextScopeKey(ImmutableArray<AkburaDeclaration> path)
    {
        for (var index = path.Length - 2; index >= 0; index--)
        {
            var declaration = path[index];
            switch (declaration.Kind)
            {
                case AkburaDeclarationKind.Component:
                case AkburaDeclarationKind.MarkupRoot:
                case AkburaDeclarationKind.MarkupElement:
                case AkburaDeclarationKind.AkcssModule:
                case AkburaDeclarationKind.AkcssStyle:
                case AkburaDeclarationKind.AkcssUtility:
                    return $"{declaration.Kind}:{declaration.Name}:{declaration.Syntax.FullSpan}";
            }
        }

        return string.Empty;
    }
}
