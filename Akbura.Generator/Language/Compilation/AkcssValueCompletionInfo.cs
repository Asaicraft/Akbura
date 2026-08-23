using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Language;

internal readonly struct AkcssValueCompletionInfo
{
    public AkcssValueCompletionInfo(
        IAkcssSymbol containingSymbol,
        AkburaPropertySymbol property)
    {
        ContainingSymbol = containingSymbol;
        Property = property;
    }

    public IAkcssSymbol ContainingSymbol { get; }

    public AkburaPropertySymbol Property { get; }

    public ITypeSymbol? ExpectedType =>
        Property.Type.Symbol as ITypeSymbol;
}
