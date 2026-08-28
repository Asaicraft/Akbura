using Akbura.Language.Syntax;

namespace Akbura.Language.Symbols;

internal interface IParamSymbol : ISymbol
{
    ParamDeclarationSyntax DeclarationSyntax { get; }

    ParamBindingKind BindingKind { get; }

    CSharpSymbolDefinition Type { get; }

    CSharpSymbolDefinition DefaultValueType { get; }

    bool HasExplicitType { get; }

    bool HasDefaultValue { get; }

    CSharpExpressionSyntax? DefaultValueSyntax { get; }

    bool ReceivesValueFromParent { get; }

    bool SendsValueToParent { get; }

    bool IsTwoWayBinding { get; }
}
