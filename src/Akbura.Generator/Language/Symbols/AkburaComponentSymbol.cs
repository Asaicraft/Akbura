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
    private ImmutableArray<MarkupChildContent> _children;
    private ImmutableArray<IMarkupComponentSymbol> _markupRoots;

    public AkburaComponentSymbol(
        AkburaSyntaxTree syntaxTree,
        AkburaDocumentSyntax declarationSyntax,
        string name,
        string namespaceName,
        ImmutableArray<INamedTypeSymbol> partialTypes,
        CSharpSymbolDefinition baseType,
        bool hasExplicitBaseType,
        MarkupContentModel contentModel,
        ImmutableArray<MarkupChildContent> children,
        ImmutableArray<IMarkupComponentSymbol> markupRoots,
        ImmutableArray<IStateSymbol> states,
        ImmutableArray<IParamSymbol> parameters,
        ImmutableArray<IInjectSymbol> injectedServices,
        ImmutableArray<ICommandSymbol> commands,
        ImmutableArray<IAkcssModuleSymbol> akcssModules,
        bool deferMarkupInitialization = false,
        ISymbol? containingSymbol = null,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations = default,
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
        BaseType = baseType;
        HasExplicitBaseType = hasExplicitBaseType;
        ContentModel = contentModel;
        _children = deferMarkupInitialization
            ? default
            : children.IsDefault
                ? ImmutableArray<MarkupChildContent>.Empty
                : children;
        _markupRoots = deferMarkupInitialization
            ? default
            : markupRoots.IsDefault
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
        AkcssModules = akcssModules.IsDefault
            ? ImmutableArray<IAkcssModuleSymbol>.Empty
            : akcssModules;

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

    public ImmutableArray<MarkupChildContent> Children => _children.IsDefault
        ? ImmutableArray<MarkupChildContent>.Empty
        : _children;

    public ImmutableArray<IMarkupAttributeOperation> AttributeOperations => ImmutableArray<IMarkupAttributeOperation>.Empty;

    public IAkburaComponentSymbol AkburaComponent => this;

    public AkburaSyntaxTree SyntaxTree { get; }

    public AkburaDocumentSyntax DeclarationSyntax { get; }

    public string NamespaceName { get; }

    public CSharpSymbolDefinition BaseType { get; }

    public bool HasExplicitBaseType { get; }

    public ImmutableArray<INamedTypeSymbol> PartialTypes { get; }

    public ImmutableArray<IMarkupComponentSymbol> MarkupRoots => _markupRoots.IsDefault
        ? ImmutableArray<IMarkupComponentSymbol>.Empty
        : _markupRoots;

    public ImmutableArray<IStateSymbol> States { get; }

    public ImmutableArray<IParamSymbol> Parameters { get; }

    public ImmutableArray<IInjectSymbol> InjectedServices { get; }

    public ImmutableArray<ICommandSymbol> Commands { get; }

    public ImmutableArray<IAkcssModuleSymbol> AkcssModules { get; private set; }

    internal void SetAkcssModules(ImmutableArray<IAkcssModuleSymbol> akcssModules)
    {
        AkcssModules = akcssModules.IsDefault
            ? ImmutableArray<IAkcssModuleSymbol>.Empty
            : akcssModules;
    }

    internal void SetMarkupContract(
        ImmutableArray<MarkupChildContent> children,
        ImmutableArray<IMarkupComponentSymbol> markupRoots)
    {
        ImmutableInterlocked.InterlockedInitialize(
            ref _children,
            children.IsDefault ? ImmutableArray<MarkupChildContent>.Empty : children);
        ImmutableInterlocked.InterlockedInitialize(
            ref _markupRoots,
            markupRoots.IsDefault ? ImmutableArray<IMarkupComponentSymbol>.Empty : markupRoots);
    }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitAkburaComponent(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitAkburaComponent(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkburaComponent(this, parameter);
    }

    public override string ToDisplayString()
    {
        return MetadataName;
    }
}
