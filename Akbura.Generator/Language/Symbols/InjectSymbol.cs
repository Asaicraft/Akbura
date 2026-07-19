using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Symbols;

internal sealed class InjectSymbol : Symbol, IInjectSymbol
{
    public InjectSymbol(
        InjectDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition type,
        ISymbol? containingSymbol = null,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations = default,
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
        IsOptional = IsOptionalType(declarationSyntax);
    }

    public override SymbolKind Kind => SymbolKind.InjectedService;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public InjectDeclarationSyntax DeclarationSyntax { get; }

    public CSharpSymbolDefinition Type { get; }

    public bool IsOptional { get; }

    public bool IsRequired => !IsOptional;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitInject(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitInject(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitInject(this, parameter);
    }

    public override string ToDisplayString()
    {
        return !Type.IsDefault
            ? $"inject {Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {Name}"
            : $"inject {Name}";
    }

    private static bool IsOptionalType(InjectDeclarationSyntax declarationSyntax)
    {
        try
        {
            return declarationSyntax.Type.ToCSharp() is CSharp.NullableTypeSyntax;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
