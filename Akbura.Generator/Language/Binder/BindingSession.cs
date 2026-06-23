using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

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

    public BoundExpression BindExpression(
        AkburaSyntax syntax,
        CSharp.ExpressionSyntax expression,
        ITypeSymbol? targetType = null,
        BinderUsage usage = BinderUsage.Expression,
        bool isBindingPath = true)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        var probeBinder = GetCSharpProbeBinder(syntax, usage);
        if (targetType != null)
        {
            return probeBinder.BindExpression(
                syntax,
                expression,
                targetType,
                isBindingPath);
        }

        return (BoundExpression)_semanticModel.GetBoundNode(
            syntax,
            () => probeBinder.BindExpression(
                syntax,
                expression,
                targetType: null,
                isBindingPath));
    }

    private bool TryFindDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<AkburaDeclaration> path)
    {
        var finder = DeclarationPathFinder.GetInstance(syntax);
        return finder.TryFind(
            _semanticModel.Compilation.DeclarationTable.Roots,
            out path);
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

    private sealed class DeclarationPathFinder
    {
        private static readonly ObjectPool<DeclarationPathFinder> s_pool = CreatePool();

        private readonly ObjectPool<DeclarationPathFinder> _pool;
        private AkburaSyntax? _syntax;
        private ArrayBuilder<AkburaDeclaration>? _path;

        private DeclarationPathFinder(ObjectPool<DeclarationPathFinder> pool)
        {
            _pool = pool;
        }

        public static DeclarationPathFinder GetInstance(AkburaSyntax syntax)
        {
            var finder = s_pool.Allocate();
            finder._syntax = syntax;
            finder._path = ArrayBuilder<AkburaDeclaration>.GetInstance();
            return finder;
        }

        public bool TryFind(
            ImmutableArray<AkburaDeclaration> roots,
            out ImmutableArray<AkburaDeclaration> path)
        {
            foreach (var root in roots)
            {
                _path!.Clear();
                if (Visit(root))
                {
                    path = _path.ToImmutableAndFree();
                    _path = null;
                    Free();
                    return true;
                }
            }

            path = default;
            Free();
            return false;
        }

        private bool Visit(AkburaDeclaration current)
        {
            _path!.Add(current);

            var syntax = _syntax!;
            if (ReferenceEquals(current.Syntax, syntax) ||
                ReferenceEquals(current.Syntax.Green, syntax.Green))
            {
                return true;
            }

            foreach (var child in current.Children)
            {
                if (Visit(child))
                {
                    return true;
                }
            }

            _path.RemoveLast();
            return false;
        }

        private void Free()
        {
            _syntax = null;
            _path?.Free();
            _path = null;
            _pool.Free(this);
        }

        private static ObjectPool<DeclarationPathFinder> CreatePool()
        {
            ObjectPool<DeclarationPathFinder>? pool = null;
            pool = new ObjectPool<DeclarationPathFinder>(
                () => new DeclarationPathFinder(pool!),
                size: 16);
            return pool;
        }
    }
}
