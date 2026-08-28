using Akbura.Language.Binder;
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
    public BoundStateDeclaration(
        StateDeclarationSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.StateDeclaration, syntax, binder, symbolInfo, diagnostics, children)
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

internal sealed class BoundStateInitializer : BoundNode
{
    public BoundStateInitializer(
        StateInitializerSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        StateBindingKind bindingKind,
        IUseHookSymbol? useHook,
        BoundUseHookInvocation? useHookInvocation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.StateInitializer,
            syntax,
            binder,
            useHook != null
                ? AkburaSymbolInfo.Success(useHook)
                : bindingResult.Symbol == null
                ? AkburaSymbolInfo.None(bindingResult.CandidateReason)
                : AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax),
            diagnostics,
            useHookInvocation == null
                ? ImmutableArray<BoundNode>.Empty
                : ImmutableArray.Create<BoundNode>(useHookInvocation),
            hasErrors: hasErrors || useHookInvocation?.HasErrors == true)
    {
        BindingResult = bindingResult;
        BindingKind = bindingKind;
        UseHook = useHook;
        UseHookInvocation = useHookInvocation;
    }

    public new StateInitializerSyntax Syntax => (StateInitializerSyntax)base.Syntax;

    public CSharpBindingResult BindingResult { get; }

    public StateBindingKind BindingKind { get; }

    public IUseHookSymbol? UseHook { get; }

    public BoundUseHookInvocation? UseHookInvocation { get; }

    public BoundStateInitializer Update(
        CSharpBindingResult bindingResult,
        StateBindingKind bindingKind,
        IUseHookSymbol? useHook,
        BoundUseHookInvocation? useHookInvocation)
    {
        if (bindingResult.Equals(BindingResult) &&
            bindingKind == BindingKind &&
            ReferenceEquals(useHook, UseHook) &&
            ReferenceEquals(useHookInvocation, UseHookInvocation))
        {
            return this;
        }

        return new BoundStateInitializer(
            Syntax,
            Binder,
            bindingResult,
            bindingKind,
            useHook,
            useHookInvocation,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitStateInitializer(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitStateInitializer(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitStateInitializer(this, parameter);
}

internal sealed class BoundParamDeclaration : BoundNamedDeclaration
{
    public BoundParamDeclaration(
        ParamDeclarationSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.ParamDeclaration, syntax, binder, symbolInfo, diagnostics, children)
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

internal sealed class BoundParamDefaultValue : BoundNode
{
    public BoundParamDefaultValue(
        CSharpExpressionSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.ParamDefaultValue,
            syntax,
            binder,
            bindingResult.Symbol == null
                ? AkburaSymbolInfo.None(bindingResult.CandidateReason)
                : AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax),
            diagnostics,
            hasErrors: hasErrors)
    {
        BindingResult = bindingResult;
    }

    public new CSharpExpressionSyntax Syntax => (CSharpExpressionSyntax)base.Syntax;

    public CSharpBindingResult BindingResult { get; }

    public BoundParamDefaultValue Update(CSharpBindingResult bindingResult)
    {
        if (bindingResult.Equals(BindingResult))
        {
            return this;
        }

        return new BoundParamDefaultValue(
            Syntax,
            Binder,
            bindingResult,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitParamDefaultValue(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitParamDefaultValue(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitParamDefaultValue(this, parameter);
}

internal sealed class BoundInjectDeclaration : BoundNamedDeclaration
{
    public BoundInjectDeclaration(
        InjectDeclarationSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(BoundKind.InjectDeclaration, syntax, binder, symbolInfo, diagnostics)
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
    public BoundCommandDeclaration(
        CommandDeclarationSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(BoundKind.CommandDeclaration, syntax, binder, symbolInfo, diagnostics)
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
