using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal readonly struct AkcssAmxInvocationValue
{
    public AkcssAmxInvocationValue(
        AkcssAmxInvocationKind kind,
        CSharpSymbolDefinition typeArgument,
        ImmutableArray<ExpressionSyntax> arguments,
        IMethodSymbol? methodSymbol,
        CSharpOperationDefinition key = default)
    {
        Kind = kind;
        TypeArgument = typeArgument;
        Arguments = arguments.IsDefault ? ImmutableArray<ExpressionSyntax>.Empty : arguments;
        MethodSymbol = methodSymbol;
        Key = key;
    }

    public AkcssAmxInvocationKind Kind { get; }

    public CSharpSymbolDefinition TypeArgument { get; }

    public ImmutableArray<ExpressionSyntax> Arguments { get; }

    public IMethodSymbol? MethodSymbol { get; }

    public CSharpOperationDefinition Key { get; }
}
