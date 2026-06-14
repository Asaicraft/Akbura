using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using RoslynLocation = Microsoft.CodeAnalysis.Location;

namespace Akbura.Language.Symbols;

internal sealed class TailwindUtilitySymbol : Symbol, ITailwindUtilitySymbol
{
    public TailwindUtilitySymbol(
        AkcssUtilityDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition targetType,
        ImmutableArray<ITailwindUtilityParameterSymbol> parameters,
        ISymbol? containingSymbol = null,
        ImmutableArray<RoslynLocation> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));

        var name = declarationSyntax.Selector.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tailwind utility symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        TargetType = targetType;
        Parameters = parameters.IsDefault
            ? ImmutableArray<ITailwindUtilityParameterSymbol>.Empty
            : parameters;
    }

    public override SymbolKind Kind => SymbolKind.AkcssUtility;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name { get; }

    AkburaSyntax IAkcssSymbol.DeclarationSyntax => DeclarationSyntax;

    public AkcssUtilityDeclarationSyntax DeclarationSyntax { get; }

    public bool HasTargetType => !TargetType.IsDefault;

    public CSharpSymbolDefinition TargetType { get; }

    public ImmutableArray<ITailwindUtilityParameterSymbol> Parameters { get; }

    public override string MetadataName => !HasTargetType
        ? Name
        : TargetType.Name + "." + Name;

    public override string ToDisplayString()
    {
        return Parameters.Length == 0
            ? $"utility {MetadataName}"
            : $"utility {MetadataName}/{Parameters.Length}";
    }
}
