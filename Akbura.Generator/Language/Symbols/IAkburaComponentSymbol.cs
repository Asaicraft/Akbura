using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IAkburaComponentSymbol : IMarkupComponentSymbol
{
    AkburaSyntaxTree SyntaxTree { get; }

    AkburaDocumentSyntax DeclarationSyntax { get; }

    string NamespaceName { get; }

    ImmutableArray<INamedTypeSymbol> PartialTypes { get; }

    ImmutableArray<IMarkupComponentSymbol> MarkupRoots { get; }

    ImmutableArray<IStateSymbol> States { get; }

    ImmutableArray<IParamSymbol> Parameters { get; }

    ImmutableArray<IInjectSymbol> InjectedServices { get; }

    ImmutableArray<ICommandSymbol> Commands { get; }

    ImmutableArray<IUseEffectSymbol> UseEffects { get; }

    ImmutableArray<UserHookSyntax> UserHooks { get; }

    ImmutableArray<IAkcssModuleSymbol> AkcssModules { get; }
}
