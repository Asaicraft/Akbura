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
        IUserHookSymbol? userHook,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.StateInitializer,
            syntax,
            binder,
            bindingResult.Symbol == null
                ? AkburaSymbolInfo.None(bindingResult.CandidateReason)
                : AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax),
            diagnostics,
            hasErrors: hasErrors)
    {
        BindingResult = bindingResult;
        BindingKind = bindingKind;
        UserHook = userHook;
    }

    public new StateInitializerSyntax Syntax => (StateInitializerSyntax)base.Syntax;

    public CSharpBindingResult BindingResult { get; }

    public StateBindingKind BindingKind { get; }

    public IUserHookSymbol? UserHook { get; }

    public BoundStateInitializer Update(
        CSharpBindingResult bindingResult,
        StateBindingKind bindingKind,
        IUserHookSymbol? userHook)
    {
        if (bindingResult.Equals(BindingResult) &&
            bindingKind == BindingKind &&
            ReferenceEquals(userHook, UserHook))
        {
            return this;
        }

        return new BoundStateInitializer(
            Syntax,
            Binder,
            bindingResult,
            bindingKind,
            userHook,
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

internal sealed class BoundUseEffectDeclaration : BoundNamedDeclaration
{
    public BoundUseEffectDeclaration(
        UseEffectDeclarationSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.UseEffectDeclaration, syntax, binder, symbolInfo, diagnostics, children)
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

internal sealed class BoundUseEffectDependency : BoundNode
{
    public BoundUseEffectDependency(
        CSharpArgumentListSyntax syntax,
        BinderType binder,
        UseEffectDependency dependency,
        CSharpBindingResult bindingResult,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.UseEffectDependency,
            syntax,
            binder,
            dependency.AkburaSymbol == null
                ? AkburaSymbolInfo.None(bindingResult.CandidateReason)
                : AkburaSymbolInfo.Success(dependency.AkburaSymbol),
            diagnostics,
            hasErrors: hasErrors)
    {
        Dependency = dependency;
        BindingResult = bindingResult;
    }

    public new CSharpArgumentListSyntax Syntax => (CSharpArgumentListSyntax)base.Syntax;

    public UseEffectDependency Dependency { get; }

    public CSharpBindingResult BindingResult { get; }

    public BoundUseEffectDependency Update(
        UseEffectDependency dependency,
        CSharpBindingResult bindingResult)
    {
        if (dependency.Equals(Dependency) &&
            bindingResult.Equals(BindingResult))
        {
            return this;
        }

        return new BoundUseEffectDependency(
            Syntax,
            Binder,
            dependency,
            bindingResult,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitUseEffectDependency(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitUseEffectDependency(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitUseEffectDependency(this, parameter);
}

internal sealed class BoundUseEffectBody : BoundNode
{
    public BoundUseEffectBody(
        AkburaSyntax syntax,
        BinderType binder,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default,
        bool hasErrors = false)
        : base(
            BoundKind.UseEffectBody,
            syntax,
            binder,
            AkburaSymbolInfo.None(CandidateReason.None),
            diagnostics,
            children,
            hasErrors)
    {
    }

    public BoundUseEffectBody Update(ImmutableArray<BoundNode> children)
    {
        if (children == Children)
        {
            return this;
        }

        return new BoundUseEffectBody(
            Syntax,
            Binder,
            Diagnostics,
            children,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitUseEffectBody(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitUseEffectBody(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitUseEffectBody(this, parameter);
}
