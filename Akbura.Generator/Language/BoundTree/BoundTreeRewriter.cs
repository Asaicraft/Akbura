using Akbura.Language.Binder;
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
        return node.Update(declaredSymbols, statements);
    }

    public override BoundNode? VisitCSharpStatement(BoundCSharpStatement node)
    {
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        var children = VisitList(node.Children);
        return node.Update(bindingResult, children);
    }

    public override BoundNode? VisitBadStatement(BoundBadStatement node)
    {
        var children = VisitList(node.Children);
        return node.Update(children);
    }

    public override BoundNode? VisitLocalDeclarationStatement(BoundLocalDeclarationStatement node)
    {
        var locals = VisitCSharpSymbolList(node.Locals);
        var initializers = VisitExpressionList(node.Initializers);
        return node.Update(locals, initializers);
    }

    public override BoundNode? VisitUseHookStatement(BoundUseHookStatement node)
    {
        var invocation = (BoundUseHookInvocation)Visit(node.Invocation)!;
        return node.Update(invocation);
    }

    public override BoundNode? VisitUseHookInvocation(BoundUseHookInvocation node)
    {
        var hook = (IUseHookSymbol)VisitSymbol(node.Hook)!;
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        var arguments = VisitExpressionList(node.EffectiveArguments);
        return node.Update(hook, bindingResult, arguments);
    }

    public override BoundNode? VisitDeclaration(BoundDeclaration node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        return node.Update(symbolInfo, children);
    }

    public override BoundNode? VisitStateInitializer(BoundStateInitializer node)
    {
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        var useHook = (IUseHookSymbol?)VisitSymbol(node.UseHook);
        var invocation = (BoundUseHookInvocation?)Visit(node.UseHookInvocation);
        return node.Update(bindingResult, node.BindingKind, useHook, invocation);
    }

    public override BoundNode? VisitParamDefaultValue(BoundParamDefaultValue node)
    {
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        return node.Update(bindingResult);
    }

    public override BoundNode? VisitMarkupRoot(BoundMarkupRoot node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        return node.Update(symbolInfo, children);
    }

    public override BoundNode? VisitMarkupComponent(BoundMarkupComponent node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        if (symbolInfo.Equals(node.SymbolInfo) &&
            children == node.Children)
        {
            return node;
        }

        return new BoundMarkupComponent(
            node.Syntax,
            node.Binder,
            symbolInfo,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitMarkupContent(BoundMarkupContent node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        return node.Update(symbolInfo, children);
    }

    public override BoundNode? VisitMarkupContentSetter(BoundMarkupContentSetter node)
    {
        var containingComponent = (IMarkupComponentSymbol?)VisitSymbol(node.ContainingComponent);
        var property = (Akbura.Language.Symbols.IPropertySymbol?)VisitSymbol(node.Property);
        var content = VisitMarkupChildContentList(node.Content);
        var valueType = VisitCSharpSymbolDefinition(node.ValueType);
        var valueOperation = VisitCSharpOperationDefinition(node.ValueOperation);
        var valueConversion = VisitConversion(node.ValueConversion);

        return node.Update(
            containingComponent,
            property,
            node.ContentModel,
            content,
            valueType,
            valueOperation,
            valueConversion,
            node.LiteralValue,
            node.IsSynthesizedString);
    }

    public override BoundNode? VisitAkcssModule(BoundAkcssModule node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        if (symbolInfo.Equals(node.SymbolInfo) &&
            children == node.Children)
        {
            return node;
        }

        return new BoundAkcssModule(
            node.Syntax,
            node.Binder,
            symbolInfo,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitAkcssStyle(BoundAkcssStyle node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        if (symbolInfo.Equals(node.SymbolInfo) &&
            children == node.Children)
        {
            return node;
        }

        return new BoundAkcssStyle(
            node.Syntax,
            node.Binder,
            symbolInfo,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitAkcssUtility(BoundAkcssUtility node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        if (symbolInfo.Equals(node.SymbolInfo) &&
            children == node.Children)
        {
            return node;
        }

        return new BoundAkcssUtility(
            node.Syntax,
            node.Binder,
            symbolInfo,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitMarkupPropertySetter(BoundMarkupPropertySetter node)
    {
        var containingComponent = (IMarkupComponentSymbol?)VisitSymbol(node.ContainingComponent);
        var property = (Akbura.Language.Symbols.IPropertySymbol?)VisitSymbol(node.Property);
        var appliedAkcssSymbols = VisitAkburaSymbolList(node.AppliedAkcssSymbols);
        var valueType = VisitCSharpSymbolDefinition(node.ValueType);
        var valueOperation = VisitCSharpOperationDefinition(node.ValueOperation);
        var valueConversion = VisitConversion(node.ValueConversion);

        return node.Update(
            containingComponent,
            property,
            appliedAkcssSymbols,
            valueType,
            valueOperation,
            valueConversion,
            node.BindingKind,
            node.ValueKind,
            node.ValueSyntax,
            node.LiteralValue,
            node.ConvertedValue);
    }

    public override BoundNode? VisitMarkupCommandBinding(BoundMarkupCommandBinding node)
    {
        var containingComponent = (IMarkupComponentSymbol?)VisitSymbol(node.ContainingComponent);
        var property = (Akbura.Language.Symbols.IPropertySymbol)VisitSymbol(node.Property)!;
        var command = (ICommandSymbol)VisitSymbol(node.Command)!;
        var handlerType = VisitCSharpSymbolDefinition(node.HandlerType);
        var handlerResultType = VisitCSharpSymbolDefinition(node.HandlerResultType);
        var handlerOperation = VisitCSharpOperationDefinition(node.HandlerOperation);

        return node.Update(
            containingComponent,
            property,
            command,
            node.BindingKind,
            node.ValueKind,
            node.ValueSyntax,
            node.HandlerKind,
            node.ArgumentMode,
            node.ResultMode,
            node.HandlerParameterCount,
            node.IsAsync,
            node.ContainsAwait,
            handlerType,
            handlerResultType,
            handlerOperation);
    }

    public override BoundNode? VisitMarkupRoutedEventBinding(BoundMarkupRoutedEventBinding node)
    {
        var containingComponent = (IMarkupComponentSymbol?)VisitSymbol(node.ContainingComponent);
        var routedEvent = (IRoutedEventSymbol)VisitSymbol(node.RoutedEvent)!;
        var handlerOperation = VisitCSharpOperationDefinition(node.HandlerOperation);

        return node.Update(
            containingComponent,
            routedEvent,
            node.BindingKind,
            node.ValueKind,
            node.ValueSyntax,
            node.HandlerKind,
            node.ArgumentMode,
            node.HandlerParameterCount,
            node.IsAsync,
            node.ContainsAwait,
            handlerOperation);
    }

    public override BoundNode? VisitTailwindUtilityAttribute(BoundTailwindUtilityAttribute node)
    {
        var containingComponent = (IMarkupComponentSymbol?)VisitSymbol(node.ContainingComponent);
        var utility = (ITailwindUtilitySymbol?)VisitSymbol(node.Utility);
        var utilities = VisitAkburaSymbolList(node.Utilities);
        var arguments = VisitTailwindUtilityArgumentList(node.Arguments);
        var conditionType = VisitCSharpSymbolDefinition(node.ConditionType);
        var conditionOperation = VisitCSharpOperationDefinition(node.ConditionOperation);

        return node.Update(
            containingComponent,
            node.UtilityName,
            utility,
            utilities,
            arguments,
            node.HasCondition,
            node.ConditionText,
            conditionType,
            conditionOperation);
    }

    public override BoundNode? VisitAkcssPropertySetter(BoundAkcssPropertySetter node)
    {
        var containingAkcssSymbol = (IAkcssSymbol)VisitSymbol(node.ContainingAkcssSymbol)!;
        var property = (Akbura.Language.Symbols.IPropertySymbol?)VisitSymbol(node.Property);
        var valueType = VisitCSharpSymbolDefinition(node.ValueType);
        var valueOperation = VisitCSharpOperationDefinition(node.ValueOperation);
        var valueConversion = VisitConversion(node.ValueConversion);

        return node.Update(
            containingAkcssSymbol,
            property,
            valueType,
            valueOperation,
            valueConversion,
            node.ValueKind,
            node.RequiresBrushConversion,
            node.ConvertedValue);
    }

    public override BoundNode? VisitAkcssIf(BoundAkcssIf node)
    {
        var containingAkcssSymbol = (IAkcssSymbol)VisitSymbol(node.ContainingAkcssSymbol)!;
        var conditionType = VisitCSharpSymbolDefinition(node.ConditionType);
        var conditionOperation = VisitCSharpOperationDefinition(node.ConditionOperation);
        var operations = VisitAkcssOperationList(node.Operations);

        return node.Update(
            containingAkcssSymbol,
            conditionType,
            conditionOperation,
            operations);
    }

    public override BoundNode? VisitAkcssApply(BoundAkcssApply node)
    {
        var containingAkcssSymbol = (IAkcssSymbol)VisitSymbol(node.ContainingAkcssSymbol)!;
        var appliedSymbols = VisitAkburaSymbolList(node.AppliedSymbols);
        return node.Update(containingAkcssSymbol, node.Items, appliedSymbols);
    }

    public override BoundNode? VisitAkcssIntercept(BoundAkcssIntercept node)
    {
        var containingAkcssSymbol = (IAkcssSymbol)VisitSymbol(node.ContainingAkcssSymbol)!;
        var interceptType = VisitCSharpSymbolDefinition(node.InterceptType);
        return node.Update(containingAkcssSymbol, interceptType);
    }

    public override BoundNode? VisitExpression(BoundExpression node)
    {
        var symbolInfo = VisitSymbolInfo(node.SymbolInfo);
        var children = VisitList(node.Children);
        return node.Update(symbolInfo, children);
    }

    public override BoundNode? VisitCSharpExpression(BoundCSharpExpression node)
    {
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        return node.Update(bindingResult);
    }

    public override BoundNode? VisitLiteralExpression(BoundLiteralExpression node)
    {
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        return node.Update(bindingResult, node.ConstantValue);
    }

    public override BoundNode? VisitBinaryExpression(BoundBinaryExpression node)
    {
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        var left = (BoundExpression?)Visit(node.Left);
        var right = (BoundExpression?)Visit(node.Right);
        if (bindingResult.Equals(node.BindingResult) &&
            ReferenceEquals(left, node.Left) &&
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

        return node.Update(bindingResult, node.OperatorKind, left, right);
    }

    public override BoundNode? VisitCallExpression(BoundCallExpression node)
    {
        var bindingResult = VisitCSharpBindingResult(node.BindingResult);
        var targetMethod = (IMethodSymbol?)VisitSymbol(node.TargetMethod);
        var receiver = (BoundExpression?)Visit(node.Receiver);
        var arguments = VisitExpressionList(node.Arguments);
        if (bindingResult.Equals(node.BindingResult) &&
            SymbolEqualityComparer.Default.Equals(targetMethod, node.TargetMethod) &&
            ReferenceEquals(receiver, node.Receiver) &&
            arguments == node.Arguments)
        {
            return node;
        }

        return node.Update(bindingResult, targetMethod, receiver, arguments);
    }

    public override BoundNode? VisitConversionExpression(BoundConversionExpression node)
    {
        var operand = (BoundExpression?)Visit(node.Operand);
        var conversion = VisitConversion(node.Conversion);
        if (ReferenceEquals(operand, node.Operand) &&
            conversion.Equals(node.Conversion))
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

        return node.Update(operand, conversion);
    }

    public override BoundNode? VisitBadExpression(BoundBadExpression node)
    {
        var children = VisitList(node.Children);
        return node.Update(children);
    }

    public override BoundNode? VisitErrorExpression(BoundErrorExpression node)
    {
        var children = VisitList(node.Children);
        return node.Update(children);
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
                return VisitParameterSymbol((IParamSymbol)symbol);
            case AkburaSymbolKind.CommandParameter:
                return VisitCommandParameterSymbol((ICommandParameterSymbol)symbol);
            case AkburaSymbolKind.TailwindUtilityParameter:
                return VisitTailwindUtilityParameterSymbol((ITailwindUtilityParameterSymbol)symbol);
            case AkburaSymbolKind.MarkupItem:
                return VisitMarkupItemSymbol((IMarkupItemSymbol)symbol);
            case AkburaSymbolKind.InjectedService:
                return VisitInjectSymbol((IInjectSymbol)symbol);
            case AkburaSymbolKind.Command:
                return VisitCommandSymbol((ICommandSymbol)symbol);
            case AkburaSymbolKind.UseHook:
                return VisitUseHookSymbol((IUseHookSymbol)symbol);
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

    protected virtual AkburaSymbol VisitUseHookSymbol(IUseHookSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitPropertySymbol(Akbura.Language.Symbols.IPropertySymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitRoutedEventSymbol(IRoutedEventSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitAkcssModuleSymbol(IAkcssModuleSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitAkcssSymbol(IAkcssSymbol symbol) => DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitTailwindUtilitySymbol(ITailwindUtilitySymbol symbol) =>
        VisitAkcssSymbol(symbol);

    protected virtual AkburaSymbol VisitTailwindUtilityParameterSymbol(ITailwindUtilityParameterSymbol symbol) =>
        DefaultVisitSymbol(symbol);

    protected virtual AkburaSymbol VisitMarkupItemSymbol(IMarkupItemSymbol symbol) =>
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

    protected virtual CSharpBindingResult VisitCSharpBindingResult(CSharpBindingResult bindingResult)
    {
        var typeSymbol = (ITypeSymbol?)VisitSymbol(bindingResult.TypeSymbol);
        var symbol = VisitSymbol(bindingResult.Symbol);
        var receiverType = (ITypeSymbol?)VisitSymbol(bindingResult.ReceiverType);
        var candidateSymbols = VisitCSharpSymbolList(bindingResult.CandidateSymbols);
        var operationDefinition = VisitCSharpOperationDefinition(bindingResult.OperationDefinition);
        var conversion = VisitConversion(bindingResult.Conversion);

        if (SymbolEqualityComparer.Default.Equals(typeSymbol, bindingResult.TypeSymbol) &&
            SymbolEqualityComparer.Default.Equals(symbol, bindingResult.Symbol) &&
            SymbolEqualityComparer.Default.Equals(receiverType, bindingResult.ReceiverType) &&
            candidateSymbols == bindingResult.CandidateSymbols &&
            operationDefinition.Equals(bindingResult.OperationDefinition) &&
            conversion.Equals(bindingResult.Conversion))
        {
            return bindingResult;
        }

        return new CSharpBindingResult(
            typeSymbol,
            symbol,
            receiverType,
            bindingResult.IsBindingPath,
            candidateSymbols,
            bindingResult.CandidateReason,
            operationDefinition,
            bindingResult.Diagnostics,
            conversion);
    }

    protected virtual CSharpSymbolDefinition VisitCSharpSymbolDefinition(CSharpSymbolDefinition definition)
    {
        var symbol = VisitSymbol(definition.Symbol);
        if (SymbolEqualityComparer.Default.Equals(symbol, definition.Symbol))
        {
            return definition;
        }

        return symbol == null
            ? default
            : new CSharpSymbolDefinition(symbol);
    }

    protected virtual CSharpOperationDefinition VisitCSharpOperationDefinition(CSharpOperationDefinition definition)
    {
        return definition;
    }

    protected virtual AkburaConversion VisitConversion(AkburaConversion conversion)
    {
        var sourceType = (ITypeSymbol?)VisitSymbol(conversion.SourceType);
        var targetType = (ITypeSymbol?)VisitSymbol(conversion.TargetType);
        _ = VisitSymbol(conversion.MethodSymbol);

        if (SymbolEqualityComparer.Default.Equals(sourceType, conversion.SourceType) &&
            SymbolEqualityComparer.Default.Equals(targetType, conversion.TargetType))
        {
            return conversion;
        }

        return new AkburaConversion(
            conversion.Kind,
            sourceType,
            targetType,
            conversion.CSharpConversion);
    }

    protected virtual MarkupChildContent VisitMarkupChildContent(MarkupChildContent content)
    {
        var type = VisitCSharpSymbolDefinition(content.Type);
        var componentSymbol = (IMarkupComponentSymbol?)VisitSymbol(content.ComponentSymbol);

        if (type.Equals(content.Type) &&
            ReferenceEquals(componentSymbol, content.ComponentSymbol))
        {
            return content;
        }

        return new MarkupChildContent(
            content.Syntax,
            content.Kind,
            type,
            componentSymbol,
            content.Text);
    }

    protected virtual BoundTailwindUtilityArgument VisitTailwindUtilityArgument(
        BoundTailwindUtilityArgument argument)
    {
        var type = VisitCSharpSymbolDefinition(argument.Type);
        var valueOperation = VisitCSharpOperationDefinition(argument.ValueOperation);

        if (type.Equals(argument.Type) &&
            valueOperation.Equals(argument.ValueOperation))
        {
            return argument;
        }

        return new BoundTailwindUtilityArgument(
            argument.Syntax,
            argument.Text,
            type,
            valueOperation,
            argument.ConstantValue);
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

    protected virtual ImmutableArray<BoundAkcssOperation> VisitAkcssOperationList(
        ImmutableArray<BoundAkcssOperation> nodes)
    {
        if (nodes.IsDefaultOrEmpty)
        {
            return nodes.IsDefault ? ImmutableArray<BoundAkcssOperation>.Empty : nodes;
        }

        ArrayBuilder<BoundAkcssOperation>? builder = null;

        for (var index = 0; index < nodes.Length; index++)
        {
            var oldNode = nodes[index];
            var newNode = Visit(oldNode) as BoundAkcssOperation;

            if (builder == null)
            {
                if (ReferenceEquals(newNode, oldNode))
                {
                    continue;
                }

                builder = ArrayBuilder<BoundAkcssOperation>.GetInstance(nodes.Length);
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

    protected virtual ImmutableArray<MarkupChildContent> VisitMarkupChildContentList(
        ImmutableArray<MarkupChildContent> content)
    {
        if (content.IsDefaultOrEmpty)
        {
            return content.IsDefault ? ImmutableArray<MarkupChildContent>.Empty : content;
        }

        ArrayBuilder<MarkupChildContent>? builder = null;

        for (var index = 0; index < content.Length; index++)
        {
            var oldContent = content[index];
            var newContent = VisitMarkupChildContent(oldContent);

            if (builder == null)
            {
                if (newContent.Equals(oldContent))
                {
                    continue;
                }

                builder = ArrayBuilder<MarkupChildContent>.GetInstance(content.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(content[previous]);
                }
            }

            builder.Add(newContent);
        }

        return builder == null
            ? content
            : builder.ToImmutableAndFree();
    }

    protected virtual ImmutableArray<BoundTailwindUtilityArgument> VisitTailwindUtilityArgumentList(
        ImmutableArray<BoundTailwindUtilityArgument> arguments)
    {
        if (arguments.IsDefaultOrEmpty)
        {
            return arguments.IsDefault ? ImmutableArray<BoundTailwindUtilityArgument>.Empty : arguments;
        }

        ArrayBuilder<BoundTailwindUtilityArgument>? builder = null;

        for (var index = 0; index < arguments.Length; index++)
        {
            var oldArgument = arguments[index];
            var newArgument = VisitTailwindUtilityArgument(oldArgument);

            if (builder == null)
            {
                if (newArgument.Equals(oldArgument))
                {
                    continue;
                }

                builder = ArrayBuilder<BoundTailwindUtilityArgument>.GetInstance(arguments.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(arguments[previous]);
                }
            }

            builder.Add(newArgument);
        }

        return builder == null
            ? arguments
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

    protected virtual ImmutableArray<TSymbol> VisitAkburaSymbolList<TSymbol>(
        ImmutableArray<TSymbol> symbols)
        where TSymbol : class, AkburaSymbol
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
                if (ReferenceEquals(newSymbol, oldSymbol))
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
