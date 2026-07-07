using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class AkcssModuleSymbol : Symbol, IAkcssModuleSymbol
{
    public AkcssModuleSymbol(
        AkburaSyntax declaringSyntax,
        bool isInlined,
        IAkburaComponentSymbol? containingSymbol,
        ImmutableArray<IAkcssSymbol> akcssSymbols,
        string? path,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclaringSyntax = declaringSyntax ?? throw new ArgumentNullException(nameof(declaringSyntax));
        IsInlined = isInlined;
        AkcssSymbols = akcssSymbols.IsDefault
            ? ImmutableArray<IAkcssSymbol>.Empty
            : akcssSymbols;
        Path = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public override SymbolKind Kind => SymbolKind.AkcssModule;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name => Path ?? "<inline akcss>";

    public override string MetadataName => Path ?? (ContainingSymbol == null
        ? Name
        : ContainingSymbol.MetadataName + ".akcss");

    public bool IsInlined { get; }

    public new IAkburaComponentSymbol? ContainingSymbol => (IAkburaComponentSymbol?)base.ContainingSymbol;

    public ImmutableArray<IAkcssSymbol> AkcssSymbols { get; }

    public string? Path { get; }

    public AkburaSyntax DeclaringSyntax { get; }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitAkcssModule(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitAkcssModule(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssModule(this, parameter);
    }

    public override string ToDisplayString()
    {
        return IsInlined
            ? $"inline akcss {ContainingSymbol?.MetadataName ?? string.Empty}".TrimEnd()
            : $"akcss {Path}";
    }
}
