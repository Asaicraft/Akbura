using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundCallExpression : BoundExpression
{
    public BoundCallExpression(
        AkburaSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        IMethodSymbol? targetMethod,
        BoundExpression? receiver,
        ImmutableArray<BoundExpression> arguments,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            BoundKind.CallExpression,
            syntax,
            binder,
            AkburaSymbolInfo.None(bindingResult.CandidateReason),
            operation: null,
            diagnostics,
            BuildChildren(receiver, arguments),
            hasErrors: bindingResult.Diagnostics.Length != 0)
    {
        BindingResult = bindingResult;
        TargetMethod = targetMethod;
        Receiver = receiver;
        Arguments = arguments.IsDefault
            ? ImmutableArray<BoundExpression>.Empty
            : arguments;
    }

    public CSharpBindingResult BindingResult { get; }

    public IMethodSymbol? TargetMethod { get; }

    public BoundExpression? Receiver { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }

    public ImmutableArray<Diagnostic> RoslynDiagnostics => BindingResult.Diagnostics;

    public override ITypeSymbol? Type => BindingResult.TypeSymbol;

    public override bool IsError => RoslynDiagnostics.Length != 0 || base.IsError;

    public BoundExpression Update(
        CSharpBindingResult bindingResult,
        IMethodSymbol? targetMethod,
        BoundExpression? receiver,
        ImmutableArray<BoundExpression> arguments)
    {
        if (bindingResult.Equals(BindingResult) &&
            SymbolEqualityComparer.Default.Equals(targetMethod, TargetMethod) &&
            ReferenceEquals(receiver, Receiver) &&
            arguments == Arguments)
        {
            return this;
        }

        return new BoundCallExpression(
            Syntax,
            Binder,
            bindingResult,
            targetMethod,
            receiver,
            arguments,
            Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitCallExpression(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitCallExpression(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitCallExpression(this, parameter);
    }

    private static ImmutableArray<BoundNode> BuildChildren(
        BoundExpression? receiver,
        ImmutableArray<BoundExpression> arguments)
    {
        if (receiver == null &&
            (arguments.IsDefaultOrEmpty || arguments.Length == 0))
        {
            return ImmutableArray<BoundNode>.Empty;
        }

        var builder = ArrayBuilder<BoundNode>.GetInstance(
            (receiver == null ? 0 : 1) +
            (arguments.IsDefault ? 0 : arguments.Length));

        if (receiver != null)
        {
            builder.Add(receiver);
        }

        if (!arguments.IsDefaultOrEmpty)
        {
            foreach (var argument in arguments)
            {
                builder.Add(argument);
            }
        }

        return builder.ToImmutableAndFree();
    }
}
