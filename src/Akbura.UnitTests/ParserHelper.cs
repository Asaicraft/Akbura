using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;

namespace Akbura.UnitTests;

internal class ParserHelper
{
    internal static Parser MakeParser(string code)
    {
        var sourceText = SourceText.From(code);
        var lexer = new Lexer(sourceText);

        return new Parser(lexer, default);
    }

    internal static Parser MakeIncrementalParser(
        string code,
        AkburaDocumentSyntax oldTree,
        IEnumerable<TextChangeRange>? changes)
    {
        var sourceText = SourceText.From(code);
        var lexer = new Lexer(sourceText);

        return new Parser(lexer, default, oldTree, changes);
    }
}
