using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupComponent : BoundNode
{
    public BoundMarkupComponent(
        MarkupElementSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.MarkupComponent, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupComponent(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupComponent(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupComponent(this, parameter);
}

internal sealed class BoundAkcssModule : BoundNode
{
    public BoundAkcssModule(
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.AkcssModule, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssModule(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitAkcssModule(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitAkcssModule(this, parameter);
}

internal sealed class BoundAkcssStyle : BoundNode
{
    public BoundAkcssStyle(
        AkcssStyleRuleSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.AkcssStyle, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssStyle(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitAkcssStyle(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitAkcssStyle(this, parameter);
}

internal sealed class BoundAkcssUtility : BoundNode
{
    public BoundAkcssUtility(
        AkcssUtilityDeclarationSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.AkcssUtility, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitAkcssUtility(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitAkcssUtility(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitAkcssUtility(this, parameter);
}
