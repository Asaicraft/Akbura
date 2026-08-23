using Akbura.Language.Symbols;

namespace Akbura.Language;

internal readonly struct AkcssPropertyLookupCandidate
{
    public AkcssPropertyLookupCandidate(
        string displayName,
        string insertName,
        string ownerTypeDisplay,
        string typeDisplay,
        IPropertySymbol property,
        bool isAttached)
    {
        DisplayName = displayName;
        InsertName = insertName;
        OwnerTypeDisplay = ownerTypeDisplay;
        TypeDisplay = typeDisplay;
        Property = property;
        IsAttached = isAttached;
    }

    public string DisplayName { get; }

    public string InsertName { get; }

    public string OwnerTypeDisplay { get; }

    public string TypeDisplay { get; }

    public IPropertySymbol Property { get; }

    public bool IsAttached { get; }
}
