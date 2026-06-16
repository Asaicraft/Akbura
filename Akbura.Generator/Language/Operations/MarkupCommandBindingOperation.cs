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
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
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
        HasErrors = hasErrors;
    }

    public OperationKind Kind => OperationKind.MarkupCommandBinding;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttributeSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => ImmutableArray<IOperation>.Empty;

    public ISymbol? TargetSymbol => Command;

    public ISymbol? TypeSymbol => Command;

    public CSharpOperationDefinition CSharpDefinition => HandlerOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol Property { get; }

    public ICommandSymbol Command { get; }

    public ImmutableArray<ICommandParameterSymbol> Parameters => Command.Parameters;

    public CSharpSymbolDefinition ReturnType => Command.ReturnType;

    public CSharpSymbolDefinition ResultType => Command.ResultType;

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
        return $"{Command.Name}={ValueSyntax?.ToFullString() ?? string.Empty}";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
