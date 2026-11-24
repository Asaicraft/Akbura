using Akbura.Language.Operations;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IAkcssSymbol : IAkcssDeclarationOwner
{
    ImmutableArray<IAkcssOperation> Operations { get; }

    string? ClassName { get; }

    bool HasTargetType { get; }

    bool IsIntercepted { get; }

    CSharpSymbolDefinition InterceptType { get; }
}
