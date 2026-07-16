using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.Language.Symbols;

internal sealed class ModuleAkburaComponentSymbol : Symbol, IAkburaComponentSymbol
{
    private readonly AkburaReferencedSource _source;
    private readonly int _sourceStart;
    private readonly int _sourceLength;
    private AkburaDocumentSyntax? _lazySyntax;

    public ModuleAkburaComponentSymbol(
        AkburaReferencedSource source,
        AkburaModuleDeclaration declaration,
        AkburaModuleTypeResolver typeResolver)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (declaration == null)
        {
            throw new ArgumentNullException(nameof(declaration));
        }

        var component = declaration.Component ?? throw new ArgumentException(
            "The module declaration does not contain a component signature.",
            nameof(declaration));
        Name = declaration.Name;
        MetadataName = RemoveGlobalAlias(declaration.MetadataName ?? declaration.Name);
        var namespaceSeparator = MetadataName.LastIndexOf('.');
        NamespaceName = namespaceSeparator < 0
            ? string.Empty
            : MetadataName[..namespaceSeparator];
        _sourceStart = declaration.SourceStart;
        _sourceLength = declaration.SourceLength;

        CSharpDefinition = typeResolver.Resolve(MetadataName);
        BaseType = typeResolver.Resolve(component.BaseTypeName);
        PartialTypes = CSharpDefinition.NamedType is { } componentType
            ? [componentType]
            : ImmutableArray<INamedTypeSymbol>.Empty;

        using var parameters = ImmutableArrayBuilder<IParamSymbol>.Rent(component.Parameters.Length);
        foreach (var parameter in component.Parameters)
        {
            parameters.Add(new ModuleParamSymbol(
                source,
                parameter,
                typeResolver.Resolve(parameter.TypeName),
                this));
        }

        Parameters = parameters.ToImmutable();

        using var injectedServices = ImmutableArrayBuilder<IInjectSymbol>.Rent(
            component.InjectedServices.Length);
        foreach (var injectedService in component.InjectedServices)
        {
            injectedServices.Add(new ModuleInjectSymbol(
                source,
                injectedService,
                typeResolver.Resolve(injectedService.TypeName),
                this));
        }

        InjectedServices = injectedServices.ToImmutable();
    }

    public override SymbolKind Kind => SymbolKind.AkburaComponent;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public override string MetadataName { get; }

    public override CSharpSymbolDefinition CSharpDefinition { get; }

    public INamedTypeSymbol? ComponentType => CSharpDefinition.NamedType;

    public MarkupContentModel ContentModel => default;

    public ImmutableArray<MarkupChildContent> Children => [];

    public ImmutableArray<IMarkupAttributeOperation> AttributeOperations => [];

    public IAkburaComponentSymbol AkburaComponent => this;

    public AkburaSyntaxTree SyntaxTree => _source.GetSyntaxTree();

    public AkburaDocumentSyntax DeclarationSyntax
    {
        get
        {
            var syntax = Volatile.Read(ref _lazySyntax);
            if (syntax != null)
            {
                return syntax;
            }

            syntax = _source.GetSyntax<AkburaDocumentSyntax>(_sourceStart, _sourceLength);
            return Interlocked.CompareExchange(ref _lazySyntax, syntax, null) ?? syntax;
        }
    }

    public string NamespaceName { get; }

    public CSharpSymbolDefinition BaseType { get; }

    public bool HasExplicitBaseType => !BaseType.IsDefault;

    public ImmutableArray<INamedTypeSymbol> PartialTypes { get; }

    public ImmutableArray<IMarkupComponentSymbol> MarkupRoots => [];

    public ImmutableArray<IStateSymbol> States => [];

    public ImmutableArray<IParamSymbol> Parameters { get; }

    public ImmutableArray<IInjectSymbol> InjectedServices { get; }

    public ImmutableArray<ICommandSymbol> Commands => [];

    public ImmutableArray<IUseEffectSymbol> UseEffects => [];

    public ImmutableArray<UserHookSyntax> UserHooks => [];

    public ImmutableArray<IAkcssModuleSymbol> AkcssModules => [];

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

    private static string RemoveGlobalAlias(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name["global::".Length..]
            : name;
    }
}
