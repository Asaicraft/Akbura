using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IUseEffectSymbol : ISymbol
{
    UseEffectDeclarationSyntax DeclarationSyntax { get; }

    CSharpArgumentListSyntax ArgumentsSyntax { get; }

    ImmutableArray<UseEffectDependency> Dependencies { get; }

    CSharpBlockSyntax Body { get; }

    EffectCancelBlockSyntax? CancelBlock { get; }

    EffectFinallyBlockSyntax? FinallyBlock { get; }

    bool HasCancelBlock { get; }

    bool HasFinallyBlock { get; }
}
