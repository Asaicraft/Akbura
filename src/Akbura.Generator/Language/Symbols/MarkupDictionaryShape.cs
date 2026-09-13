using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Akbura.Language.Symbols;

internal readonly struct MarkupDictionaryShape
{
    private MarkupDictionaryShape(
        INamedTypeSymbol? contractType,
        ITypeSymbol? keyType,
        ITypeSymbol? valueType,
        bool isAmbiguous = false,
        bool isReadOnlyOnly = false)
    {
        ContractType = contractType;
        KeyType = keyType;
        ValueType = valueType;
        IsAmbiguous = isAmbiguous;
        IsReadOnlyOnly = isReadOnlyOnly;
    }

    public INamedTypeSymbol? ContractType { get; }
    public ITypeSymbol? KeyType { get; }
    public ITypeSymbol? ValueType { get; }
    public bool IsAmbiguous { get; }
    public bool IsReadOnlyOnly { get; }
    public bool IsDictionary => ContractType != null || IsAmbiguous || IsReadOnlyOnly;
    public bool IsGeneric => ContractType?.Arity == 2;

    public static MarkupDictionaryShape Create(ITypeSymbol type, Compilation compilation)
    {
        var generic = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        var nonGeneric = compilation.GetTypeByMetadataName("System.Collections.IDictionary");
        var readOnly = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
        INamedTypeSymbol? selected = null;
        INamedTypeSymbol? readOnlyContract = null;
        var hasNonGeneric = false;

        foreach (var candidate in GetContracts(type))
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, generic))
            {
                if (selected != null && !SymbolEqualityComparer.Default.Equals(selected, candidate))
                {
                    return new MarkupDictionaryShape(null, null, null, isAmbiguous: true);
                }

                selected = candidate;
            }
            else if (SymbolEqualityComparer.Default.Equals(candidate, nonGeneric))
            {
                hasNonGeneric = true;
            }
            else if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, readOnly))
            {
                readOnlyContract = candidate;
            }
        }

        if (selected != null)
        {
            return new MarkupDictionaryShape(selected, GetAnnotatedTypeArgument(selected, 0),
                GetAnnotatedTypeArgument(selected, 1));
        }

        if (hasNonGeneric)
        {
            var objectType = compilation.GetSpecialType(SpecialType.System_Object);
            return new MarkupDictionaryShape(nonGeneric, objectType, objectType);
        }

        return readOnlyContract == null
            ? default
            : new MarkupDictionaryShape(null, GetAnnotatedTypeArgument(readOnlyContract, 0),
                GetAnnotatedTypeArgument(readOnlyContract, 1), isReadOnlyOnly: true);
    }

    private static ITypeSymbol GetAnnotatedTypeArgument(INamedTypeSymbol contract, int index)
    {
        var type = contract.TypeArguments[index];
        var annotations = contract.TypeArgumentNullableAnnotations;
        return annotations.IsDefaultOrEmpty
            ? type
            : type.WithNullableAnnotation(annotations[index]);
    }

    private static IEnumerable<INamedTypeSymbol> GetContracts(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            yield return namedType;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            yield return candidate;
        }
    }
}
