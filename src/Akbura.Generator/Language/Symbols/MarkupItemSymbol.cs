using Akbura.Language.Syntax;
using System;

namespace Akbura.Language.Symbols;

internal sealed class MarkupItemSymbol : Symbol, IMarkupItemSymbol
{
    public MarkupItemSymbol(
        string name,
        CSharpSymbolDefinition type,
        MarkupAttachedPropertyAttributeSyntax declarationSyntax,
        ISymbol? containingSymbol = null)
        : base(containingSymbol, isImplicitlyDeclared: true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Markup item symbol name cannot be empty.", nameof(name));
        }

        if (type.Symbol is not Microsoft.CodeAnalysis.ITypeSymbol)
        {
            throw new ArgumentException("Markup item symbol must have a C# type.", nameof(type));
        }

        Name = name;
        Type = type;
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));
    }

    public override SymbolKind Kind => SymbolKind.MarkupItem;

    public override SymbolLanguage Language => SymbolLanguage.Markup;

    public override string Name { get; }

    public CSharpSymbolDefinition Type { get; }

    public MarkupAttachedPropertyAttributeSyntax DeclarationSyntax { get; }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitMarkupItem(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitMarkupItem(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitMarkupItem(this, parameter);
    }

    public override string ToDisplayString()
    {
        return $"{Type.ToDisplayString()} {Name}";
    }
}
