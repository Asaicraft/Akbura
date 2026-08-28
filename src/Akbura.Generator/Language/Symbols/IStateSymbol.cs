using Akbura.Language.Syntax;

namespace Akbura.Language.Symbols;

internal interface IStateSymbol : ISymbol
{
    StateDeclarationSyntax DeclarationSyntax { get; }

    StateInitializerSyntax InitializerSyntax { get; }

    CSharpExpressionSyntax InitializerExpression { get; }

    CSharpSymbolDefinition Type { get; }

    CSharpSymbolDefinition InitializerType { get; }

    IUseHookSymbol? UseHook { get; }

    bool HasExplicitType { get; }

    bool IsBindable { get; }

    bool IsReadOnly { get; }

    StateBindingKind BindingKind { get; }
}
