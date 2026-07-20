using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Operations;

internal interface IUseHookOperation : IOperation
{
    IUseHookSymbol Hook { get; }

    IMethodSymbol Method { get; }

    ITypeSymbol ReturnType { get; }

    ImmutableArray<ITypeSymbol> TypeArguments { get; }

    IParameterSymbol? SelfParameter { get; }

    UseHookSelfKind SelfKind { get; }

    CSharp.InvocationExpressionSyntax OriginalInvocation { get; }

    CSharp.InvocationExpressionSyntax EffectiveInvocation { get; }

    bool HasSyntheticSelf { get; }

    bool HasPropertyArgumentSubstitution { get; }

    ICSharpOperation? InvocationOperation { get; }
}
