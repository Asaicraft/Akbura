using Akbura.Language.Syntax;

namespace Akbura.Language.Symbols;

internal interface IInjectSymbol : ISymbol
{
    InjectDeclarationSyntax DeclarationSyntax { get; }

    CSharpSymbolDefinition Type { get; }

    bool IsOptional { get; }

    bool IsRequired { get; }
}
