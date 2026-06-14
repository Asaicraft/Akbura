using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class InjectSymbol : Symbol, IInjectSymbol
{
    public InjectSymbol(
        InjectDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition type,
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
            throw new ArgumentException("Inject symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        Type = type;
    }

    public override SymbolKind Kind => SymbolKind.InjectedService;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public InjectDeclarationSyntax DeclarationSyntax { get; }

    public CSharpSymbolDefinition Type { get; }

    public bool IsRequired => true;

    public override string ToDisplayString()
    {
        return !Type.IsDefault
            ? $"inject {Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {Name}"
            : $"inject {Name}";
    }
}
