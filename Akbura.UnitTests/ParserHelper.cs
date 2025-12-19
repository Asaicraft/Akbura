using Akbura.Language;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.UnitTests;

internal class ParserHelper
{
    internal static Parser MakeParser(string code)
    {
        var sourceText = SourceText.From(code);
        var lexer = new Lexer(sourceText);

        return new Parser(lexer, default);
    }
}
