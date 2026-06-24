using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class CSharpLocalSymbol : Symbol
{
    public CSharpLocalSymbol(
        ILocalSymbol local,
        AkburaSyntax declarationSyntax,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        Local = local ?? throw new ArgumentNullException(nameof(local));
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));
    }

    public override SymbolKind Kind => SymbolKind.CSharpSymbol;

    public override SymbolLanguage Language => SymbolLanguage.CSharp;

    public override string Name => Local.Name;

    public override string MetadataName => Local.MetadataName;

    public override CSharpSymbolDefinition CSharpDefinition => new(Local);

    public ILocalSymbol Local { get; }

    public AkburaSyntax DeclarationSyntax { get; }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitCSharpSymbol(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitCSharpSymbol(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitCSharpSymbol(this, parameter);
    }

    public override string ToDisplayString()
    {
        return Local.ToDisplayString();
    }
}
