namespace Akbura.Language.Symbols;

internal readonly struct MarkupContentModel
{
    public MarkupContentModel(
        CSharpSymbolDefinition contentProperty,
        CSharpSymbolDefinition allowedChildType,
        bool isCollection,
        bool allowsText,
        IParamSymbol? contentParameter = null)
    {
        ContentProperty = contentProperty;
        AllowedChildType = allowedChildType;
        IsCollection = isCollection;
        AllowsText = allowsText;
        ContentParameter = contentParameter;
    }

    public CSharpSymbolDefinition ContentProperty { get; }

    public CSharpSymbolDefinition AllowedChildType { get; }

    public bool IsCollection { get; }

    public bool AllowsText { get; }

    public IParamSymbol? ContentParameter { get; }

    public bool AllowsChildren => !AllowedChildType.IsDefault;

    public bool IsDefault =>
        ContentProperty.IsDefault &&
        ContentParameter == null &&
        AllowedChildType.IsDefault;
}
