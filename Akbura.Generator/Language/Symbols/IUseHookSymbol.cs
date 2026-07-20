using Microsoft.CodeAnalysis;

namespace Akbura.Language.Symbols;

internal enum UseHookSelfKind : byte
{
    None = 0,
    Explicit,
    Implicit,
}

internal interface IUseHookSymbol : ISymbol
{
    string InvocationName { get; }

    IMethodSymbol Method { get; }

    ITypeSymbol ReturnType { get; }

    IParameterSymbol? SelfParameter { get; }

    UseHookSelfKind SelfKind { get; }

    bool HasSelfParameter { get; }

    bool IsSelfImplicit { get; }
}
