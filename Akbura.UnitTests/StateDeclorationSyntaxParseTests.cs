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
        Assert.Equal("int ", syntax.Type?.ToString());
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
        Assert.Equal("List<int> ", syntax.Type?.ToString());
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

        Assert.Equal("System.Collections.Generic.List<string> ", syntax.Type?.ToString());
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

        Assert.Equal("int? ", syntax.Type?.ToString());
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

        Assert.Equal("int[] ", syntax.Type?.ToString());
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

        Assert.Equal("(int a, string b) ", syntax.Type?.ToString());
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

        Assert.Equal("MyType ", syntax.Type?.ToString());
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

        Assert.Equal("int   ", syntax.Type?.ToString());
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

        Assert.Equal("int ", syntax.Type?.ToString());
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

        Assert.Equal("int/*c*/", syntax.Type?.ToString());
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

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("@class", syntax.Name.ToString());
        Assert.Equal("1", syntax.Initializer.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void MissingSemicolon_StillBuildsNode_WithMissingToken()
    {
        // If your parser follows Roslyn-style recovery, this should still produce a node
        // and the semicolon token should be missing.
        const string code = "state int a = 11";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal("11", syntax.Initializer.ToString());

        // This assumes your MissingToken prints empty string or something stable.
        // If your missing token ToString() differs, adjust this assertion accordingly.
        Assert.True(syntax.Semicolon.IsMissing);
    }

    [Fact]
    public void MissingEquals_ParsesAndRecoversToSemicolon()
    {
        const string code = "state int a 11;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());

        // '=' should be missing.
        Assert.True(syntax.EqualsToken.IsMissing);

        // Initializer will likely become "11" if you still parse until ';',
        // or it might be missing depending on your strategy. At least don't crash.
        Assert.False(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void MissingInitializer_ParsesWithMissingExpression()
    {
        const string code = "state int a = ;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken.ToString());
        Assert.True(syntax.Initializer.Expression.IsMissing);

        // Depending on your implementation you might produce empty expression, "default", or missing token.
        // The key requirement: the node exists and semicolon is present.
        Assert.False(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void GarbageAfterInitializer_IsSkippedAndStillTerminates()
    {
        const string code = "state int a = 11 !!! ;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.False(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void BadType_FallsBackToImplicitType()
    {
        // Type parsing should fail and the parser should treat it as implicit type state declaration.
        const string code = "state < > a = 1;";

        var parser = MakeParser(code);
        var syntax = parser.ParseStateDeclaration();

        Assert.NotNull(syntax);

        Assert.Null(syntax.Type);
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal("1", syntax.Initializer.ToString());
        Assert.False(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }
}
