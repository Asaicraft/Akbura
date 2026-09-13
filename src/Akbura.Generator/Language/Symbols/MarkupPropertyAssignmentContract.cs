using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class ResolvedMarkupPropertyReference
{
    public ResolvedMarkupPropertyReference(INamedTypeSymbol lookupOwner,
        IFieldSymbol field, ITypeSymbol valueType, TextSpan ownerSpan, TextSpan propertySpan)
    {
        LookupOwner = lookupOwner;
        Field = field;
        ValueType = valueType;
        OwnerSpan = ownerSpan;
        PropertySpan = propertySpan;
    }

    public INamedTypeSymbol LookupOwner { get; }
    public IFieldSymbol Field { get; }
    public ITypeSymbol ValueType { get; }
    public TextSpan OwnerSpan { get; }
    public TextSpan PropertySpan { get; }
}

internal readonly struct MarkupStyleTargetContext
{
    public MarkupStyleTargetContext(ImmutableArray<INamedTypeSymbol> types, bool isUnknown = false)
    {
        Types = types.IsDefault ? ImmutableArray<INamedTypeSymbol>.Empty : types;
        IsUnknown = isUnknown || Types.IsEmpty;
    }

    public ImmutableArray<INamedTypeSymbol> Types { get; }
    public bool IsUnknown { get; }
    public bool IsAmbiguous => !Types.IsDefaultOrEmpty && Types.Length > 1;
}

internal readonly struct MarkupPropertyAssignmentContract
{
    public MarkupPropertyAssignmentContract(ITypeSymbol? declaredType,
        ITypeSymbol? contextualValueType, MarkupPropertyMetadata metadata,
        ResolvedMarkupPropertyReference? targetPropertyReference,
        bool contextualTypeUnknown = false, bool contextualTypeAmbiguous = false)
    {
        DeclaredType = declaredType;
        ContextualValueType = contextualValueType;
        Metadata = metadata;
        TargetPropertyReference = targetPropertyReference;
        ContextualTypeUnknown = contextualTypeUnknown;
        ContextualTypeAmbiguous = contextualTypeAmbiguous;
    }

    public ITypeSymbol? DeclaredType { get; }
    public ITypeSymbol? ContextualValueType { get; }
    public MarkupPropertyMetadata Metadata { get; }
    public bool AssignBinding => Metadata.AssignBinding;
    public ImmutableArray<Microsoft.CodeAnalysis.ISymbol> Dependencies => Metadata.Dependencies;
    public ResolvedMarkupPropertyReference? TargetPropertyReference { get; }
    public bool ContextualTypeUnknown { get; }
    public bool ContextualTypeAmbiguous { get; }
}
