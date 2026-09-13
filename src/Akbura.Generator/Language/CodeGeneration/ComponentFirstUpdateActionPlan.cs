using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Akbura.Language.Binder;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal enum ComponentFirstUpdateActionKind : byte
{
    None,
    NameAssignment,
    PropertyWrite,
    PropertySubscription,
    RoutedEvent,
    CommandBinding,
}

internal readonly struct ComponentFirstUpdateActionPlan
{
    private ComponentFirstUpdateActionPlan(ComponentFirstUpdateActionKind kind, int index)
    {
        Kind = kind;
        Index = index;
    }

    public ComponentFirstUpdateActionKind Kind { get; }

    public int Index { get; }

    public static ComponentFirstUpdateActionPlan CreateNameAssignment(int index)
    {
        return new ComponentFirstUpdateActionPlan(ComponentFirstUpdateActionKind.NameAssignment, index);
    }

    public static ComponentFirstUpdateActionPlan CreateWrite(int index)
    {
        return new ComponentFirstUpdateActionPlan(ComponentFirstUpdateActionKind.PropertyWrite, index);
    }

    public static ComponentFirstUpdateActionPlan CreateSubscription(int index)
    {
        return new ComponentFirstUpdateActionPlan(ComponentFirstUpdateActionKind.PropertySubscription, index);
    }

    public static ComponentFirstUpdateActionPlan CreateRoutedEvent(int index)
    {
        return new ComponentFirstUpdateActionPlan(ComponentFirstUpdateActionKind.RoutedEvent, index);
    }

    public static ComponentFirstUpdateActionPlan CreateCommandBinding(int index)
    {
        return new ComponentFirstUpdateActionPlan(ComponentFirstUpdateActionKind.CommandBinding, index);
    }
}

internal readonly struct ComponentNameAssignmentPlan
{
    public ComponentNameAssignmentPlan(string name, AkburaSyntax syntax)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
    }

    public string Name { get; }

    public AkburaSyntax Syntax { get; }
}

internal enum ComponentRoutedEventKind : byte
{
    None,
    ClrEvent,
    AvaloniaRoutedEvent,
}

internal readonly struct ComponentRoutedEventPlan
{
    private ComponentRoutedEventPlan(
        ComponentRoutedEventKind kind,
        ISymbol eventSymbol,
        ITypeSymbol handlerType,
        string handlerExpression,
        AkburaSyntax syntax)
    {
        Kind = kind;
        EventSymbol = eventSymbol;
        HandlerType = handlerType;
        HandlerExpression = handlerExpression;
        Syntax = syntax;
    }

    public ComponentRoutedEventKind Kind { get; }

    public ISymbol? EventSymbol { get; }

    public ITypeSymbol? HandlerType { get; }

    public string? HandlerExpression { get; }

    public AkburaSyntax? Syntax { get; }

    public bool IsValid =>
        Kind != ComponentRoutedEventKind.None &&
        EventSymbol != null &&
        HandlerType != null &&
        !string.IsNullOrEmpty(HandlerExpression) &&
        Syntax != null;

    public static ComponentRoutedEventPlan CreateClrEvent(
        IEventSymbol eventSymbol,
        string handlerExpression,
        AkburaSyntax syntax)
    {
        return new ComponentRoutedEventPlan(
            ComponentRoutedEventKind.ClrEvent,
            eventSymbol,
            eventSymbol.Type,
            handlerExpression,
            syntax);
    }

    public static ComponentRoutedEventPlan CreateAvaloniaRoutedEvent(
        ISymbol eventSymbol,
        ITypeSymbol handlerType,
        string handlerExpression,
        AkburaSyntax syntax)
    {
        return new ComponentRoutedEventPlan(
            ComponentRoutedEventKind.AvaloniaRoutedEvent,
            eventSymbol,
            handlerType,
            handlerExpression,
            syntax);
    }
}

internal enum ComponentCommandAwaitableKind : byte
{
    None,
    Task,
    ValueTask,
}

internal readonly struct ComponentCommandBindingPlan
{
    public ComponentCommandBindingPlan(
        PropertyWritePlan destination,
        string commandName,
        AkburaSyntax syntax,
        ExpressionSyntax? handlerExpression = null,
        MarkupCommandHandlerKind handlerKind = MarkupCommandHandlerKind.DirectReference,
        MarkupCommandArgumentMode argumentMode = MarkupCommandArgumentMode.None,
        MarkupCommandResultMode resultMode = MarkupCommandResultMode.Unknown,
        ImmutableArray<ITypeSymbol> parameterTypes = default,
        ITypeSymbol? resultType = null,
        bool isAsync = false,
        bool containsAwait = false,
        int handlerParameterCount = 0,
        ITypeSymbol? handlerType = null,
        ITypeSymbol? handlerResultType = null,
        ComponentCommandAwaitableKind awaitableKind = ComponentCommandAwaitableKind.None,
        bool isCommandReference = true,
        ITypeSymbol? awaitableResultType = null)
    {
        Destination = destination;
        CommandName = commandName ?? throw new ArgumentNullException(nameof(commandName));
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        HandlerExpression = handlerExpression ?? Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName(commandName);
        HandlerKind = handlerKind;
        ArgumentMode = argumentMode;
        ResultMode = resultMode;
        ParameterTypes = parameterTypes;
        ResultType = resultType;
        IsAsync = isAsync;
        ContainsAwait = containsAwait;
        HandlerParameterCount = handlerParameterCount;
        HandlerType = handlerType;
        HandlerResultType = handlerResultType;
        AwaitableKind = awaitableKind;
        IsCommandReference = isCommandReference;
        AwaitableResultType = awaitableResultType;
    }

    public PropertyWritePlan Destination { get; }

    public string CommandName { get; }

    public AkburaSyntax Syntax { get; }

    public ExpressionSyntax HandlerExpression { get; }

    public MarkupCommandHandlerKind HandlerKind { get; }

    public MarkupCommandArgumentMode ArgumentMode { get; }

    public MarkupCommandResultMode ResultMode { get; }

    public ImmutableArray<ITypeSymbol> ParameterTypes { get; }

    public ITypeSymbol? ResultType { get; }

    public bool IsAsync { get; }

    public bool ContainsAwait { get; }

    public int HandlerParameterCount { get; }

    public ITypeSymbol? HandlerType { get; }

    public ITypeSymbol? HandlerResultType { get; }

    public ComponentCommandAwaitableKind AwaitableKind { get; }

    public bool IsCommandReference { get; }

    public ITypeSymbol? AwaitableResultType { get; }

    public bool IsValid => Destination.IsValid && !string.IsNullOrEmpty(CommandName);
}
