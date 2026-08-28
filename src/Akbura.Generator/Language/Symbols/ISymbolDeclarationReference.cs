using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Threading;

namespace Akbura.Language.Symbols;

internal interface ISymbolDeclarationReference
{
    TextSpan Span { get; }

    AkburaSyntax GetSyntax(CancellationToken cancellationToken = default);
}
