using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class RoutedEventSymbol : Symbol, IRoutedEventSymbol
{
    public RoutedEventSymbol(
        string name,
        CSharpSymbolDefinition handlerType,
        CSharpSymbolDefinition eventArgsType,
        CSharpSymbolDefinition routedEventDefinition = default,
        CSharpSymbolDefinition clrEventDefinition = default,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Event symbol name cannot be empty.", nameof(name));
        }

        Name = name;
        HandlerType = handlerType;
        EventArgsType = eventArgsType;
        RoutedEventDefinition = routedEventDefinition;
        ClrEventDefinition = clrEventDefinition;
    }

    public override SymbolKind Kind => SymbolKind.Event;

    public override SymbolLanguage Language => SymbolLanguage.Markup;

    public override string Name { get; }

    public CSharpSymbolDefinition HandlerType { get; }

    public CSharpSymbolDefinition EventArgsType { get; }

    public CSharpSymbolDefinition RoutedEventDefinition { get; }

    public CSharpSymbolDefinition ClrEventDefinition { get; }

    public bool IsAvaloniaRoutedEvent => !RoutedEventDefinition.IsDefault;

    public bool IsClrEvent => !ClrEventDefinition.IsDefault;

    public override CSharpSymbolDefinition CSharpDefinition => !ClrEventDefinition.IsDefault
        ? ClrEventDefinition
        : RoutedEventDefinition;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitRoutedEvent(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitRoutedEvent(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitRoutedEvent(this, parameter);
    }

    public override string ToDisplayString()
    {
        if (!HandlerType.IsDefault)
        {
            return $"{Name}: {HandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
        }

        return Name;
    }
}
