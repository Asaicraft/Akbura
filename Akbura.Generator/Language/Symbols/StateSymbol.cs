using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class StateSymbol : Symbol, IStateSymbol
{
    public StateSymbol(
        StateDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition type,
        bool hasExplicitType,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));

        var name = declarationSyntax.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        Type = type;
        HasExplicitType = hasExplicitType;
    }

    public override SymbolKind Kind => SymbolKind.State;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public StateDeclarationSyntax DeclarationSyntax { get; }

    public CSharpSymbolDefinition Type { get; }

    public bool HasExplicitType { get; }

    public override string ToDisplayString()
    {
        return !Type.IsDefault ? $"state {Type.Name} {Name}" : $"state {Name}";
    }
}
