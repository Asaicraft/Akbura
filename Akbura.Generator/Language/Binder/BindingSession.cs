using Akbura.Collections;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class BindingSession
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly BinderFactory _binderFactory;
    private readonly ConcurrentCache<BinderCacheKey, Binder> _blockBinderCache;

    public BindingSession(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        RootBinder = new CompilationBinder(semanticModel);
        MarkupDataTypes = new MarkupDataTypeResolver(semanticModel);
        _binderFactory = new BinderFactory(semanticModel, this);
        _blockBinderCache = new ConcurrentCache<BinderCacheKey, Binder>(32);
    }

    public CompilationBinder RootBinder { get; }

    public MarkupDataTypeResolver MarkupDataTypes { get; }

    public int CachedBinderCount => _binderFactory.CachedBinderCount;

    public Binder GetBinder(AkburaSyntax syntax, BinderUsage usage = BinderUsage.Default)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if ((!TryFindDeclarationPath(syntax, out var path) ||
             path.Length == 0) &&
            (!TryFindDeclarationPath(syntax.Root, syntax.Position, out path) ||
             path.Length == 0))
        {
            return AddContainingBlockBinders(RootBinder, syntax, usage);
        }

        var binder = GetOrCreateBinder(path, usage);
        return AddContainingBlockBinders(binder, syntax, usage);
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
            return AddContainingBlockBinders(RootBinder, syntax, normalizedPosition, usage);
        }

        var binder = GetOrCreateBinder(path, usage);
        return AddContainingBlockBinders(binder, syntax, normalizedPosition, usage);
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
                AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax or
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
                AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax or
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

    internal Binder AddContainingBlockBinders(
        Binder binder,
        AkburaSyntax syntax,
        BinderUsage usage)
    {
        return AddContainingBlockBinders(
            binder,
            GetContainingCSharpBlocks(syntax),
            usage);
    }

    private Binder AddContainingBlockBinders(
        Binder binder,
        AkburaSyntax syntax,
        int position,
        BinderUsage usage)
    {
        return AddContainingBlockBinders(
            binder,
            GetContainingCSharpBlocks(syntax, position),
            usage);
    }

    private Binder AddContainingBlockBinders(
        Binder binder,
        ImmutableArray<CSharpBlockSyntax> blocks,
        BinderUsage usage)
    {
        var current = binder;
        foreach (var block in blocks)
        {
            if (BinderChainContainsScope(current, block))
            {
                continue;
            }

            current = GetOrCreateBlockBinder(block, current, usage);
        }

        return current;
    }

    private Binder GetOrCreateBlockBinder(
        CSharpBlockSyntax block,
        Binder next,
        BinderUsage usage)
    {
        var key = new BinderCacheKey(block, usage);
        if (_blockBinderCache.TryGetValue(key, out var binder))
        {
            return binder;
        }

        binder = new BlockBinder(
            _semanticModel,
            next,
            block,
            next.Flags | GetUsageFlags(usage));
        if (!_blockBinderCache.TryAdd(key, binder) &&
            _blockBinderCache.TryGetValue(key, out var cachedBinder))
        {
            return cachedBinder;
        }

        return binder;
    }

    private static ImmutableArray<CSharpBlockSyntax> GetContainingCSharpBlocks(AkburaSyntax syntax)
    {
        var builder = ArrayBuilder<CSharpBlockSyntax>.GetInstance();
        for (var node = syntax; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.CSharpBlockSyntax)
            {
                builder.Add(Unsafe.As<CSharpBlockSyntax>(node));
            }
        }

        builder.ReverseContents();
        return builder.ToImmutableAndFree();
    }

    private static ImmutableArray<CSharpBlockSyntax> GetContainingCSharpBlocks(
        AkburaSyntax syntax,
        int position)
    {
        var builder = ArrayBuilder<CSharpBlockSyntax>.GetInstance();
        foreach (var node in syntax.DescendantNodesAndSelf(
                     candidate => ContainsPosition(candidate, position)))
        {
            if (node.Kind == AkburaSyntaxKind.CSharpBlockSyntax &&
                ContainsPosition(node, position))
            {
                builder.Add(Unsafe.As<CSharpBlockSyntax>(node));
            }
        }

        return builder.ToImmutableAndFree();
    }

    private static bool BinderChainContainsScope(
        Binder binder,
        CSharpBlockSyntax block)
    {
        for (var current = binder; current != null; current = current.Next)
        {
            if (current.ScopeDesignator != null &&
                SemanticSyntaxIdentity.Equals(current.ScopeDesignator, block))
            {
                return true;
            }
        }

        return false;
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

    private static bool ContainsPosition(AkburaSyntax syntax, int position)
    {
        return syntax.FullSpan.Contains(position) ||
               (syntax.FullWidth == 0 && position == syntax.Position);
    }

}
