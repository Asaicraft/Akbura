using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using RoslynLocation = Microsoft.CodeAnalysis.Location;

namespace Akbura.Language.Symbols;

internal sealed class AkcssStyleSymbol : Symbol, IAkcssSymbol
{
    public AkcssStyleSymbol(
        AkcssStyleRuleSyntax declarationSyntax,
        CSharpSymbolDefinition targetType,
        ImmutableArray<IAkcssOperation> operations,
        ISymbol? containingSymbol = null,
        ImmutableArray<RoslynLocation> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));

        var name = declarationSyntax.Selector.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Akcss style symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        TargetType = targetType;
        Operations = operations.IsDefault
            ? ImmutableArray<IAkcssOperation>.Empty
            : operations;
    }

    public override SymbolKind Kind => SymbolKind.AkcssClass;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name { get; }

    AkburaSyntax IAkcssSymbol.DeclarationSyntax => DeclarationSyntax;

    public AkcssStyleRuleSyntax DeclarationSyntax { get; }

    public ImmutableArray<IAkcssOperation> Operations { get; private set; }

    internal void SetOperations(ImmutableArray<IAkcssOperation> operations)
    {
        Operations = operations.IsDefault
            ? ImmutableArray<IAkcssOperation>.Empty
            : operations;
    }

    public bool HasTargetType => !TargetType.IsDefault;

    public CSharpSymbolDefinition TargetType { get; }

    public override string MetadataName => HasTargetType
        ? TargetType.Name + "." + Name
        : Name;

    public override string ToDisplayString()
    {
        return $"style {MetadataName}";
    }
}
