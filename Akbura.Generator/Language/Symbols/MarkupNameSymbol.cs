using Akbura.Language.Syntax;
using System;

namespace Akbura.Language.Symbols;

internal sealed class MarkupNameSymbol : Symbol, IMarkupNameSymbol
{
    public MarkupNameSymbol(
        string name,
        string identifierText,
        CSharpSymbolDefinition type,
        MarkupAttachedPropertyAttributeSyntax declarationSyntax,
        MarkupElementSyntax elementSyntax,
        ISymbol? containingSymbol = null)
        : base(containingSymbol)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Markup name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(identifierText))
        {
            throw new ArgumentException("Markup name identifier cannot be empty.", nameof(identifierText));
        }

        if (type.Symbol is not Microsoft.CodeAnalysis.ITypeSymbol)
        {
            throw new ArgumentException("Markup name must have a C# type.", nameof(type));
        }

        Name = name;
        IdentifierText = identifierText;
        Type = type;
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));
        ElementSyntax = elementSyntax ?? throw new ArgumentNullException(nameof(elementSyntax));
    }

    public override SymbolKind Kind => SymbolKind.MarkupName;

    public override SymbolLanguage Language => SymbolLanguage.Markup;

    public override string Name { get; }

    public string IdentifierText { get; }

    public CSharpSymbolDefinition Type { get; }

    public MarkupAttachedPropertyAttributeSyntax DeclarationSyntax { get; }

    public MarkupElementSyntax ElementSyntax { get; }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitMarkupName(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitMarkupName(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitMarkupName(this, parameter);
    }

    public override string ToDisplayString()
    {
        return $"{Type.ToDisplayString()} {IdentifierText}";
    }
}
