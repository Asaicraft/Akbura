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

        Assert.Equal("state", syntax.StateKeyword.ToString());
        Assert.Equal("int", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken.ToString());
        Assert.Equal("11", syntax.Initializer.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ImplicitTypeStateDeclaration_ParseSuccessfully()
    {
        const string code = "state b = 100;";

        var parser = MakeParser(code);

        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("state", syntax.StateKeyword.ToString());
        Assert.Null(syntax.Type);
        Assert.Equal("b", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken.ToString());
        Assert.Equal("100", syntax.Initializer.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void GenericTypeStateDeclaration_ParseSuccessfully()
    {
        const string code = "state List<int> items = new();";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("state", syntax.StateKeyword.ToString());
        Assert.Equal("List<int>", syntax.Type?.ToString());
        Assert.Equal("items", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken.ToString());
        Assert.Equal("new()", syntax.Initializer.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

}
