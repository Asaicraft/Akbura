namespace Akbura.Language.Symbols;

internal interface IPropertySymbol : ISymbol
{
    CSharpSymbolDefinition Type { get; }

    CSharpSymbolDefinition AvaloniaPropertyDefinition { get; }

    CSharpSymbolDefinition ClrPropertyDefinition { get; }

    IParamSymbol? Parameter { get; }

    ICommandSymbol? Command { get; }

    bool IsAvaloniaProperty { get; }

    bool IsClrProperty { get; }

    bool IsParameter { get; }

    bool IsCommand { get; }

    bool CanRead { get; }

    bool CanWrite { get; }
}
