using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class MarkupComponentSymbol : Symbol, IMarkupComponentSymbol
{
    public MarkupComponentSymbol(
        string name,
        CSharpSymbolDefinition csharpDefinition,
        MarkupContentModel contentModel = default,
        ImmutableArray<MarkupChildContent> children = default,
        ImmutableArray<IParamSymbol> parameters = default,
        ImmutableArray<ICommandSymbol> commands = default,
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

        if (csharpDefinition.IsDefault)
        {
            throw new ArgumentException("Markup component must reference a C# symbol.", nameof(csharpDefinition));
        }

        Name = name;
        CSharpDefinition = csharpDefinition;
        ContentModel = contentModel;
        Children = children.IsDefault
            ? ImmutableArray<MarkupChildContent>.Empty
            : children;
        Parameters = parameters.IsDefault
            ? ImmutableArray<IParamSymbol>.Empty
            : parameters;
        Commands = commands.IsDefault
            ? ImmutableArray<ICommandSymbol>.Empty
            : commands;
    }

    public override SymbolKind Kind => SymbolKind.MarkupComponent;

    public override SymbolLanguage Language => SymbolLanguage.Markup;

    public override string Name { get; }

    public override string MetadataName => string.IsNullOrEmpty(CSharpDefinition.MetadataName)
        ? Name
        : CSharpDefinition.MetadataName;

    public override CSharpSymbolDefinition CSharpDefinition { get; }

    public INamedTypeSymbol? ComponentType => CSharpDefinition.NamedType;

    public MarkupContentModel ContentModel { get; }

    public ImmutableArray<MarkupChildContent> Children { get; }

    public ImmutableArray<IParamSymbol> Parameters { get; }

    public ImmutableArray<ICommandSymbol> Commands { get; }

    public override string ToDisplayString()
    {
        var csharpDisplay = CSharpDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.IsNullOrEmpty(csharpDisplay) ? Name : $"{Name} -> {csharpDisplay}";
    }
}
