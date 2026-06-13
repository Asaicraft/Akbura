namespace Akbura.Language.Symbols;

internal readonly struct MarkupContentModel
{
    public MarkupContentModel(
        CSharpSymbolDefinition contentProperty,
        CSharpSymbolDefinition allowedChildType,
        bool isCollection,
        bool allowsText)
    {
        ContentProperty = contentProperty;
        AllowedChildType = allowedChildType;
        IsCollection = isCollection;
        AllowsText = allowsText;
    }

    public CSharpSymbolDefinition ContentProperty { get; }

    public CSharpSymbolDefinition AllowedChildType { get; }

    public bool IsCollection { get; }

    public bool AllowsText { get; }

    public bool AllowsChildren => !AllowedChildType.IsDefault;

    public bool IsDefault => ContentProperty.IsDefault && AllowedChildType.IsDefault;
}
