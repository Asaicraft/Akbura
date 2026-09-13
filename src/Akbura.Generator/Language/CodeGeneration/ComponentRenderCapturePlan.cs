using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentRenderCapturePlan
{
    public ComponentRenderCapturePlan(string name, ITypeSymbol type, ImmutableArray<int> scopeIds)
    {
        Name = name;
        Type = type;
        ScopeIds = scopeIds;
        Key = "render/local/" + name;
    }

    public string Name { get; }

    public ITypeSymbol Type { get; }

    public string Key { get; }

    public ImmutableArray<int> ScopeIds { get; }

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
