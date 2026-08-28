using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface ICommandSymbol : ISymbol
{
    CommandDeclarationSyntax DeclarationSyntax { get; }

    CSharpSymbolDefinition ReturnType { get; }

    CSharpSymbolDefinition ResultType { get; }

    ImmutableArray<ICommandParameterSymbol> Parameters { get; }

    bool IsVoid { get; }

    bool IsAsyncLike { get; }

    bool HasResult { get; }

    bool SupportsIsExecuting { get; }
}
