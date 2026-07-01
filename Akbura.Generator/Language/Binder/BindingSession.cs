using Akbura.Collections;
using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class BindingSession
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly BinderFactory _binderFactory;
    private readonly ConcurrentCache<BinderCacheKey, ExecutableCodeBinder> _executableBinderCache;

    public BindingSession(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        RootBinder = new CompilationBinder(semanticModel);
        _binderFactory = new BinderFactory(semanticModel, this);
        _executableBinderCache = new ConcurrentCache<BinderCacheKey, ExecutableCodeBinder>(16);
    }

    public CompilationBinder RootBinder { get; }

    public int CachedBinderCount => _binderFactory.CachedBinderCount;

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

        return GetBinderFromPath(path, usage);
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

        return GetBinderFromPath(path, usage);
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

    public BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        return _semanticModel.GetBoundNode(
            syntax,
            () => GetSemanticBinder(syntax).BindSemanticSyntax(syntax));
    }

    private Binder GetOperationBinder(AkburaSyntax syntax)
    {
        return GetBinder(syntax.Root, syntax.Position, GetOperationUsage(syntax.Kind));
    }

    private Binder GetSemanticBinder(AkburaSyntax syntax)
    {
        return GetBinder(syntax.Root, syntax.Position, GetSemanticUsage(syntax.Kind));
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

    private static BinderUsage GetOperationUsage(AkburaSyntaxKind kind)
    {
        return kind switch
        {
            AkburaSyntaxKind.MarkupElementSyntax or
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
    }

    private static BinderUsage GetSemanticUsage(AkburaSyntaxKind kind)
    {
        return kind switch
        {
            AkburaSyntaxKind.MarkupRootSyntax or
                AkburaSyntaxKind.MarkupElementSyntax or
                AkburaSyntaxKind.MarkupElementContentSyntax or
                AkburaSyntaxKind.MarkupInlineExpressionSyntax or
                AkburaSyntaxKind.MarkupTextLiteralSyntax or
                AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax => BinderUsage.Markup,
            AkburaSyntaxKind.InlineAkcssBlockSyntax or
                AkburaSyntaxKind.AkcssStyleRuleSyntax or
                AkburaSyntaxKind.AkcssUtilityDeclarationSyntax or
                AkburaSyntaxKind.AkcssAssignmentSyntax or
                AkburaSyntaxKind.AkcssIfDirectiveSyntax or
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax or
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax => BinderUsage.Akcss,
            _ => BinderUsage.Expression,
        };
    }

    internal Binder GetOrCreateBinder(
        ImmutableArray<AkburaDeclaration> path,
        BinderUsage usage)
    {
        if (path.IsDefaultOrEmpty)
        {
            return RootBinder;
        }

        var cacheSyntax = path[path.Length - 1].Syntax;
        var key = BinderFactory.BinderFactoryVisitor.CreateBinderCacheKey(
            cacheSyntax,
            usage);
        return _binderFactory.GetOrCreateBinder(
            key,
            path,
            usage);
    }

    private Binder GetBinderFromPath(
        ImmutableArray<AkburaDeclaration> path,
        BinderUsage usage)
    {
        if (TryGetExecutableRootPath(path, out var executableRootPath))
        {
            var targetSyntax = path[path.Length - 1].Syntax;
            return GetExecutableCodeBinder(executableRootPath, usage)
                .GetBinder(targetSyntax);
        }

        return GetOrCreateBinder(path, usage);
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

    private ExecutableCodeBinder GetExecutableCodeBinder(
        ImmutableArray<AkburaDeclaration> executableRootPath,
        BinderUsage usage)
    {
        var rootDeclaration = executableRootPath[executableRootPath.Length - 1];
        var key = new BinderCacheKey(rootDeclaration.Syntax.Green, usage);
        if (_executableBinderCache.TryGetValue(key, out var executableBinder))
        {
            return executableBinder;
        }

        var nextPath = SlicePath(executableRootPath, executableRootPath.Length - 1);
        var next = nextPath.IsDefaultOrEmpty
            ? RootBinder
            : GetOrCreateBinder(nextPath, usage);
        executableBinder = new ExecutableCodeBinder(
            this,
            executableRootPath,
            next,
            usage);
        if (!_executableBinderCache.TryAdd(key, executableBinder) &&
            _executableBinderCache.TryGetValue(key, out var cachedExecutableBinder))
        {
            return cachedExecutableBinder;
        }

        return executableBinder;
    }

    private static bool TryGetExecutableRootPath(
        ImmutableArray<AkburaDeclaration> path,
        out ImmutableArray<AkburaDeclaration> executableRootPath)
    {
        for (var index = 0; index < path.Length; index++)
        {
            if (IsExecutableRoot(path[index]))
            {
                executableRootPath = SlicePath(path, index + 1);
                return true;
            }
        }

        executableRootPath = default;
        return false;
    }

    private static bool IsExecutableRoot(AkburaDeclaration declaration)
    {
        return declaration.Kind is
            AkburaDeclarationKind.CSharpStatement or
            AkburaDeclarationKind.CSharpBlock;
    }

    private static ImmutableArray<AkburaDeclaration> SlicePath(
        ImmutableArray<AkburaDeclaration> path,
        int length)
    {
        if (length == 0)
        {
            return ImmutableArray<AkburaDeclaration>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<AkburaDeclaration>(length);
        for (var index = 0; index < length; index++)
        {
            builder.Add(path[index]);
        }

        return builder.ToImmutable();
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
            return BindingSession.ContainsPosition(syntax, position);
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

    private static bool ContainsPosition(AkburaSyntax syntax, int position)
    {
        return syntax.FullSpan.Contains(position) ||
               (syntax.FullWidth == 0 && position == syntax.Position);
    }
}
