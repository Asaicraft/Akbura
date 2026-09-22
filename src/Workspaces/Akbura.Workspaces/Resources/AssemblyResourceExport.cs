using Microsoft.CodeAnalysis;

namespace Akbura.Workspaces.Resources;

internal readonly struct AssemblyResourceExport
{
    public AssemblyResourceExport(ResourceDictionaryIdentity dictionary, string key, ITypeSymbol? resourceType)
    {
        Dictionary = dictionary;
        Key = key;
        ResourceType = resourceType;
    }

    public ResourceDictionaryIdentity Dictionary { get; }

    public string Key { get; }

    public ITypeSymbol? ResourceType { get; }
}
