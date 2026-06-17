using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal interface IAkcssInterceptOperation : IAkcssOperation
{
    new AkcssInterceptDirectiveSyntax Syntax { get; }

    CSharpSymbolDefinition InterceptType { get; }
}
