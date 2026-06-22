using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Binding;

internal abstract class BoundNode
{
    protected BoundNode(
        AkburaSyntax syntax,
        Binder binder,
        AkburaSymbolInfo symbolInfo,
        IOperation? operation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
    {
        Syntax = syntax;
        Binder = binder;
        SymbolInfo = symbolInfo;
        Operation = operation;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : diagnostics;
        Children = children.IsDefault
            ? ImmutableArray<BoundNode>.Empty
            : children;
    }

    public AkburaSyntax Syntax { get; }

    public Binder Binder { get; }

    public AkburaSymbolInfo SymbolInfo { get; }

    public IOperation? Operation { get; }

    public ImmutableArray<AkburaSemanticDiagnostic> Diagnostics { get; }

    public ImmutableArray<BoundNode> Children { get; }
}
