using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface ISymbol : IEquatable<ISymbol>
{
    SymbolKind Kind { get; }

    SymbolLanguage Language { get; }

    string Name { get; }

    string MetadataName { get; }

    ISymbol? ContainingSymbol { get; }

    ISymbol OriginalDefinition { get; }

    CSharpSymbolDefinition CSharpDefinition { get; }

    ImmutableArray<Microsoft.CodeAnalysis.Location> Locations { get; }

    ImmutableArray<ISymbolDeclarationReference> DeclaringSyntaxReferences { get; }

    bool CanBeReferencedByName { get; }

    bool IsDefinition { get; }

    bool IsImplicitlyDeclared { get; }

    void Accept(SymbolVisitor visitor);

    TResult Accept<TResult>(SymbolVisitor<TResult> visitor);

    TResult Accept<TParameter, TResult>(SymbolVisitor<TParameter, TResult> visitor, TParameter parameter);

    string ToDisplayString();
}
