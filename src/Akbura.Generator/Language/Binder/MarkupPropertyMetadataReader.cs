using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Binder;

internal readonly struct MarkupPropertyMetadata
{
    public MarkupPropertyMetadata(bool isContent, bool assignBinding,
        ImmutableArray<ISymbol> dependencies, ImmutableArray<string> invalidDependencies)
    {
        IsContent = isContent;
        AssignBinding = assignBinding;
        Dependencies = dependencies;
        InvalidDependencies = invalidDependencies;
    }

    public bool IsContent { get; }
    public bool AssignBinding { get; }
    public ImmutableArray<ISymbol> Dependencies { get; }
    public ImmutableArray<string> InvalidDependencies { get; }
}

/// <summary>Reads metadata from one Roslyn compilation snapshot.</summary>
internal sealed class MarkupPropertyMetadataReader
{
    private readonly ConcurrentDictionary<ISymbol, MarkupPropertyMetadata> _cache =
        new(SymbolEqualityComparer.Default);

    public MarkupPropertyMetadata GetMetadata(ISymbol member) =>
        _cache.GetOrAdd(member, ReadMetadata);

    internal static bool HasAssignBinding(ISymbol member)
    {
        for (var current = member; current != null; current = GetOverriddenMember(current))
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == "Avalonia.Data.AssignBindingAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MarkupPropertyMetadata ReadMetadata(ISymbol member)
    {
        var isContent = false;
        var assignBinding = false;
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var current = member; current != null; current = GetOverriddenMember(current))
        {
            foreach (var attribute in current.GetAttributes())
            {
                switch (attribute.AttributeClass?.ToDisplayString())
                {
                    case "Avalonia.Metadata.ContentAttribute":
                        isContent = true;
                        break;
                    case "Avalonia.Data.AssignBindingAttribute":
                        assignBinding = true;
                        break;
                    case "Avalonia.Metadata.DependsOnAttribute":
                        if (attribute.ConstructorArguments.Length == 1 &&
                            attribute.ConstructorArguments[0].Value is string name)
                        {
                            names.Add(name);
                        }
                        break;
                }
            }
        }

        var dependencies = ImmutableArray.CreateBuilder<ISymbol>();
        var invalid = ImmutableArray.CreateBuilder<string>();
        foreach (var name in names.OrderBy(static name => name, StringComparer.Ordinal))
        {
            var dependency = FindDependency(member.ContainingType, name);
            if (dependency == null)
            {
                invalid.Add(name);
            }
            else
            {
                dependencies.Add(dependency);
            }
        }

        return new MarkupPropertyMetadata(isContent, assignBinding,
            dependencies.ToImmutable(), invalid.ToImmutable());
    }

    private static ISymbol? GetOverriddenMember(ISymbol member) => member switch
    {
        IPropertySymbol property => property.OverriddenProperty,
        IMethodSymbol method => method.OverriddenMethod,
        _ => null,
    };

    private static ISymbol? FindDependency(INamedTypeSymbol? type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var members = current.GetMembers(name).Where(static member =>
                member is IPropertySymbol { IsStatic: false } or
                    IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary }).ToArray();
            if (members.Length != 0)
            {
                return members.Length == 1 ? members[0] : null;
            }
        }

        return null;
    }
}
