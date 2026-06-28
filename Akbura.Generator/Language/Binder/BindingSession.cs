using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

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

    public Binder GetBinder(
        AkburaSyntax syntax,
        int position,
        BinderUsage usage = BinderUsage.Default)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (syntax.FullWidth == 0 && position == syntax.Position)
        {
            return GetBinder(syntax, usage);
        }

        if (!syntax.FullSpan.Contains(position) &&
            position != syntax.EndPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var normalizedPosition = position == syntax.EndPosition
            ? position - 1
            : position;
        if (!TryFindDeclarationPath(syntax, normalizedPosition, out var path) ||
            path.Length == 0)
        {
            return RootBinder;
        }

        var cacheSyntax = path[path.Length - 1].Syntax;
        var key = BinderFactory.BinderFactoryVisitor.CreateBinderCacheKey(
            cacheSyntax,
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

    public BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        return _semanticModel.GetBoundNode(
            syntax,
            () => GetOperationBinder(syntax).BindOperationSyntax(syntax));
    }

    private Binder GetOperationBinder(AkburaSyntax syntax)
    {
        var usage = syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax => BinderUsage.Markup,
            AkburaSyntaxKind.AkcssAssignmentSyntax or
                AkburaSyntaxKind.AkcssIfDirectiveSyntax or
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax or
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax => BinderUsage.Akcss,
            _ => BinderUsage.Expression,
        };

        return GetBinder(syntax.Root, syntax.Position, usage);
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

    private bool TryFindDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<AkburaDeclaration> path)
    {
        var finder = DeclarationPathFinder.GetInstance(syntax, position);
        return finder.TryFind(
            _semanticModel.Compilation.DeclarationTable.Roots,
            out path);
    }

    private sealed class DeclarationPathFinder
    {
        private static readonly ObjectPool<DeclarationPathFinder> s_pool = CreatePool();

        private readonly ObjectPool<DeclarationPathFinder> _pool;
        private AkburaSyntax? _syntax;
        private int _position;
        private bool _findByPosition;
        private ArrayBuilder<AkburaDeclaration>? _path;

        private DeclarationPathFinder(ObjectPool<DeclarationPathFinder> pool)
        {
            _pool = pool;
        }

        public static DeclarationPathFinder GetInstance(AkburaSyntax syntax)
        {
            var finder = s_pool.Allocate();
            finder._syntax = syntax;
            finder._position = 0;
            finder._findByPosition = false;
            finder._path = ArrayBuilder<AkburaDeclaration>.GetInstance();
            return finder;
        }

        public static DeclarationPathFinder GetInstance(
            AkburaSyntax syntax,
            int position)
        {
            var finder = s_pool.Allocate();
            finder._syntax = syntax;
            finder._position = position;
            finder._findByPosition = true;
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

            if (_findByPosition)
            {
                return VisitByPosition(current);
            }

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

        private bool VisitByPosition(AkburaDeclaration current)
        {
            var syntax = _syntax!;
            if (!ReferenceEquals(current.Syntax.Root.Green, syntax.Root.Green) ||
                !ContainsPosition(current.Syntax, _position))
            {
                _path!.RemoveLast();
                return false;
            }

            foreach (var child in current.Children)
            {
                if (Visit(child))
                {
                    return true;
                }
            }

            return true;
        }

        private void Free()
        {
            _syntax = null;
            _position = 0;
            _findByPosition = false;
            _path?.Free();
            _path = null;
            _pool.Free(this);
        }

        private static bool ContainsPosition(AkburaSyntax syntax, int position)
        {
            return syntax.FullSpan.Contains(position) ||
                   (syntax.FullWidth == 0 && position == syntax.Position);
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
