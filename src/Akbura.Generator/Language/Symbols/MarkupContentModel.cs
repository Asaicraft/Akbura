using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal enum MarkupContentKind
{
    None,
    Property,
    Collection,
    Dictionary,
    AddMethods,
}

internal readonly struct MarkupContentModel
{
    public MarkupContentModel(
        CSharpSymbolDefinition contentProperty,
        CSharpSymbolDefinition allowedChildType,
        bool isCollection,
        bool allowsText,
        IParamSymbol? contentParameter = null,
        MarkupDictionaryShape dictionaryShape = default,
        ImmutableArray<IMethodSymbol> addMethods = default)
    {
        ContentProperty = contentProperty;
        AllowedChildType = allowedChildType;
        IsCollection = isCollection;
        AllowsText = allowsText;
        ContentParameter = contentParameter;
        DictionaryShape = dictionaryShape;
        AddMethods = addMethods.IsDefault ? ImmutableArray<IMethodSymbol>.Empty : addMethods;
    }

    public CSharpSymbolDefinition ContentProperty { get; }

    public CSharpSymbolDefinition AllowedChildType { get; }

    public bool IsCollection { get; }

    public MarkupDictionaryShape DictionaryShape { get; }

    public bool IsDictionary => DictionaryShape.IsDictionary;

    public ImmutableArray<IMethodSymbol> AddMethods { get; }

    public MarkupContentKind Kind => IsDictionary
        ? MarkupContentKind.Dictionary
        : IsCollection
            ? MarkupContentKind.Collection
            : !AddMethods.IsDefaultOrEmpty
                ? MarkupContentKind.AddMethods
                : IsDefault ? MarkupContentKind.None : MarkupContentKind.Property;

    public bool AllowsText { get; }

    public IParamSymbol? ContentParameter { get; }

    public bool AllowsChildren => !AllowedChildType.IsDefault || !AddMethods.IsDefaultOrEmpty;

    public bool IsDefault =>
        ContentProperty.IsDefault &&
        ContentParameter == null &&
        AllowedChildType.IsDefault &&
        !IsDictionary &&
        AddMethods.IsDefaultOrEmpty;
}
