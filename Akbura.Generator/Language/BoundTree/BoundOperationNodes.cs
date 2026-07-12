using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal readonly struct BoundTailwindUtilityArgument
{
    public BoundTailwindUtilityArgument(
        TailwindSegmentSyntax syntax,
        string text,
        CSharpSymbolDefinition type,
        CSharpOperationDefinition valueOperation,
        object? constantValue)
    {
        Syntax = syntax;
        Text = text;
        Type = type;
        ValueOperation = valueOperation;
        ConstantValue = constantValue;
    }

    public TailwindSegmentSyntax Syntax { get; }

    public string Text { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public object? ConstantValue { get; }
}

internal abstract class BoundMarkupAttribute : BoundNode
{
    protected BoundMarkupAttribute(
        BoundKind kind,
        MarkupAttributeSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default,
        bool hasErrors = false)
        : base(kind, syntax, binder, symbolInfo, diagnostics, children, hasErrors)
    {
        ContainingComponent = containingComponent;
    }

    public new MarkupAttributeSyntax Syntax => (MarkupAttributeSyntax)base.Syntax;

    public IMarkupComponentSymbol? ContainingComponent { get; }
}

internal sealed class BoundMarkupContentSetter : BoundNode
{
    public BoundMarkupContentSetter(
        MarkupElementSyntax syntax,
        BinderType binder,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        MarkupContentModel contentModel,
        ImmutableArray<MarkupChildContent> content,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        string? literalValue,
        bool isSynthesizedString,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.MarkupContentSetter,
            syntax,
            binder,
            property == null ? AkburaSymbolInfo.None(CandidateReason.NotFound) : AkburaSymbolInfo.Success(property),
            diagnostics,
            hasErrors: hasErrors)
    {
        ContainingComponent = containingComponent;
        Property = property;
        ContentModel = contentModel;
        Content = content.IsDefault
            ? ImmutableArray<MarkupChildContent>.Empty
            : content;
        ValueType = valueType;
        ValueOperation = valueOperation;
        ValueConversion = valueConversion;
        LiteralValue = literalValue;
        IsSynthesizedString = isSynthesizedString;
    }

    public new MarkupElementSyntax Syntax => (MarkupElementSyntax)base.Syntax;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol? Property { get; }

    public MarkupContentModel ContentModel { get; }

    public ImmutableArray<MarkupChildContent> Content { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public AkburaConversion ValueConversion { get; }

    public string? LiteralValue { get; }

    public bool IsSynthesizedString { get; }

    public BoundMarkupContentSetter Update(
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        MarkupContentModel contentModel,
        ImmutableArray<MarkupChildContent> content,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        string? literalValue,
        bool isSynthesizedString)
    {
        if (ReferenceEquals(containingComponent, ContainingComponent) &&
            ReferenceEquals(property, Property) &&
            contentModel.Equals(ContentModel) &&
            content == Content &&
            valueType.Equals(ValueType) &&
            valueOperation.Equals(ValueOperation) &&
            valueConversion.Equals(ValueConversion) &&
            literalValue == LiteralValue &&
            isSynthesizedString == IsSynthesizedString)
        {
            return this;
        }

        return new BoundMarkupContentSetter(
            Syntax,
            Binder,
            containingComponent,
            property,
            contentModel,
            content,
            valueType,
            valueOperation,
            valueConversion,
            literalValue,
            isSynthesizedString,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupContentSetter(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupContentSetter(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupContentSetter(this, parameter);
}

internal sealed class BoundMarkupPropertySetter : BoundMarkupAttribute
{
    public BoundMarkupPropertySetter(
        MarkupAttributeSyntax syntax,
        BinderType binder,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        ImmutableArray<IAkcssSymbol> appliedAkcssSymbols,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        string? literalValue,
        object? convertedValue,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.MarkupPropertySetter,
            syntax,
            binder,
            property == null ? AkburaSymbolInfo.None(CandidateReason.NotFound) : AkburaSymbolInfo.Success(property),
            containingComponent,
            diagnostics,
            hasErrors: hasErrors)
    {
        Property = property;
        AppliedAkcssSymbols = appliedAkcssSymbols.IsDefault
            ? ImmutableArray<IAkcssSymbol>.Empty
            : appliedAkcssSymbols;
        ValueType = valueType;
        ValueOperation = valueOperation;
        ValueConversion = valueConversion;
        BindingKind = bindingKind;
        ValueKind = valueKind;
        ValueSyntax = valueSyntax;
        LiteralValue = literalValue;
        ConvertedValue = convertedValue;
    }

    public IPropertySymbol? Property { get; }

    public ImmutableArray<IAkcssSymbol> AppliedAkcssSymbols { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public AkburaConversion ValueConversion { get; }

    public MarkupAttributeBindingKind BindingKind { get; }

    public MarkupAttributeValueKind ValueKind { get; }

    public MarkupAttributeValueSyntax? ValueSyntax { get; }

    public string? LiteralValue { get; }

    public object? ConvertedValue { get; }

    public BoundMarkupPropertySetter Update(
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        ImmutableArray<IAkcssSymbol> appliedAkcssSymbols,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        string? literalValue,
        object? convertedValue)
    {
        if (ReferenceEquals(containingComponent, ContainingComponent) &&
            ReferenceEquals(property, Property) &&
            appliedAkcssSymbols == AppliedAkcssSymbols &&
            valueType.Equals(ValueType) &&
            valueOperation.Equals(ValueOperation) &&
            valueConversion.Equals(ValueConversion) &&
            bindingKind == BindingKind &&
            valueKind == ValueKind &&
            ReferenceEquals(valueSyntax, ValueSyntax) &&
            literalValue == LiteralValue &&
            Equals(convertedValue, ConvertedValue))
        {
            return this;
        }

        return new BoundMarkupPropertySetter(
            Syntax,
            Binder,
            containingComponent,
            property,
            appliedAkcssSymbols,
            valueType,
            valueOperation,
            valueConversion,
            bindingKind,
            valueKind,
            valueSyntax,
            literalValue,
            convertedValue,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupPropertySetter(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupPropertySetter(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupPropertySetter(this, parameter);
}

internal sealed class BoundMarkupCommandBinding : BoundMarkupAttribute
{
    public BoundMarkupCommandBinding(
        MarkupAttributeSyntax syntax,
        BinderType binder,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol property,
        ICommandSymbol command,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        MarkupCommandHandlerKind handlerKind,
        MarkupCommandArgumentMode argumentMode,
        MarkupCommandResultMode resultMode,
        int handlerParameterCount,
        bool isAsync,
        bool containsAwait,
        CSharpSymbolDefinition handlerType,
        CSharpSymbolDefinition handlerResultType,
        CSharpOperationDefinition handlerOperation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.MarkupCommandBinding,
            syntax,
            binder,
            AkburaSymbolInfo.Success(command),
            containingComponent,
            diagnostics,
            hasErrors: hasErrors)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        Command = command ?? throw new ArgumentNullException(nameof(command));
        BindingKind = bindingKind;
        ValueKind = valueKind;
        ValueSyntax = valueSyntax;
        HandlerKind = handlerKind;
        ArgumentMode = argumentMode;
        ResultMode = resultMode;
        HandlerParameterCount = handlerParameterCount;
        IsAsync = isAsync;
        ContainsAwait = containsAwait;
        HandlerType = handlerType;
        HandlerResultType = handlerResultType;
        HandlerOperation = handlerOperation;
    }

    public IPropertySymbol Property { get; }

    public ICommandSymbol Command { get; }

    public MarkupAttributeBindingKind BindingKind { get; }

    public MarkupAttributeValueKind ValueKind { get; }

    public MarkupAttributeValueSyntax? ValueSyntax { get; }

    public MarkupCommandHandlerKind HandlerKind { get; }

    public MarkupCommandArgumentMode ArgumentMode { get; }

    public MarkupCommandResultMode ResultMode { get; }

    public int HandlerParameterCount { get; }

    public bool IsAsync { get; }

    public bool ContainsAwait { get; }

    public CSharpSymbolDefinition HandlerType { get; }

    public CSharpSymbolDefinition HandlerResultType { get; }

    public CSharpOperationDefinition HandlerOperation { get; }

    public BoundMarkupCommandBinding Update(
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol property,
        ICommandSymbol command,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        MarkupCommandHandlerKind handlerKind,
        MarkupCommandArgumentMode argumentMode,
        MarkupCommandResultMode resultMode,
        int handlerParameterCount,
        bool isAsync,
        bool containsAwait,
        CSharpSymbolDefinition handlerType,
        CSharpSymbolDefinition handlerResultType,
        CSharpOperationDefinition handlerOperation)
    {
        if (ReferenceEquals(containingComponent, ContainingComponent) &&
            ReferenceEquals(property, Property) &&
            ReferenceEquals(command, Command) &&
            bindingKind == BindingKind &&
            valueKind == ValueKind &&
            ReferenceEquals(valueSyntax, ValueSyntax) &&
            handlerKind == HandlerKind &&
            argumentMode == ArgumentMode &&
            resultMode == ResultMode &&
            handlerParameterCount == HandlerParameterCount &&
            isAsync == IsAsync &&
            containsAwait == ContainsAwait &&
            handlerType.Equals(HandlerType) &&
            handlerResultType.Equals(HandlerResultType) &&
            handlerOperation.Equals(HandlerOperation))
        {
            return this;
        }

        return new BoundMarkupCommandBinding(
            Syntax,
            Binder,
            containingComponent,
            property,
            command,
            bindingKind,
            valueKind,
            valueSyntax,
            handlerKind,
            argumentMode,
            resultMode,
            handlerParameterCount,
            isAsync,
            containsAwait,
            handlerType,
            handlerResultType,
            handlerOperation,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupCommandBinding(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupCommandBinding(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupCommandBinding(this, parameter);
}

internal sealed class BoundMarkupRoutedEventBinding : BoundMarkupAttribute
{
    public BoundMarkupRoutedEventBinding(
        MarkupAttributeSyntax syntax,
        BinderType binder,
        IMarkupComponentSymbol? containingComponent,
        IRoutedEventSymbol routedEvent,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        MarkupCommandHandlerKind handlerKind,
        MarkupCommandArgumentMode argumentMode,
        int handlerParameterCount,
        bool isAsync,
        bool containsAwait,
        CSharpOperationDefinition handlerOperation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.MarkupRoutedEventBinding,
            syntax,
            binder,
            AkburaSymbolInfo.Success(routedEvent),
            containingComponent,
            diagnostics,
            hasErrors: hasErrors)
    {
        RoutedEvent = routedEvent ?? throw new ArgumentNullException(nameof(routedEvent));
        BindingKind = bindingKind;
        ValueKind = valueKind;
        ValueSyntax = valueSyntax;
        HandlerKind = handlerKind;
        ArgumentMode = argumentMode;
        HandlerParameterCount = handlerParameterCount;
        IsAsync = isAsync;
        ContainsAwait = containsAwait;
        HandlerOperation = handlerOperation;
    }

    public IRoutedEventSymbol RoutedEvent { get; }

    public MarkupAttributeBindingKind BindingKind { get; }

    public MarkupAttributeValueKind ValueKind { get; }

    public MarkupAttributeValueSyntax? ValueSyntax { get; }

    public MarkupCommandHandlerKind HandlerKind { get; }

    public MarkupCommandArgumentMode ArgumentMode { get; }

    public int HandlerParameterCount { get; }

    public bool IsAsync { get; }

    public bool ContainsAwait { get; }

    public CSharpOperationDefinition HandlerOperation { get; }

    public BoundMarkupRoutedEventBinding Update(
        IMarkupComponentSymbol? containingComponent,
        IRoutedEventSymbol routedEvent,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        MarkupCommandHandlerKind handlerKind,
        MarkupCommandArgumentMode argumentMode,
        int handlerParameterCount,
        bool isAsync,
        bool containsAwait,
        CSharpOperationDefinition handlerOperation)
    {
        if (ReferenceEquals(containingComponent, ContainingComponent) &&
            ReferenceEquals(routedEvent, RoutedEvent) &&
            bindingKind == BindingKind &&
            valueKind == ValueKind &&
            ReferenceEquals(valueSyntax, ValueSyntax) &&
            handlerKind == HandlerKind &&
            argumentMode == ArgumentMode &&
            handlerParameterCount == HandlerParameterCount &&
            isAsync == IsAsync &&
            containsAwait == ContainsAwait &&
            handlerOperation.Equals(HandlerOperation))
        {
            return this;
        }

        return new BoundMarkupRoutedEventBinding(
            Syntax,
            Binder,
            containingComponent,
            routedEvent,
            bindingKind,
            valueKind,
            valueSyntax,
            handlerKind,
            argumentMode,
            handlerParameterCount,
            isAsync,
            containsAwait,
            handlerOperation,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupRoutedEventBinding(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupRoutedEventBinding(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupRoutedEventBinding(this, parameter);
}

internal sealed class BoundTailwindUtilityAttribute : BoundMarkupAttribute
{
    public BoundTailwindUtilityAttribute(
        TailwindAttributeSyntax syntax,
        BinderType binder,
        IMarkupComponentSymbol? containingComponent,
        string utilityName,
        ITailwindUtilitySymbol? utility,
        ImmutableArray<ITailwindUtilitySymbol> utilities,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        bool hasCondition,
        string? conditionText,
        CSharpSymbolDefinition conditionType,
        CSharpOperationDefinition conditionOperation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.TailwindUtilityAttribute,
            syntax,
            binder,
            utility == null ? AkburaSymbolInfo.None(CandidateReason.NotFound) : AkburaSymbolInfo.Success(utility),
            containingComponent,
            diagnostics,
            hasErrors: hasErrors)
    {
        UtilityName = utilityName;
        Utility = utility;
        Utilities = utilities.IsDefault
            ? ImmutableArray<ITailwindUtilitySymbol>.Empty
            : utilities;
        Arguments = arguments.IsDefault
            ? ImmutableArray<BoundTailwindUtilityArgument>.Empty
            : arguments;
        HasCondition = hasCondition;
        ConditionText = conditionText;
        ConditionType = conditionType;
        ConditionOperation = conditionOperation;
    }

    public new TailwindAttributeSyntax Syntax => (TailwindAttributeSyntax)base.Syntax;

    public string UtilityName { get; }

    public ITailwindUtilitySymbol? Utility { get; }

    public ImmutableArray<ITailwindUtilitySymbol> Utilities { get; }

    public ImmutableArray<BoundTailwindUtilityArgument> Arguments { get; }

    public bool HasCondition { get; }

    public string? ConditionText { get; }

    public CSharpSymbolDefinition ConditionType { get; }

    public CSharpOperationDefinition ConditionOperation { get; }

    public BoundTailwindUtilityAttribute Update(
        IMarkupComponentSymbol? containingComponent,
        string utilityName,
        ITailwindUtilitySymbol? utility,
        ImmutableArray<ITailwindUtilitySymbol> utilities,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        bool hasCondition,
        string? conditionText,
        CSharpSymbolDefinition conditionType,
        CSharpOperationDefinition conditionOperation)
    {
        if (ReferenceEquals(containingComponent, ContainingComponent) &&
            utilityName == UtilityName &&
            ReferenceEquals(utility, Utility) &&
            utilities == Utilities &&
            arguments == Arguments &&
            hasCondition == HasCondition &&
            conditionText == ConditionText &&
            conditionType.Equals(ConditionType) &&
            conditionOperation.Equals(ConditionOperation))
        {
            return this;
        }

        return new BoundTailwindUtilityAttribute(
            Syntax,
            Binder,
            containingComponent,
            utilityName,
            utility,
            utilities,
            arguments,
            hasCondition,
            conditionText,
            conditionType,
            conditionOperation,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitTailwindUtilityAttribute(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitTailwindUtilityAttribute(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitTailwindUtilityAttribute(this, parameter);
}

internal abstract class BoundAkcssOperation : BoundNode
{
    protected BoundAkcssOperation(
        BoundKind kind,
        AkburaSyntax syntax,
        BinderType binder,
        IAkcssSymbol containingAkcssSymbol,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default,
        bool hasErrors = false)
        : base(kind, syntax, binder, symbolInfo, diagnostics, children, hasErrors)
    {
        ContainingAkcssSymbol = containingAkcssSymbol ?? throw new ArgumentNullException(nameof(containingAkcssSymbol));
    }

    public IAkcssSymbol ContainingAkcssSymbol { get; }
}

internal sealed class BoundAkcssPropertySetter : BoundAkcssOperation
{
    public BoundAkcssPropertySetter(
        AkcssAssignmentSyntax syntax,
        BinderType binder,
        IAkcssSymbol containingAkcssSymbol,
        IPropertySymbol? property,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        AkcssPropertyValueKind valueKind,
        bool requiresBrushConversion,
        object? convertedValue,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.AkcssPropertySetter,
            syntax,
            binder,
            containingAkcssSymbol,
            property == null ? AkburaSymbolInfo.None(CandidateReason.NotFound) : AkburaSymbolInfo.Success(property),
            diagnostics,
            hasErrors: hasErrors)
    {
        Property = property;
        ValueType = valueType;
        ValueOperation = valueOperation;
        ValueConversion = valueConversion;
        ValueKind = valueKind;
        RequiresBrushConversion = requiresBrushConversion;
        ConvertedValue = convertedValue;
    }

    public new AkcssAssignmentSyntax Syntax => (AkcssAssignmentSyntax)base.Syntax;

    public IPropertySymbol? Property { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public AkburaConversion ValueConversion { get; }

    public AkcssPropertyValueKind ValueKind { get; }

    public bool RequiresBrushConversion { get; }

    public object? ConvertedValue { get; }

    public BoundAkcssPropertySetter Update(
        IAkcssSymbol containingAkcssSymbol,
        IPropertySymbol? property,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        AkcssPropertyValueKind valueKind,
        bool requiresBrushConversion,
        object? convertedValue)
    {
        if (ReferenceEquals(containingAkcssSymbol, ContainingAkcssSymbol) &&
            ReferenceEquals(property, Property) &&
            valueType.Equals(ValueType) &&
            valueOperation.Equals(ValueOperation) &&
            valueConversion.Equals(ValueConversion) &&
            valueKind == ValueKind &&
            requiresBrushConversion == RequiresBrushConversion &&
            Equals(convertedValue, ConvertedValue))
        {
            return this;
        }

        return new BoundAkcssPropertySetter(
            Syntax,
            Binder,
            containingAkcssSymbol,
            property,
            valueType,
            valueOperation,
            valueConversion,
            valueKind,
            requiresBrushConversion,
            convertedValue,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssPropertySetter(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitAkcssPropertySetter(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitAkcssPropertySetter(this, parameter);
}

internal sealed class BoundAkcssIf : BoundAkcssOperation
{
    public BoundAkcssIf(
        AkcssIfDirectiveSyntax syntax,
        BinderType binder,
        IAkcssSymbol containingAkcssSymbol,
        CSharpSymbolDefinition conditionType,
        CSharpOperationDefinition conditionOperation,
        ImmutableArray<BoundAkcssOperation> operations,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.AkcssIf,
            syntax,
            binder,
            containingAkcssSymbol,
            AkburaSymbolInfo.None(CandidateReason.None),
            diagnostics,
            operations.IsDefault ? ImmutableArray<BoundNode>.Empty : ImmutableArray.CreateRange<BoundNode>(operations),
            hasErrors)
    {
        ConditionType = conditionType;
        ConditionOperation = conditionOperation;
        Operations = operations.IsDefault ? ImmutableArray<BoundAkcssOperation>.Empty : operations;
    }

    public new AkcssIfDirectiveSyntax Syntax => (AkcssIfDirectiveSyntax)base.Syntax;

    public CSharpSymbolDefinition ConditionType { get; }

    public CSharpOperationDefinition ConditionOperation { get; }

    public ImmutableArray<BoundAkcssOperation> Operations { get; }

    public BoundAkcssIf Update(
        IAkcssSymbol containingAkcssSymbol,
        CSharpSymbolDefinition conditionType,
        CSharpOperationDefinition conditionOperation,
        ImmutableArray<BoundAkcssOperation> operations)
    {
        if (ReferenceEquals(containingAkcssSymbol, ContainingAkcssSymbol) &&
            conditionType.Equals(ConditionType) &&
            conditionOperation.Equals(ConditionOperation) &&
            operations == Operations)
        {
            return this;
        }

        return new BoundAkcssIf(
            Syntax,
            Binder,
            containingAkcssSymbol,
            conditionType,
            conditionOperation,
            operations,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssIf(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitAkcssIf(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitAkcssIf(this, parameter);
}

internal sealed class BoundAkcssApply : BoundAkcssOperation
{
    public BoundAkcssApply(
        AkcssApplyDirectiveSyntax syntax,
        BinderType binder,
        IAkcssSymbol containingAkcssSymbol,
        ImmutableArray<string> items,
        ImmutableArray<IAkcssSymbol> appliedSymbols,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.AkcssApply,
            syntax,
            binder,
            containingAkcssSymbol,
            AkburaSymbolInfo.None(CandidateReason.None),
            diagnostics,
            hasErrors: hasErrors)
    {
        Items = items.IsDefault ? ImmutableArray<string>.Empty : items;
        AppliedSymbols = appliedSymbols.IsDefault ? ImmutableArray<IAkcssSymbol>.Empty : appliedSymbols;
    }

    public new AkcssApplyDirectiveSyntax Syntax => (AkcssApplyDirectiveSyntax)base.Syntax;

    public ImmutableArray<string> Items { get; }

    public ImmutableArray<IAkcssSymbol> AppliedSymbols { get; }

    public BoundAkcssApply Update(
        IAkcssSymbol containingAkcssSymbol,
        ImmutableArray<string> items,
        ImmutableArray<IAkcssSymbol> appliedSymbols)
    {
        if (ReferenceEquals(containingAkcssSymbol, ContainingAkcssSymbol) &&
            items == Items &&
            appliedSymbols == AppliedSymbols)
        {
            return this;
        }

        return new BoundAkcssApply(
            Syntax,
            Binder,
            containingAkcssSymbol,
            items,
            appliedSymbols,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssApply(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitAkcssApply(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitAkcssApply(this, parameter);
}

internal sealed class BoundAkcssIntercept : BoundAkcssOperation
{
    public BoundAkcssIntercept(
        AkcssInterceptDirectiveSyntax syntax,
        BinderType binder,
        IAkcssSymbol containingAkcssSymbol,
        CSharpSymbolDefinition interceptType,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.AkcssIntercept,
            syntax,
            binder,
            containingAkcssSymbol,
            AkburaSymbolInfo.None(CandidateReason.None),
            diagnostics,
            hasErrors: hasErrors)
    {
        InterceptType = interceptType;
    }

    public new AkcssInterceptDirectiveSyntax Syntax => (AkcssInterceptDirectiveSyntax)base.Syntax;

    public CSharpSymbolDefinition InterceptType { get; }

    public BoundAkcssIntercept Update(
        IAkcssSymbol containingAkcssSymbol,
        CSharpSymbolDefinition interceptType)
    {
        if (ReferenceEquals(containingAkcssSymbol, ContainingAkcssSymbol) &&
            interceptType.Equals(InterceptType))
        {
            return this;
        }

        return new BoundAkcssIntercept(
            Syntax,
            Binder,
            containingAkcssSymbol,
            interceptType,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssIntercept(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitAkcssIntercept(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitAkcssIntercept(this, parameter);
}
