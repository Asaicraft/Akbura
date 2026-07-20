using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Operations;

internal sealed class UseHookOperation : IUseHookOperation
{
    public UseHookOperation(
        AkburaSyntax syntax,
        IUseHookSymbol hook,
        CSharp.InvocationExpressionSyntax originalInvocation,
        CSharp.InvocationExpressionSyntax effectiveInvocation,
        CSharpOperationDefinition csharpDefinition,
        bool hasSyntheticSelf,
        bool hasPropertyArgumentSubstitution,
        bool hasErrors,
        ICSharpOperation? invocationOperation)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Hook = hook ?? throw new ArgumentNullException(nameof(hook));
        OriginalInvocation = originalInvocation ??
            throw new ArgumentNullException(nameof(originalInvocation));
        EffectiveInvocation = effectiveInvocation ??
            throw new ArgumentNullException(nameof(effectiveInvocation));
        CSharpDefinition = csharpDefinition;
        HasSyntheticSelf = hasSyntheticSelf;
        HasPropertyArgumentSubstitution = hasPropertyArgumentSubstitution;
        HasErrors = hasErrors;
        InvocationOperation = invocationOperation;
        Children = invocationOperation == null
            ? ImmutableArray<IOperation>.Empty
            : ImmutableArray.Create<IOperation>(invocationOperation);

        if (invocationOperation is CSharpOperation csharpOperation)
        {
            csharpOperation.SetParent(this);
        }
    }

    public OperationKind Kind => OperationKind.UseHook;

    public OperationLanguage Language => OperationLanguage.Akbura;

    public AkburaSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children { get; }

    public AkburaSymbol TargetSymbol => Hook;

    public AkburaSymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition { get; }

    public bool IsImplicit => HasSyntheticSelf;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    public IUseHookSymbol Hook { get; }

    public IMethodSymbol Method => Hook.Method;

    public ITypeSymbol ReturnType => Hook.ReturnType;

    public ImmutableArray<ITypeSymbol> TypeArguments => Method.TypeArguments;

    public IParameterSymbol? SelfParameter => Hook.SelfParameter;

    public UseHookSelfKind SelfKind => Hook.SelfKind;

    public CSharp.InvocationExpressionSyntax OriginalInvocation { get; }

    public CSharp.InvocationExpressionSyntax EffectiveInvocation { get; }

    public bool HasSyntheticSelf { get; }

    public bool HasPropertyArgumentSubstitution { get; }

    public ICSharpOperation? InvocationOperation { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitUseHook(this);
    }

    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitUseHook(this, parameter);
    }

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation && Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public string ToDisplayString()
    {
        return EffectiveInvocation.ToFullString().Trim();
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
