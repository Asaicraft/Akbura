using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

/// <summary>
/// A bound AKCSS construct whose meaning depends on its selector or inclusion context.
/// The data is immutable and can be used to materialize independently owned operation trees.
/// </summary>
internal sealed class BoundAkcssContextual(
    BoundKind kind,
    AkburaSyntax syntax,
    BinderType binder,
    AkcssBindingContext context,
    AkcssBoundData? data,
    AkburaSymbolInfo symbolInfo,
    ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
    ImmutableArray<BoundNode> children = default,
    bool hasErrors = false)
    : BoundNode(kind, syntax, binder, symbolInfo, diagnostics, children, hasErrors)
{
    public AkcssBindingContext Context { get; } = context;

    public AkcssBoundData? Data { get; } = data;

    public BoundAkcssContextual WithChildren(ImmutableArray<BoundNode> children) =>
        Children == children ? this : new BoundAkcssContextual(
            Kind, Syntax, Binder, Context, Data, SymbolInfo, Diagnostics, children, HasErrors);

    public BoundAkcssContextual WithDiagnostics(ImmutableArray<AkburaSemanticDiagnostic> diagnostics) =>
        new(Kind, Syntax, Binder, Context, Data, SymbolInfo, diagnostics, Children, HasErrors);

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssContextual(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor) where TResult : default =>
        visitor.VisitAkcssContextual(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor, TParameter parameter) where TResult : default =>
        visitor.VisitAkcssContextual(this, parameter);
}
