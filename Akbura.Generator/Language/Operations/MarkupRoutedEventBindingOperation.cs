using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupRoutedEventBindingOperation : IMarkupRoutedEventBindingOperation
{
    public MarkupRoutedEventBindingOperation(
        MarkupAttributeSyntax syntax,
        IMarkupComponentSymbol? containingComponent,
        IRoutedEventSymbol @event,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        MarkupCommandHandlerKind handlerKind,
        MarkupCommandArgumentMode argumentMode,
        int handlerParameterCount,
        bool isAsync,
        bool containsAwait,
        CSharpOperationDefinition handlerOperation,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
        Event = @event ?? throw new ArgumentNullException(nameof(@event));
        BindingKind = bindingKind;
        ValueKind = valueKind;
        ValueSyntax = valueSyntax;
        HandlerKind = handlerKind;
        ArgumentMode = argumentMode;
        HandlerParameterCount = handlerParameterCount;
        IsAsync = isAsync;
        ContainsAwait = containsAwait;
        HandlerOperation = handlerOperation;
        HasErrors = hasErrors;
    }

    public OperationKind Kind => OperationKind.MarkupEventBinding;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttributeSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => ImmutableArray<IOperation>.Empty;

    public ISymbol? TargetSymbol => Event;

    public ISymbol? TypeSymbol => Event;

    public CSharpOperationDefinition CSharpDefinition => HandlerOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IRoutedEventSymbol Event { get; }

    public MarkupAttributeBindingKind BindingKind { get; }

    public MarkupAttributeValueKind ValueKind { get; }

    public MarkupAttributeValueSyntax? ValueSyntax { get; }

    public MarkupCommandHandlerKind HandlerKind { get; }

    public MarkupCommandArgumentMode ArgumentMode { get; }

    public int HandlerParameterCount { get; }

    public bool IsAsync { get; }

    public bool ContainsAwait { get; }

    public CSharpSymbolDefinition HandlerType => Event.HandlerType;

    public CSharpSymbolDefinition EventArgsType => Event.EventArgsType;

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
        return $"{Event.Name}={ValueSyntax?.ToFullString() ?? string.Empty}";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
