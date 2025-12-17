using Akbura.Language;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public class StateDeclorationSyntaxParseTests
{
    private static Parser MakeParser(string code)
    {
        var sourceText = SourceText.From(code);
        var lexer = new Lexer(sourceText);

        return new Parser(lexer, default);
    }

    [Fact]
    public void SimpleStateDeclaration_ParseSuccessfully()
    {
        const string code = "state int a = 11;";

        var parser = MakeParser(code);

        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);
    }
}
