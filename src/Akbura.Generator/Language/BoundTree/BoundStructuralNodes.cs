using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupRoot : BoundNode
{
    public BoundMarkupRoot(
        MarkupRootSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.MarkupRoot, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public new MarkupRootSyntax Syntax => (MarkupRootSyntax)base.Syntax;

    public BoundMarkupRoot Update(
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<BoundNode> children)
    {
        if (symbolInfo.Equals(SymbolInfo) &&
            children == Children)
        {
            return this;
        }

        return new BoundMarkupRoot(
            Syntax,
            Binder,
            symbolInfo,
            Diagnostics,
            children);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupRoot(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupRoot(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupRoot(this, parameter);
}

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

    public new MarkupElementSyntax Syntax => (MarkupElementSyntax)base.Syntax;

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

internal sealed class BoundMarkupContent : BoundNode
{
    public BoundMarkupContent(
        MarkupContentSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.MarkupContent, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public new MarkupContentSyntax Syntax => (MarkupContentSyntax)base.Syntax;

    public BoundMarkupContent Update(
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<BoundNode> children)
    {
        if (symbolInfo.Equals(SymbolInfo) &&
            children == Children)
        {
            return this;
        }

        return new BoundMarkupContent(
            Syntax,
            Binder,
            symbolInfo,
            Diagnostics,
            children);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupContent(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupContent(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupContent(this, parameter);
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

    public new AkcssStyleRuleSyntax Syntax => (AkcssStyleRuleSyntax)base.Syntax;

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

    public new AkcssUtilityDeclarationSyntax Syntax => (AkcssUtilityDeclarationSyntax)base.Syntax;

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
