using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal class BoundNamedDeclaration : BoundDeclaration
{
    protected BoundNamedDeclaration(
        BoundKind kind,
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(kind, syntax, binder, symbolInfo, diagnostics, children)
    {
    }
}

internal sealed class BoundComponentDeclaration : BoundNamedDeclaration
{
    public BoundComponentDeclaration(
        AkburaDocumentSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.ComponentDeclaration, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitComponentDeclaration(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitComponentDeclaration(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitComponentDeclaration(this, parameter);
}

internal sealed class BoundStateDeclaration : BoundNamedDeclaration
{
    public BoundStateDeclaration(StateDeclarationSyntax syntax, BinderType binder, AkburaSymbolInfo symbolInfo)
        : base(BoundKind.StateDeclaration, syntax, binder, symbolInfo)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitStateDeclaration(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitStateDeclaration(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitStateDeclaration(this, parameter);
}

internal sealed class BoundParamDeclaration : BoundNamedDeclaration
{
    public BoundParamDeclaration(ParamDeclarationSyntax syntax, BinderType binder, AkburaSymbolInfo symbolInfo)
        : base(BoundKind.ParamDeclaration, syntax, binder, symbolInfo)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitParamDeclaration(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitParamDeclaration(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitParamDeclaration(this, parameter);
}

internal sealed class BoundInjectDeclaration : BoundNamedDeclaration
{
    public BoundInjectDeclaration(InjectDeclarationSyntax syntax, BinderType binder, AkburaSymbolInfo symbolInfo)
        : base(BoundKind.InjectDeclaration, syntax, binder, symbolInfo)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitInjectDeclaration(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitInjectDeclaration(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitInjectDeclaration(this, parameter);
}

internal sealed class BoundCommandDeclaration : BoundNamedDeclaration
{
    public BoundCommandDeclaration(CommandDeclarationSyntax syntax, BinderType binder, AkburaSymbolInfo symbolInfo)
        : base(BoundKind.CommandDeclaration, syntax, binder, symbolInfo)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitCommandDeclaration(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitCommandDeclaration(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitCommandDeclaration(this, parameter);
}

internal sealed class BoundUseEffectDeclaration : BoundNamedDeclaration
{
    public BoundUseEffectDeclaration(UseEffectDeclarationSyntax syntax, BinderType binder, AkburaSymbolInfo symbolInfo)
        : base(BoundKind.UseEffectDeclaration, syntax, binder, symbolInfo)
    {
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitUseEffectDeclaration(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitUseEffectDeclaration(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitUseEffectDeclaration(this, parameter);
}
