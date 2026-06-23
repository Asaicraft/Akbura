using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.BoundTree;

internal sealed class BoundBlock : BoundNode
{
    public BoundBlock(
        AkburaSyntax syntax,
        BinderType binder,
        ImmutableArray<AkburaSymbol> declaredSymbols,
        ImmutableArray<BoundNode> statements,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            syntax,
            binder,
            AkburaSymbolInfo.None(CandidateReason.None),
            operation: null,
            diagnostics,
            statements)
    {
        DeclaredSymbols = declaredSymbols.IsDefault
            ? ImmutableArray<AkburaSymbol>.Empty
            : declaredSymbols;
        Statements = Children;
    }

    public ImmutableArray<AkburaSymbol> DeclaredSymbols { get; }

    public ImmutableArray<BoundNode> Statements { get; }
}
