using Akbura.Language.Symbols;

namespace Akbura.Language;

internal readonly struct MarkupAttachedPropertyLookupCandidate
{
    public MarkupAttachedPropertyLookupCandidate(
        string displayName,
        string ownerTypeDisplay,
        string typeDisplay,
        IPropertySymbol property)
    {
        DisplayName = displayName;
        OwnerTypeDisplay = ownerTypeDisplay;
        TypeDisplay = typeDisplay;
        Property = property;
    }

    public string DisplayName { get; }

    public string OwnerTypeDisplay { get; }

    public string TypeDisplay { get; }

    public IPropertySymbol Property { get; }
}
