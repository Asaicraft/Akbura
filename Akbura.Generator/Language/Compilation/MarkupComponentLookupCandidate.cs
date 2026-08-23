using Microsoft.CodeAnalysis;

namespace Akbura.Language;

internal readonly struct MarkupComponentLookupCandidate
{
    public MarkupComponentLookupCandidate(
        string displayName,
        string metadataName,
        INamedTypeSymbol? componentType,
        bool isAkburaComponent)
    {
        DisplayName = displayName;
        MetadataName = metadataName;
        ComponentType = componentType;
        IsAkburaComponent = isAkburaComponent;
    }

    public string DisplayName { get; }

    public string MetadataName { get; }

    public INamedTypeSymbol? ComponentType { get; }

    public bool IsAkburaComponent { get; }
}
