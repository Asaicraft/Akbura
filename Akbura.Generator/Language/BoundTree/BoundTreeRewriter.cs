using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using RoslynSymbolKind = Microsoft.CodeAnalysis.SymbolKind;

namespace Akbura.Language.BoundTree;

internal class BoundTreeRewriter : BoundTreeVisitor<BoundNode?>
{
    private int _recursionDepth;

    public override BoundNode? Visit(BoundNode? node)
    {
        if (node == null)
        {
            return null;
        }

        _recursionDepth++;
        StackGuard.EnsureSufficientExecutionStack(_recursionDepth);

        try
        {
            return node.Accept(this);
        }
        finally
        {
            _recursionDepth--;
        }
    }

    public override BoundNode? DefaultVisit(BoundNode node)
    {
        return node;
    }

    public override BoundNode? VisitBlock(BoundBlock node)
    {
        var declaredSymbols = VisitAkburaSymbolList(node.DeclaredSymbols);
        var statements = VisitList(node.Statements);
        if (declaredSymbols == node.DeclaredSymbols &&
            statements == node.Statements)
        {
            return node;
        }

        return new BoundBlock(
            node.Syntax,
            node.Binder,
            declaredSymbols,
            statements,
            node.Diagnostics);
    }

    public override BoundNode? VisitBadStatement(BoundBadStatement node)
    {
        var children = VisitList(node.Children);
        if (children == node.Children)
        {
            return node;
        }

        return new BoundBadStatement(
            node.Syntax,
            node.Binder,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitLocalDeclarationStatement(BoundLocalDeclarationStatement node)
    {
        var locals = VisitCSharpSymbolList(node.Locals);
        var initializers = VisitExpressionList(node.Initializers);
        if (locals == node.Locals &&
            initializers == node.Initializers)
        {
            return node;
        }

        return new BoundLocalDeclarationStatement(
            node.Syntax,
            node.Binder,
            node.BindingResult,
            locals,
            initializers,
            node.Diagnostics);
    }

    public override BoundNode? VisitDeclaration(BoundDeclaration node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        if (symbolInfo.Equals(node.SymbolInfo) &&
            children == node.Children)
        {
            return node;
        }

        return new BoundDeclaration(
            node.Syntax,
            node.Binder,
            symbolInfo,
            node.Operation,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitExpression(BoundExpression node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        if (symbolInfo.Equals(node.SymbolInfo) &&
            children == node.Children)
        {
            return node;
        }

        return new BoundExpression(
            node.Syntax,
            node.Binder,
            symbolInfo,
            node.Operation,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitCSharpExpression(BoundCSharpExpression node)
    {
        return VisitExpression(node);
    }

    public override BoundNode? VisitLiteralExpression(BoundLiteralExpression node)
    {
        return VisitExpression(node);
    }

    public override BoundNode? VisitBinaryExpression(BoundBinaryExpression node)
    {
        var left = (BoundExpression?)Visit(node.Left);
        var right = (BoundExpression?)Visit(node.Right);
        if (ReferenceEquals(left, node.Left) &&
            ReferenceEquals(right, node.Right))
        {
            return node;
        }

        if (left == null || right == null)
        {
            return new BoundBadExpression(
                node.Syntax,
                node.Binder,
                node.Diagnostics);
        }

        return new BoundBinaryExpression(
            node.Syntax,
            node.Binder,
            node.BindingResult,
            node.OperatorKind,
            left,
            right,
            node.Diagnostics);
    }

    public override BoundNode? VisitCallExpression(BoundCallExpression node)
    {
        var targetMethod = (IMethodSymbol?)VisitSymbol(node.TargetMethod);
        var receiver = (BoundExpression?)Visit(node.Receiver);
        var arguments = VisitExpressionList(node.Arguments);
        if (SymbolEqualityComparer.Default.Equals(targetMethod, node.TargetMethod) &&
            ReferenceEquals(receiver, node.Receiver) &&
            arguments == node.Arguments)
        {
            return node;
        }

        return new BoundCallExpression(
            node.Syntax,
            node.Binder,
            node.BindingResult,
            targetMethod,
            receiver,
            arguments,
            node.Diagnostics);
    }

    public override BoundNode? VisitConversionExpression(BoundConversionExpression node)
    {
        var operand = (BoundExpression?)Visit(node.Operand);
        if (ReferenceEquals(operand, node.Operand))
        {
            return node;
        }

        if (operand == null)
        {
            return new BoundBadExpression(
                node.Syntax,
                node.Binder,
                node.Diagnostics);
        }

        return new BoundConversionExpression(
            node.Syntax,
            node.Binder,
            operand,
            node.Conversion,
            node.Diagnostics);
    }

    public override BoundNode? VisitBadExpression(BoundBadExpression node)
    {
        var children = VisitList(node.Children);
        if (children == node.Children)
        {
            return node;
        }

        return new BoundBadExpression(
            node.Syntax,
            node.Binder,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitErrorExpression(BoundErrorExpression node)
    {
        var children = VisitList(node.Children);
        if (children == node.Children)
        {
            return node;
        }

        return new BoundErrorExpression(
            node.Syntax,
            node.Binder,
            node.Diagnostics,
            children);
    }

    [return: NotNullIfNotNull(nameof(symbol))]
    public virtual AkburaSymbol? VisitSymbol(AkburaSymbol? symbol)
    {
        if (symbol == null)
        {
            return null;
        }

        switch (symbol.Kind)
        {
            case AkburaSymbolKind.AkburaComponent:
                return VisitAkburaComponentSymbol((IAkburaComponentSymbol)symbol);
            case AkburaSymbolKind.MarkupComponent:
                return VisitMarkupComponentSymbol((IMarkupComponentSymbol)symbol);
            case AkburaSymbolKind.State:
                return VisitStateSymbol((IStateSymbol)symbol);
            case AkburaSymbolKind.Parameter:
                return VisitParameterLikeSymbol(symbol);
            case AkburaSymbolKind.InjectedService:
                return VisitInjectSymbol((IInjectSymbol)symbol);
            case AkburaSymbolKind.Command:
                return VisitCommandSymbol((ICommandSymbol)symbol);
            case AkburaSymbolKind.UseEffect:
                return VisitUseEffectSymbol((IUseEffectSymbol)symbol);
            case AkburaSymbolKind.UserHook:
                return VisitUserHookSymbol((IUserHookSymbol)symbol);
            case AkburaSymbolKind.Property:
                return VisitPropertySymbol((Akbura.Language.Symbols.IPropertySymbol)symbol);
            case AkburaSymbolKind.Event:
                return VisitRoutedEventSymbol((IRoutedEventSymbol)symbol);
            case AkburaSymbolKind.AkcssModule:
                return VisitAkcssModuleSymbol((IAkcssModuleSymbol)symbol);
            case AkburaSymbolKind.AkcssUtility:
                return VisitTailwindUtilitySymbol((ITailwindUtilitySymbol)symbol);
            case AkburaSymbolKind.AkcssClass:
                return VisitAkcssSymbol((IAkcssSymbol)symbol);
            default:
                return DefaultVisitSymbol(symbol);
        }
    }

    [return: NotNullIfNotNull(nameof(symbol))]
    public virtual RoslynSymbol? VisitSymbol(RoslynSymbol? symbol)
    {
        if (symbol == null)
        {
            return null;
        }

        switch (symbol.Kind)
        {
            case RoslynSymbolKind.Alias:
                return VisitAliasSymbol((IAliasSymbol)symbol);
            case RoslynSymbolKind.Discard:
                return VisitDiscardSymbol((IDiscardSymbol)symbol);
            case RoslynSymbolKind.Event:
                return VisitEventSymbol((IEventSymbol)symbol);
            case RoslynSymbolKind.Field:
                return VisitFieldSymbol((IFieldSymbol)symbol);
            case RoslynSymbolKind.Label:
                return VisitLabelSymbol((ILabelSymbol)symbol);
            case RoslynSymbolKind.Local:
                return VisitLocalSymbol((ILocalSymbol)symbol);
            case RoslynSymbolKind.Method:
                return VisitMethodSymbol((IMethodSymbol)symbol);
            case RoslynSymbolKind.Namespace:
                return VisitNamespaceSymbol((INamespaceSymbol)symbol);
            case RoslynSymbolKind.Parameter:
                return VisitParameterSymbol((IParameterSymbol)symbol);
            case RoslynSymbolKind.Property:
                return VisitPropertySymbol((Microsoft.CodeAnalysis.IPropertySymbol)symbol);
            case RoslynSymbolKind.RangeVariable:
                return VisitRangeVariableSymbol((IRangeVariableSymbol)symbol);
            default:
                return symbol is ITypeSymbol type
                    ? VisitTypeSymbol(type)
                    : DefaultVisitSymbol(symbol);
        }
    }

    protected virtual AkburaSymbol DefaultVisitSymbol(AkburaSymbol symbol) => symbol;

    protected virtual AkburaSymbol VisitParameterLikeSymbol(AkburaSymbol symbol)
    {
        if (symbol is ICommandParameterSymbol commandParameter)
        {
            return VisitCommandParameterSymbol(commandParameter);
        }

        if (symbol is ITailwindUtilityParameterSymbol utilityParameter)
        {
            return VisitTailwindUtilityParameterSymbol(utilityParameter);
        }

        return VisitParameterSymbol((IParamSymbol)symbol);
    }

    protected virtual AkburaSymbol VisitAkburaComponentSymbol(IAkburaComponentSymbol symbol) =>
        VisitMarkupComponentSymbol(symbol);

    protected virtual AkburaSymbol VisitMarkupComponentSymbol(IMarkupComponentSymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitStateSymbol(IStateSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitParameterSymbol(IParamSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitInjectSymbol(IInjectSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitCommandSymbol(ICommandSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitCommandParameterSymbol(ICommandParameterSymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitUseEffectSymbol(IUseEffectSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitUserHookSymbol(IUserHookSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitPropertySymbol(Akbura.Language.Symbols.IPropertySymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitRoutedEventSymbol(IRoutedEventSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitAkcssModuleSymbol(IAkcssModuleSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitAkcssSymbol(IAkcssSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitTailwindUtilitySymbol(ITailwindUtilitySymbol symbol) =>
        VisitAkcssSymbol(symbol);

    protected virtual AkburaSymbol VisitTailwindUtilityParameterSymbol(ITailwindUtilityParameterSymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol DefaultVisitSymbol(RoslynSymbol symbol) => symbol;

    protected virtual RoslynSymbol VisitAliasSymbol(IAliasSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitDiscardSymbol(IDiscardSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitEventSymbol(IEventSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitFieldSymbol(IFieldSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitLabelSymbol(ILabelSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitLocalSymbol(ILocalSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitMethodSymbol(IMethodSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitNamespaceSymbol(INamespaceSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitParameterSymbol(IParameterSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitPropertySymbol(Microsoft.CodeAnalysis.IPropertySymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitRangeVariableSymbol(IRangeVariableSymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual RoslynSymbol VisitTypeSymbol(ITypeSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbolInfo VisitSymbolInfo(AkburaSymbolInfo symbolInfo)
    {
        var symbol = VisitSymbol(symbolInfo.Symbol);
        var candidateSymbols = VisitAkburaSymbolList(symbolInfo.CandidateSymbols);

        if (ReferenceEquals(symbol, symbolInfo.Symbol) &&
            candidateSymbols == symbolInfo.CandidateSymbols)
        {
            return symbolInfo;
        }

        if (symbol != null)
        {
            return AkburaSymbolInfo.Success(symbol);
        }

        return candidateSymbols.Length == 0
            ? AkburaSymbolInfo.None(symbolInfo.CandidateReason)
            : AkburaSymbolInfo.Candidates(candidateSymbols, symbolInfo.CandidateReason);
    }

    protected virtual ImmutableArray<BoundNode> VisitList(ImmutableArray<BoundNode> nodes)
    {
        if (nodes.IsDefaultOrEmpty)
        {
            return nodes.IsDefault ? ImmutableArray<BoundNode>.Empty : nodes;
        }

        ArrayBuilder<BoundNode>? builder = null;

        for (var index = 0; index < nodes.Length; index++)
        {
            var oldNode = nodes[index];
            var newNode = Visit(oldNode);

            if (builder == null)
            {
                if (ReferenceEquals(newNode, oldNode))
                {
                    continue;
                }

                builder = ArrayBuilder<BoundNode>.GetInstance(nodes.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(nodes[previous]);
                }
            }

            if (newNode != null)
            {
                builder.Add(newNode);
            }
        }

        return builder == null
            ? nodes
            : builder.ToImmutableAndFree();
    }

    protected virtual ImmutableArray<BoundExpression> VisitExpressionList(ImmutableArray<BoundExpression> nodes)
    {
        if (nodes.IsDefaultOrEmpty)
        {
            return nodes.IsDefault ? ImmutableArray<BoundExpression>.Empty : nodes;
        }

        ArrayBuilder<BoundExpression>? builder = null;

        for (var index = 0; index < nodes.Length; index++)
        {
            var oldNode = nodes[index];
            var newNode = (BoundExpression?)Visit(oldNode);

            if (builder == null)
            {
                if (ReferenceEquals(newNode, oldNode))
                {
                    continue;
                }

                builder = ArrayBuilder<BoundExpression>.GetInstance(nodes.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(nodes[previous]);
                }
            }

            if (newNode != null)
            {
                builder.Add(newNode);
            }
        }

        return builder == null
            ? nodes
            : builder.ToImmutableAndFree();
    }

    protected virtual ImmutableArray<AkburaSymbol> VisitAkburaSymbolList(
        ImmutableArray<AkburaSymbol> symbols)
    {
        if (symbols.IsDefaultOrEmpty)
        {
            return symbols.IsDefault ? ImmutableArray<AkburaSymbol>.Empty : symbols;
        }

        ArrayBuilder<AkburaSymbol>? builder = null;

        for (var index = 0; index < symbols.Length; index++)
        {
            var oldSymbol = symbols[index];
            var newSymbol = VisitSymbol(oldSymbol);

            if (builder == null)
            {
                if (ReferenceEquals(newSymbol, oldSymbol))
                {
                    continue;
                }

                builder = ArrayBuilder<AkburaSymbol>.GetInstance(symbols.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(symbols[previous]);
                }
            }

            if (newSymbol != null)
            {
                builder.Add(newSymbol);
            }
        }

        return builder == null
            ? symbols
            : builder.ToImmutableAndFree();
    }

    protected virtual ImmutableArray<TSymbol> VisitCSharpSymbolList<TSymbol>(
        ImmutableArray<TSymbol> symbols)
        where TSymbol : class, RoslynSymbol
    {
        if (symbols.IsDefaultOrEmpty)
        {
            return symbols.IsDefault ? ImmutableArray<TSymbol>.Empty : symbols;
        }

        ArrayBuilder<TSymbol>? builder = null;

        for (var index = 0; index < symbols.Length; index++)
        {
            var oldSymbol = symbols[index];
            var newSymbol = VisitSymbol(oldSymbol) as TSymbol;

            if (builder == null)
            {
                if (SymbolEqualityComparer.Default.Equals(newSymbol, oldSymbol))
                {
                    continue;
                }

                builder = ArrayBuilder<TSymbol>.GetInstance(symbols.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(symbols[previous]);
                }
            }

            if (newSymbol != null)
            {
                builder.Add(newSymbol);
            }
        }

        return builder == null
            ? symbols
            : builder.ToImmutableAndFree();
    }
}
