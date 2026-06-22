using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Symbols;

internal abstract class Symbol : ISymbol
{
    protected Symbol(
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
    {
        ContainingSymbol = containingSymbol;
        Locations = locations.IsDefault ? ImmutableArray<Location>.Empty : locations;
        DeclaringSyntaxReferences = declaringSyntaxReferences.IsDefault
            ? ImmutableArray<ISymbolDeclarationReference>.Empty
            : declaringSyntaxReferences;
        IsImplicitlyDeclared = isImplicitlyDeclared;
    }

    public abstract SymbolKind Kind { get; }

    public abstract SymbolLanguage Language { get; }

    public abstract string Name { get; }

    public virtual string MetadataName => Name;

    public ISymbol? ContainingSymbol { get; }

    public virtual ISymbol OriginalDefinition => this;

    public virtual CSharpSymbolDefinition CSharpDefinition => default;

    public ImmutableArray<Location> Locations { get; }

    public ImmutableArray<ISymbolDeclarationReference> DeclaringSyntaxReferences { get; }

    public virtual bool CanBeReferencedByName => !string.IsNullOrEmpty(Name);

    public virtual bool IsDefinition => true;

    public bool IsImplicitlyDeclared { get; }

    public virtual void Accept(SymbolVisitor visitor)
    {
        visitor.DefaultVisit(this);
    }

    public virtual TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.DefaultVisit(this);
    }

    public virtual TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.DefaultVisit(this, parameter);
    }

    public virtual bool Equals(ISymbol? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is ISymbol symbol && Equals(symbol);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public virtual string ToDisplayString()
    {
        return MetadataName;
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
