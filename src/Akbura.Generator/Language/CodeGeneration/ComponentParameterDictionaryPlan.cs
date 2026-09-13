using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;

namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentParameterDictionaryPlan(
    ITypeSymbol propertyType,
    ITypeSymbol backingType,
    MarkupDictionaryShape shape,
    bool canCreateBacking,
    bool usesStandardDictionaryFactory = false)
{
    public ITypeSymbol PropertyType { get; } = propertyType;
    public ITypeSymbol BackingType { get; } = backingType;
    public MarkupDictionaryShape Shape { get; } = shape;
    public bool CanCreateBacking { get; } = canCreateBacking;
    public bool UsesStandardDictionaryFactory { get; } = usesStandardDictionaryFactory;
}
