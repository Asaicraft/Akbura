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
    private readonly BinderFactory _binderFactory;

    public BindingSession(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        RootBinder = new CompilationBinder(semanticModel);
        _binderFactory = new BinderFactory(semanticModel, this);
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

        var key = BinderFactory.BinderFactoryVisitor.CreateBinderCacheKey(
            syntax,
            path,
            usage);
        return _binderCache.GetOrAdd(
            key,
            _ => _binderFactory.CreateBinder(path, usage));
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
