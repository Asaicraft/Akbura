using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class MetadataTailwindUtilityParameterSymbol : Symbol, ITailwindUtilityParameterSymbol
{
    public MetadataTailwindUtilityParameterSymbol(
        ISymbol containingSymbol,
        int ordinal,
        string name,
        string? csharpName,
        ITypeSymbol type,
        bool isOptional,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations)
        : base(
            containingSymbol,
            locations,
            isImplicitlyDeclared: true)
    {
        Ordinal = ordinal;
        Name = name;
        CSharpName = string.IsNullOrWhiteSpace(csharpName) ? name : csharpName!;
        Type = new CSharpSymbolDefinition(type);
        IsOptional = isOptional;
    }

    public override SymbolKind Kind => SymbolKind.TailwindUtilityParameter;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name { get; }

    public AkcssUtilityParameterSyntax? DeclarationSyntax => null;

    public int Ordinal { get; }

    public CSharpSymbolDefinition Type { get; }

    public IParameterSymbol? CSharpParameter => null;

    public string CSharpName { get; }

    public bool IsOptional { get; }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitTailwindUtilityParameter(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitTailwindUtilityParameter(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitTailwindUtilityParameter(this, parameter);
    }
}
