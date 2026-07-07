using Akbura.Collections;
using Akbura.Language.BoundTree;
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
        if (IsCurrentSemanticModelSyntax(syntax) &&
            _semanticModel.GetMemberSemanticModel(syntax) is ExecutableMemberSemanticModel executableModel)
        {
            next = executableModel.CreateIncrementalBinder(next);
        }

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
            () => IsCurrentSemanticModelSyntax(syntax)
                ? _semanticModel.GetMemberSemanticModel(syntax).BindOperationSyntax(syntax)
                : GetOperationBinder(syntax).BindOperationSyntax(syntax));
    }

    public BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        return _semanticModel.GetBoundNode(
            syntax,
            () => IsCurrentSemanticModelSyntax(syntax)
                ? _semanticModel.GetMemberSemanticModel(syntax).BindSemanticSyntax(syntax)
                : GetSemanticBinder(syntax).BindSemanticSyntax(syntax));
    }

    private bool IsCurrentSemanticModelSyntax(AkburaSyntax syntax)
    {
        return SemanticSyntaxIdentity.IsInSameTree(
            syntax,
            _semanticModel.SyntaxTree.GetRoot());
    }

    internal Binder GetOperationBinder(AkburaSyntax syntax)
    {
        return GetBinder(syntax.Root, syntax.Position, GetOperationUsage(syntax.Kind));
    }

    internal Binder GetSemanticBinder(AkburaSyntax syntax)
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
        ImmutableArray<Declaration> path,
        BinderUsage usage)
    {
        if (path.IsDefaultOrEmpty)
        {
            return RootBinder;
        }

        var cacheSyntax = DeclarationFacts.GetSyntax(path[path.Length - 1]);
        var key = BinderFactory.BinderFactoryVisitor.CreateBinderCacheKey(
            cacheSyntax,
            usage);
        return _binderFactory.GetOrCreateBinder(
            key,
            path,
            usage);
    }

    private Binder GetBinderFromPath(
        ImmutableArray<Declaration> path,
        BinderUsage usage)
    {
        if (TryGetExecutableRootPath(path, out var executableRootPath))
        {
            var targetSyntax = DeclarationFacts.GetSyntax(path[path.Length - 1]);
            return GetExecutableCodeBinder(executableRootPath, usage)
                .GetBinder(targetSyntax) ?? RootBinder;
        }

        return GetOrCreateBinder(path, usage);
    }

    private bool TryFindDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<Declaration> path)
    {
        return _semanticModel.Compilation.DeclarationTable.TryGetDeclarationPath(
            syntax,
            out path);
    }

    private bool TryFindDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<Declaration> path)
    {
        return _semanticModel.Compilation.DeclarationTable.TryGetDeclarationPath(
            syntax,
            position,
            out path);
    }

    private ExecutableCodeBinder GetExecutableCodeBinder(
        ImmutableArray<Declaration> executableRootPath,
        BinderUsage usage)
    {
        var rootDeclaration = executableRootPath[executableRootPath.Length - 1];
        var key = new BinderCacheKey(DeclarationFacts.GetSyntax(rootDeclaration), usage);
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
        ImmutableArray<Declaration> path,
        out ImmutableArray<Declaration> executableRootPath)
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

    private static bool IsExecutableRoot(Declaration declaration)
    {
        return declaration.Kind is
            DeclarationKind.CSharpStatement or
            DeclarationKind.CSharpBlock;
    }

    private static ImmutableArray<Declaration> SlicePath(
        ImmutableArray<Declaration> path,
        int length)
    {
        if (length == 0)
        {
            return [];
        }

        using var builder = ImmutableArrayBuilder<Declaration>.Rent(length);
        for (var index = 0; index < length; index++)
        {
            builder.Add(path[index]);
        }

        return builder.ToImmutable();
    }

}
