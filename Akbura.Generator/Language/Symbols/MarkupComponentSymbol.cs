using Akbura.Language.Operations;
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
        ImmutableArray<IMarkupAttributeOperation> attributeOperations = default,
        IAkburaComponentSymbol? akburaComponent = null,
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

        if (csharpDefinition.IsDefault && akburaComponent == null)
        {
            throw new ArgumentException("Markup component must reference a C# or Akbura component symbol.", nameof(csharpDefinition));
        }

        Name = name;
        CSharpDefinition = csharpDefinition;
        ContentModel = contentModel;
        Children = children.IsDefault
            ? ImmutableArray<MarkupChildContent>.Empty
            : children;
        AttributeOperations = attributeOperations.IsDefault
            ? ImmutableArray<IMarkupAttributeOperation>.Empty
            : attributeOperations;
        AkburaComponent = akburaComponent;
    }

    public override SymbolKind Kind => SymbolKind.MarkupComponent;

    public override SymbolLanguage Language => SymbolLanguage.Markup;

    public override string Name { get; }

    public override string MetadataName => AkburaComponent?.MetadataName
        ?? (string.IsNullOrEmpty(CSharpDefinition.MetadataName)
            ? Name
            : CSharpDefinition.MetadataName);

    public override CSharpSymbolDefinition CSharpDefinition { get; }

    public INamedTypeSymbol? ComponentType => CSharpDefinition.NamedType;

    public MarkupContentModel ContentModel { get; }

    public ImmutableArray<MarkupChildContent> Children { get; }

    public ImmutableArray<IMarkupAttributeOperation> AttributeOperations { get; private set; }

    public IAkburaComponentSymbol? AkburaComponent { get; }

    internal void SetAttributeOperations(ImmutableArray<IMarkupAttributeOperation> attributeOperations)
    {
        AttributeOperations = attributeOperations.IsDefault
            ? ImmutableArray<IMarkupAttributeOperation>.Empty
            : attributeOperations;
    }

    public override string ToDisplayString()
    {
        var csharpDisplay = CSharpDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!string.IsNullOrEmpty(csharpDisplay))
        {
            return $"{Name} -> {csharpDisplay}";
        }

        return AkburaComponent == null
            ? Name
            : $"{Name} -> {AkburaComponent.MetadataName}";
    }
}
