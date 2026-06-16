using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Symbols;

internal sealed class AkburaComponentSymbol : Symbol, IAkburaComponentSymbol
{
    private readonly CSharpSymbolDefinition _csharpDefinition;

    public AkburaComponentSymbol(
        AkburaSyntaxTree syntaxTree,
        AkburaDocumentSyntax declarationSyntax,
        string name,
        string namespaceName,
        ImmutableArray<INamedTypeSymbol> partialTypes,
        MarkupContentModel contentModel,
        ImmutableArray<MarkupChildContent> children,
        ImmutableArray<IMarkupComponentSymbol> markupRoots,
        ImmutableArray<IStateSymbol> states,
        ImmutableArray<IParamSymbol> parameters,
        ImmutableArray<IInjectSymbol> injectedServices,
        ImmutableArray<ICommandSymbol> commands,
        ImmutableArray<UseEffectDeclarationSyntax> useEffects,
        ImmutableArray<UserHookSyntax> userHooks,
        ImmutableArray<InlineAkcssBlockSyntax> inlineAkcssBlocks,
        ISymbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Akbura component name cannot be empty.", nameof(name));
        }

        Name = name;
        NamespaceName = namespaceName ?? string.Empty;
        PartialTypes = partialTypes.IsDefault
            ? ImmutableArray<INamedTypeSymbol>.Empty
            : partialTypes;
        ContentModel = contentModel;
        Children = children.IsDefault
            ? ImmutableArray<MarkupChildContent>.Empty
            : children;
        MarkupRoots = markupRoots.IsDefault
            ? ImmutableArray<IMarkupComponentSymbol>.Empty
            : markupRoots;
        States = states.IsDefault
            ? ImmutableArray<IStateSymbol>.Empty
            : states;
        Parameters = parameters.IsDefault
            ? ImmutableArray<IParamSymbol>.Empty
            : parameters;
        InjectedServices = injectedServices.IsDefault
            ? ImmutableArray<IInjectSymbol>.Empty
            : injectedServices;
        Commands = commands.IsDefault
            ? ImmutableArray<ICommandSymbol>.Empty
            : commands;
        UseEffects = useEffects.IsDefault
            ? ImmutableArray<UseEffectDeclarationSyntax>.Empty
            : useEffects;
        UserHooks = userHooks.IsDefault
            ? ImmutableArray<UserHookSyntax>.Empty
            : userHooks;
        InlineAkcssBlocks = inlineAkcssBlocks.IsDefault
            ? ImmutableArray<InlineAkcssBlockSyntax>.Empty
            : inlineAkcssBlocks;

        var csharpType = PartialTypes.Length == 1
            ? PartialTypes[0]
            : PartialTypes.FirstOrDefault();
        _csharpDefinition = csharpType == null
            ? default
            : new CSharpSymbolDefinition(csharpType);
    }

    public override SymbolKind Kind => SymbolKind.AkburaComponent;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public override string MetadataName => string.IsNullOrEmpty(NamespaceName)
        ? Name
        : NamespaceName + "." + Name;

    public override CSharpSymbolDefinition CSharpDefinition => _csharpDefinition;

    public INamedTypeSymbol? ComponentType => CSharpDefinition.NamedType;

    public MarkupContentModel ContentModel { get; }

    public ImmutableArray<MarkupChildContent> Children { get; }

    public ImmutableArray<IMarkupAttributeOperation> AttributeOperations => ImmutableArray<IMarkupAttributeOperation>.Empty;

    public IAkburaComponentSymbol AkburaComponent => this;

    public AkburaSyntaxTree SyntaxTree { get; }

    public AkburaDocumentSyntax DeclarationSyntax { get; }

    public string NamespaceName { get; }

    public ImmutableArray<INamedTypeSymbol> PartialTypes { get; }

    public ImmutableArray<IMarkupComponentSymbol> MarkupRoots { get; }

    public ImmutableArray<IStateSymbol> States { get; }

    public ImmutableArray<IParamSymbol> Parameters { get; }

    public ImmutableArray<IInjectSymbol> InjectedServices { get; }

    public ImmutableArray<ICommandSymbol> Commands { get; }

    public ImmutableArray<UseEffectDeclarationSyntax> UseEffects { get; }

    public ImmutableArray<UserHookSyntax> UserHooks { get; }

    public ImmutableArray<InlineAkcssBlockSyntax> InlineAkcssBlocks { get; }

    public override string ToDisplayString()
    {
        return MetadataName;
    }
}
