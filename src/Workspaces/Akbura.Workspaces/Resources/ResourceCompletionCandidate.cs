using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Resources;

internal enum ResourceCompletionOriginKind
{
    Local,
    AssemblyExport,
    BuiltInHint,
}

internal readonly struct ResourceCompletionOrigin
{
    public ResourceCompletionOrigin(ResourceCompletionOriginKind kind, string description, ITypeSymbol? resourceType, int priority)
    {
        Kind = kind;
        Description = description;
        ResourceType = resourceType;
        Priority = priority;
    }

    public ResourceCompletionOriginKind Kind { get; }

    public string Description { get; }

    public ITypeSymbol? ResourceType { get; }

    public int Priority { get; }
}

internal readonly struct ResourceCompletionCandidate
{
    public ResourceCompletionCandidate(string key, ImmutableArray<ResourceCompletionOrigin> origins)
    {
        Key = key;
        Origins = origins.IsDefault
            ? ImmutableArray<ResourceCompletionOrigin>.Empty
            : origins;
    }

    public string Key { get; }

    public ImmutableArray<ResourceCompletionOrigin> Origins { get; }

    public int Priority
    {
        get
        {
            var priority = int.MaxValue;
            foreach (var origin in Origins)
            {
                priority = Math.Min(
                    priority,
                    origin.Priority);
            }

            return priority;
        }
    }
}
