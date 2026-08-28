using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundUseHookInvocation : BoundExpression
{
    public BoundUseHookInvocation(
        AkburaSyntax syntax,
        BinderType binder,
        IUseHookSymbol hook,
        CSharp.InvocationExpressionSyntax originalInvocation,
        CSharp.InvocationExpressionSyntax effectiveInvocation,
        CSharpBindingResult bindingResult,
        ImmutableArray<BoundExpression> effectiveArguments,
        bool hasSyntheticSelf,
        bool hasPropertyArgumentSubstitution,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            BoundKind.UseHookInvocation,
            syntax,
            binder,
            AkburaSymbolInfo.Success(hook),
            diagnostics,
            ToChildren(effectiveArguments),
            hasErrors: bindingResult.Diagnostics.Length != 0 ||
                !diagnostics.IsDefaultOrEmpty)
    {
        Hook = hook;
        OriginalInvocation = originalInvocation;
        EffectiveInvocation = effectiveInvocation;
        BindingResult = bindingResult;
        EffectiveArguments = effectiveArguments.IsDefault
            ? ImmutableArray<BoundExpression>.Empty
            : effectiveArguments;
        HasSyntheticSelf = hasSyntheticSelf;
        HasPropertyArgumentSubstitution = hasPropertyArgumentSubstitution;
    }

    public IUseHookSymbol Hook { get; }

    public CSharp.InvocationExpressionSyntax OriginalInvocation { get; }

    public CSharp.InvocationExpressionSyntax EffectiveInvocation { get; }

    public CSharpBindingResult BindingResult { get; }

    public ImmutableArray<BoundExpression> EffectiveArguments { get; }

    public bool HasSyntheticSelf { get; }

    public bool HasPropertyArgumentSubstitution { get; }

    public ImmutableArray<ITypeSymbol> TypeArguments => Hook.Method.TypeArguments;

    public override ITypeSymbol? Type => Hook.ReturnType;

    public BoundUseHookInvocation Update(
        IUseHookSymbol hook,
        CSharpBindingResult bindingResult,
        ImmutableArray<BoundExpression> effectiveArguments)
    {
        if (ReferenceEquals(hook, Hook) &&
            bindingResult.Equals(BindingResult) &&
            effectiveArguments == EffectiveArguments)
        {
            return this;
        }

        return new BoundUseHookInvocation(
            Syntax,
            Binder,
            hook,
            OriginalInvocation,
            EffectiveInvocation,
            bindingResult,
            effectiveArguments,
            HasSyntheticSelf,
            HasPropertyArgumentSubstitution,
            Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitUseHookInvocation(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitUseHookInvocation(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitUseHookInvocation(this, parameter);
    }

    private static ImmutableArray<BoundNode> ToChildren(
        ImmutableArray<BoundExpression> arguments)
    {
        if (arguments.IsDefaultOrEmpty)
        {
            return ImmutableArray<BoundNode>.Empty;
        }

        return ImmutableArray.CreateRange<BoundNode>(arguments);
    }
}
