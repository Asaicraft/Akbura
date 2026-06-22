using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Symbols;

internal sealed class UseEffectSymbol : Symbol, IUseEffectSymbol
{
    public UseEffectSymbol(
        UseEffectDeclarationSyntax declarationSyntax,
        ImmutableArray<UseEffectDependency> dependencies,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));
        Dependencies = dependencies.IsDefault
            ? ImmutableArray<UseEffectDependency>.Empty
            : dependencies;

        foreach (var tail in declarationSyntax.Tails)
        {
            switch (tail)
            {
                case EffectCancelBlockSyntax cancelBlock:
                    CancelBlock = cancelBlock;
                    break;

                case EffectFinallyBlockSyntax finallyBlock:
                    FinallyBlock = finallyBlock;
                    break;
            }
        }
    }

    public override SymbolKind Kind => SymbolKind.UseEffect;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name => "useEffect";

    public override string MetadataName => "useEffect@" + DeclarationSyntax.Position.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public UseEffectDeclarationSyntax DeclarationSyntax { get; }

    public CSharpArgumentListSyntax ArgumentsSyntax => DeclarationSyntax.Arguments;

    public ImmutableArray<UseEffectDependency> Dependencies { get; }

    public CSharpBlockSyntax Body => DeclarationSyntax.Body;

    public EffectCancelBlockSyntax? CancelBlock { get; }

    public EffectFinallyBlockSyntax? FinallyBlock { get; }

    public bool HasCancelBlock => CancelBlock != null;

    public bool HasFinallyBlock => FinallyBlock != null;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitUseEffect(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitUseEffect(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitUseEffect(this, parameter);
    }

    public override string ToDisplayString()
    {
        return Dependencies.Length == 0
            ? "useEffect()"
            : "useEffect(" + string.Join(", ", Dependencies.Select(static dependency => dependency.ExpressionText)) + ")";
    }
}
