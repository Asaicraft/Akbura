using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class AkburaMarkupComponentSymbol : Symbol, IMarkupComponentSymbol
{
    public AkburaMarkupComponentSymbol(
        string name,
        string metadataName,
        AkburaSyntaxTree syntaxTree,
        ImmutableArray<IParamSymbol> parameters,
        MarkupContentModel contentModel = default,
        ImmutableArray<MarkupChildContent> children = default,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Markup component name cannot be empty.", nameof(name));
        }

        Name = name;
        MetadataName = string.IsNullOrWhiteSpace(metadataName) ? name : metadataName;
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        Parameters = parameters.IsDefault
            ? ImmutableArray<IParamSymbol>.Empty
            : parameters;
        ContentModel = contentModel;
        Children = children.IsDefault
            ? ImmutableArray<MarkupChildContent>.Empty
            : children;
    }

    public override SymbolKind Kind => SymbolKind.MarkupComponent;

    public override SymbolLanguage Language => SymbolLanguage.Markup;

    public override string Name { get; }

    public override string MetadataName { get; }

    public AkburaSyntaxTree SyntaxTree { get; }

    public INamedTypeSymbol? ComponentType => null;

    public MarkupContentModel ContentModel { get; }

    public ImmutableArray<MarkupChildContent> Children { get; }

    public ImmutableArray<IParamSymbol> Parameters { get; }

    public override string ToDisplayString()
    {
        return $"{Name} -> {MetadataName}";
    }
}
