using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;

namespace Akbura.Language.Symbols;

/// <summary>
/// A declaration must admit an observable backing without changing its CLR type.
/// Arbitrary IList implementations are valid sources for an IList parameter;
/// they need not be valid concrete types for the owned backing itself.
/// </summary>
internal static class ObservableListParameterShape
{
    public static bool TryCreate(IParamSymbol parameter, CSharpCompilation compilation,
        out ITypeSymbol elementType, out INamedTypeSymbol backingType)
    {
        elementType = null!;
        backingType = null!;
        if (parameter.BindingKind != ParamBindingKind.Default ||
            parameter.Type.Symbol is not INamedTypeSymbol type) return false;
        return TryCreate(type, compilation, out elementType, out backingType);
    }

    public static bool TryCreate(INamedTypeSymbol type, CSharpCompilation compilation,
        out ITypeSymbol elementType, out INamedTypeSymbol backingType)
    {
        elementType = null!;
        backingType = null!;
        var list = compilation.GetTypeByMetadataName("System.Collections.IList");
        var genericList = compilation.GetTypeByMetadataName("System.Collections.Generic.IList`1");
        var collection = compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1");
        var observable = compilation.GetTypeByMetadataName("System.Collections.ObjectModel.ObservableCollection`1");
        if (observable == null) return false;
        var definition = type.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(definition, list))
        {
            elementType = compilation.GetSpecialType(SpecialType.System_Object);
        }
        else if (SymbolEqualityComparer.Default.Equals(definition, collection))
        {
            elementType = type.TypeArguments[0];
        }
        else
        {
            foreach (var candidate in type.AllInterfaces.Prepend(type))
            {
                if (!SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, genericList)) continue;
                if (elementType != null && !SymbolEqualityComparer.Default.Equals(elementType, candidate.TypeArguments[0]))
                    return false; // ambiguous IList<T> contracts
                elementType = candidate.TypeArguments[0];
            }
            if (elementType == null) return false;
        }
        var standard = observable.Construct(elementType);
        if (compilation.ClassifyConversion(standard, type).IsImplicit)
        {
            backingType = standard;
            return true;
        }

        // A user ObservableCollection<T> subclass must keep its declared CLR type.
        // Accept a public, concrete, parameterless subtype, not an unsafe cast of
        // ObservableCollection<T> to arbitrary IList-implementing classes.
        if (type.TypeKind != TypeKind.Class || type.IsAbstract ||
            !type.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0))
            return false;
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, observable)) continue;
            backingType = (INamedTypeSymbol)type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            return true;
        }
        return false;
    }
}
