namespace Akbura.Language.Symbols;

internal interface IPropertySymbol : ISymbol
{
    CSharpSymbolDefinition Type { get; }

    CSharpSymbolDefinition AvaloniaPropertyDefinition { get; }

    CSharpSymbolDefinition AttachedPropertyDefinition { get; }

    CSharpSymbolDefinition AttachedGetterDefinition { get; }

    CSharpSymbolDefinition AttachedSetterDefinition { get; }

    CSharpSymbolDefinition AttachedTargetType { get; }

    CSharpSymbolDefinition ClrPropertyDefinition { get; }

    PropertyAccessKind ReadKind { get; }

    CSharpSymbolDefinition ReadDefinition { get; }

    PropertyAccessKind WriteKind { get; }

    CSharpSymbolDefinition WriteDefinition { get; }

    IParamSymbol? Parameter { get; }

    ICommandSymbol? Command { get; }

    bool IsAvaloniaProperty { get; }

    bool IsAttachedProperty { get; }

    bool IsClrProperty { get; }

    bool IsParameter { get; }

    bool IsCommand { get; }

    bool CanRead { get; }

    bool CanWrite { get; }
}
