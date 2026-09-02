using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;

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
        string handlerExpression,
        AkburaSyntax syntax)
    {
        Kind = kind;
        EventSymbol = eventSymbol;
        HandlerExpression = handlerExpression;
        Syntax = syntax;
    }

    public ComponentRoutedEventKind Kind { get; }

    public ISymbol? EventSymbol { get; }

    public string? HandlerExpression { get; }

    public AkburaSyntax? Syntax { get; }

    public bool IsValid =>
        Kind != ComponentRoutedEventKind.None &&
        EventSymbol != null &&
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
            handlerExpression,
            syntax);
    }

    public static ComponentRoutedEventPlan CreateAvaloniaRoutedEvent(
        ISymbol eventSymbol,
        string handlerExpression,
        AkburaSyntax syntax)
    {
        return new ComponentRoutedEventPlan(
            ComponentRoutedEventKind.AvaloniaRoutedEvent,
            eventSymbol,
            handlerExpression,
            syntax);
    }
}

internal readonly struct ComponentCommandBindingPlan
{
    public ComponentCommandBindingPlan(
        PropertyWritePlan destination,
        string commandName,
        AkburaSyntax syntax)
    {
        Destination = destination;
        CommandName = commandName ?? throw new ArgumentNullException(nameof(commandName));
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
    }

    public PropertyWritePlan Destination { get; }

    public string CommandName { get; }

    public AkburaSyntax Syntax { get; }

    public bool IsValid => Destination.IsValid && !string.IsNullOrEmpty(CommandName);
}
