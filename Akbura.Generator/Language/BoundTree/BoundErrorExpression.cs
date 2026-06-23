using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundErrorExpression : BoundExpression
{
    public BoundErrorExpression(
        AkburaSyntax syntax,
        BinderType binder,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
        : base(
            syntax,
            binder,
            AkburaSymbolInfo.None(CandidateReason.NotFound),
            operation: null,
            diagnostics,
            children)
    {
    }

    public override bool IsError => true;
}
