using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentConditionalCapturePlan
{
    public ComponentConditionalCapturePlan(string name, ITypeSymbol type, string key,
        ImmutableArray<int> scopeIds, ImmutableArray<int> nonNullScopeIds = default)
    {
        Name = name;
        Type = type;
        Key = key;
        ScopeIds = scopeIds;
        NonNullScopeIds = nonNullScopeIds.IsDefault ? [] : nonNullScopeIds;
    }

    public string Name { get; }

    public ITypeSymbol Type { get; }

    public string Key { get; }

    public ImmutableArray<int> ScopeIds { get; }

    public ImmutableArray<int> NonNullScopeIds { get; }

    public bool IsKnownNonNullByScope(int scopeId)
    {
        foreach (var id in NonNullScopeIds)
        {
            if (id == scopeId)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsReadByScope(int scopeId)
    {
        foreach (var id in ScopeIds)
        {
            if (id == scopeId)
            {
                return true;
            }
        }

        return false;
    }
}
