using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language;

/// <summary>
/// Immutable lookup data owned by one semantic-model context. Concrete lookup
/// symbols are created separately to preserve identity and isolate mutable state.
/// </summary>
internal readonly struct AkcssLookupSymbolDescriptor
{
    private readonly AkburaSyntax _declaration;
    private readonly CSharpSymbolDefinition _targetType;
    private readonly ImmutableArray<ITailwindUtilityParameterSymbol> _parameters;

    public AkcssLookupSymbolDescriptor(AkcssStyleRuleSyntax declaration, CSharpSymbolDefinition targetType)
    {
        _declaration = declaration;
        _targetType = targetType;
        _parameters = ImmutableArray<ITailwindUtilityParameterSymbol>.Empty;
    }

    public AkcssLookupSymbolDescriptor(
        AkcssUtilityDeclarationSyntax declaration,
        CSharpSymbolDefinition targetType,
        ImmutableArray<ITailwindUtilityParameterSymbol> parameters)
    {
        _declaration = declaration;
        _targetType = targetType;
        _parameters = parameters;
    }

    public IAkcssSymbol CreateSymbol()
    {
        return _declaration switch
        {
            AkcssStyleRuleSyntax style => new AkcssStyleSymbol(
                style, _targetType, ImmutableArray<IAkcssOperation>.Empty),
            AkcssUtilityDeclarationSyntax utility => new TailwindUtilitySymbol(
                utility, _targetType, _parameters, ImmutableArray<IAkcssOperation>.Empty),
            _ => ThrowHelper.UnexpectedValue<IAkcssSymbol>(_declaration.Kind),
        };
    }
}
