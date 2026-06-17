using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IAkcssApplyOperation : IAkcssOperation
{
    new AkcssApplyDirectiveSyntax Syntax { get; }

    ImmutableArray<string> Items { get; }

    ImmutableArray<IAkcssSymbol> AppliedSymbols { get; }
}
