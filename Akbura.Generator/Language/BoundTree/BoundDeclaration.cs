using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundDeclaration : BoundNode
{
    public BoundDeclaration(
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        IOperation? operation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
        : base(syntax, binder, symbolInfo, operation, diagnostics, children)
    {
    }
}
