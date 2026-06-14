using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class TailwindUtilityParameterSymbol : Symbol, ITailwindUtilityParameterSymbol
{
    public TailwindUtilityParameterSymbol(
        AkcssUtilityParameterSyntax declarationSyntax,
        int ordinal,
        CSharpSymbolDefinition type,
        IParameterSymbol? csharpParameter = null,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));

        var name = declarationSyntax.ParamName.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tailwind utility parameter symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        Ordinal = ordinal;
        Type = type;
        CSharpParameter = csharpParameter;
    }

    public override SymbolKind Kind => SymbolKind.Parameter;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name { get; }

    public AkcssUtilityParameterSyntax DeclarationSyntax { get; }

    public int Ordinal { get; }

    public CSharpSymbolDefinition Type { get; }

    public IParameterSymbol? CSharpParameter { get; }

    public override CSharpSymbolDefinition CSharpDefinition => CSharpParameter == null
        ? default
        : new CSharpSymbolDefinition(CSharpParameter);

    public override string ToDisplayString()
    {
        return Type.IsDefault ? Name : $"{Type.Name} {Name}";
    }
}
