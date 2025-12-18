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


    [Fact]
    public void QualifiedTypeStateDeclaration_ParseSuccessfully()
    {
        const string code = "state System.Collections.Generic.List<string> names = new();";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("System.Collections.Generic.List<string>", syntax.Type?.ToString());
        Assert.Equal("names", syntax.Name.ToString());
        Assert.Equal("new()", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void NullableTypeStateDeclaration_ParseSuccessfully()
    {
        const string code = "state int? x = null;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int?", syntax.Type?.ToString());
        Assert.Equal("x", syntax.Name.ToString());
        Assert.Equal("null", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ArrayTypeStateDeclaration_ParseSuccessfully()
    {
        const string code = "state int[] xs = new[] { 1, 2, 3 };";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int[]", syntax.Type?.ToString());
        Assert.Equal("xs", syntax.Name.ToString());
        Assert.Equal("new[] { 1, 2, 3 }", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TupleTypeStateDeclaration_ParseSuccessfully()
    {
        const string code = "state (int a, string b) t = (1, \"x\");";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("(int a, string b)", syntax.Type?.ToString());
        Assert.Equal("t", syntax.Name.ToString());
        Assert.Equal("(1, \"x\")", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ImplicitType_WithComplexInitializer_DoesNotStopEarly()
    {
        // The initializer contains nested parentheses and braces.
        const string code = "state b = Foo(1, new[] { 2, 3 }, (4, 5));";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Null(syntax.Type);
        Assert.Equal("b", syntax.Name.ToString());
        Assert.Equal("Foo(1, new[] { 2, 3 }, (4, 5))", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ExplicitType_WithObjectInitializer_DoesNotStopEarly()
    {
        const string code = "state MyType x = new() { A = 1, B = Foo(2, 3) };";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("MyType", syntax.Type?.ToString());
        Assert.Equal("x", syntax.Name.ToString());
        Assert.Equal("new() { A = 1, B = Foo(2, 3) }", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PreservesMultipleSpaces_BetweenTokens()
    {
        // Verifies trivia preservation between type and name.
        const string code = "state int   a = 11;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int", syntax.Type?.ToString());
        Assert.Equal("int   ", syntax.Type?.ToFullString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PreservesNewlineTrivia_AfterStateKeyword()
    {
        // Verifies trivia preservation when the type is on the next line.
        const string code = "state\nint a = 11;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PreservesComments_BetweenTypeAndName()
    {
        // Ensures comment trivia isn't lost when collapsing type tokens.
        const string code = "state int/*c*/a = 11;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void VerbatimIdentifierName_ParseSuccessfully()
    {
        // '@' identifiers are valid in C#.
        const string code = "state int @class = 1;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int", syntax.Type?.ToString());
        Assert.Equal("@class", syntax.Name.ToString());
        Assert.Equal("1", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }
}
