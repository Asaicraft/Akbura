namespace Akbura.Language.Symbols;

internal interface IRoutedEventSymbol : ISymbol
{
    CSharpSymbolDefinition HandlerType { get; }

    CSharpSymbolDefinition EventArgsType { get; }

    CSharpSymbolDefinition RoutedEventDefinition { get; }

    CSharpSymbolDefinition ClrEventDefinition { get; }

    bool IsAvaloniaRoutedEvent { get; }

    bool IsClrEvent { get; }
}
