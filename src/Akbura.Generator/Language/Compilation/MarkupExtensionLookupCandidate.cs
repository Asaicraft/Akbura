using Microsoft.CodeAnalysis;

namespace Akbura.Language;

internal readonly struct MarkupExtensionLookupCandidate
{
    public MarkupExtensionLookupCandidate(
        string displayName,
        string metadataName,
        INamedTypeSymbol extensionType,
        IMethodSymbol? provideValueMethod,
        bool isAvaloniaBinding,
        bool isUtilityVariant)
    {
        DisplayName = displayName;
        MetadataName = metadataName;
        ExtensionType = extensionType;
        ProvideValueMethod = provideValueMethod;
        IsAvaloniaBinding = isAvaloniaBinding;
        IsUtilityVariant = isUtilityVariant;
    }

    public string DisplayName { get; }

    public string MetadataName { get; }

    public INamedTypeSymbol ExtensionType { get; }

    public IMethodSymbol? ProvideValueMethod { get; }

    public bool IsAvaloniaBinding { get; }

    public bool IsUtilityVariant { get; }
}
