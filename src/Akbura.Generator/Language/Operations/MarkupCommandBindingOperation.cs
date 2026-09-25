using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupCommandBindingOperation : IMarkupCommandBindingOperation
{
    public MarkupCommandBindingOperation(
        MarkupAttributeSyntax syntax,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol property,
        ICommandSymbol? command,
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
        bool hasErrors,
        ICSharpOperation? handlerOperationTree = null,
        MarkupCommandTargetKind targetKind = MarkupCommandTargetKind.DeclaredCommand,
        ImmutableArray<CSharpSymbolDefinition> parameterTypes = default,
        CSharpSymbolDefinition returnType = default,
        CSharpSymbolDefinition resultType = default)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
        Property = property ?? throw new ArgumentNullException(nameof(property));
        Command = command;
        TargetKind = targetKind;
        ParameterTypes = parameterTypes.IsDefault
            ? command?.Parameters.Select(static parameter => parameter.Type).ToImmutableArray() ?? []
            : parameterTypes;
        ReturnType = returnType.IsDefault && command != null ? command.ReturnType : returnType;
        ResultType = resultType.IsDefault && command != null ? command.ResultType : resultType;
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
        HasErrors = hasErrors;
        HandlerOperationTree = handlerOperationTree;
        AdoptCSharpOperationTree(HandlerOperationTree);
        Children = HandlerOperationTree == null
            ? ImmutableArray<IOperation>.Empty
            : ImmutableArray.Create<IOperation>(HandlerOperationTree);
    }

    public OperationKind Kind => OperationKind.MarkupCommandBinding;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttributeSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children { get; }

    public ISymbol? TargetSymbol => Command is not null ? Command : Property;

    public ISymbol? TypeSymbol => Command is not null ? Command : Property;

    public CSharpOperationDefinition CSharpDefinition => HandlerOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol Property { get; }

    public ICommandSymbol? Command { get; }

    public MarkupCommandTargetKind TargetKind { get; }

    public ImmutableArray<ICommandParameterSymbol> Parameters => Command?.Parameters ?? [];

    public ImmutableArray<CSharpSymbolDefinition> ParameterTypes { get; }

    public CSharpSymbolDefinition ReturnType { get; }

    public CSharpSymbolDefinition ResultType { get; }

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

    public ICSharpOperation? HandlerOperationTree { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitMarkupCommandBinding(this);
    }


    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitMarkupCommandBinding(this, parameter);
    }

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation && Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public string ToDisplayString()
    {
        return $"{Command?.Name ?? Property.Name}={ValueSyntax?.ToFullString() ?? string.Empty}";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }

    private void AdoptCSharpOperationTree(ICSharpOperation? operation)
    {
        if (operation is CSharpOperation csharpOperation)
        {
            csharpOperation.SetParent(this);
        }
    }
}
