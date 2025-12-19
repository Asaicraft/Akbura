using System;
using System.Collections.Generic;
using System.Text;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class ParamDeclarationSyntaxParseTests
{
    [Fact]
    public void SimpleParamDeclaration_WithTypeAndDefault_ParseSuccessfully()
    {
        const string code = "param int UserId = 1;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("param", syntax.ParamKeyword.ToString());
        Assert.Null(syntax.BindingKeyword);
        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("UserId", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken?.ToString());
        Assert.Equal("1", syntax.DefaultValue?.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void SimpleParamDeclaration_ImplicitTypeWithDefault_ParseSuccessfully()
    {
        const string code = "param b = \"sad\";";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("param", syntax.ParamKeyword.ToString());
        Assert.Null(syntax.BindingKeyword);
        Assert.Null(syntax.Type);
        Assert.Equal("b", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken?.ToString());
        Assert.Equal("\"sad\"", syntax.DefaultValue?.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void OutParamDeclaration_WithTypeAndDefault_ParseSuccessfully()
    {
        const string code = "param out Hello = 10;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("param", syntax.ParamKeyword.ToString());
        Assert.Equal("out", syntax.BindingKeyword?.ToString());
        Assert.Null(syntax.Type); // "Hello" should be treated as the name, not a type
        Assert.Equal("Hello", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken?.ToString());
        Assert.Equal("10", syntax.DefaultValue?.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void BindParamDeclaration_WithTypeAndDefault_ParseSuccessfully()
    {
        const string code = "param bind int Count = 42;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("param", syntax.ParamKeyword.ToString());
        Assert.Equal("bind", syntax.BindingKeyword?.ToString());
        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("Count", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken?.ToString());
        Assert.Equal("42", syntax.DefaultValue?.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_WithTypeWithoutDefault_ParseSuccessfully()
    {
        const string code = "param int UserId;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("param", syntax.ParamKeyword.ToString());
        Assert.Null(syntax.BindingKeyword);
        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("UserId", syntax.Name.ToString());
        Assert.Null(syntax.EqualsToken);
        Assert.Null(syntax.DefaultValue);
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_ImplicitTypeWithoutDefault_ParseSuccessfully()
    {
        const string code = "param UserId;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("param", syntax.ParamKeyword.ToString());
        Assert.Null(syntax.BindingKeyword);
        Assert.Null(syntax.Type);
        Assert.Equal("UserId", syntax.Name.ToString());
        Assert.Null(syntax.EqualsToken);
        Assert.Null(syntax.DefaultValue);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_GenericTypeAndComplexDefault_ParseSuccessfully()
    {
        const string code = "param System.Collections.Generic.List<int> Items = new() { 1, 2, 3 };";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("System.Collections.Generic.List<int> ", syntax.Type?.ToString());
        Assert.Equal("Items", syntax.Name.ToString());
        Assert.Equal("new() { 1, 2, 3 }", syntax.DefaultValue?.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_ExpressionDefault_DoesNotStopEarly()
    {
        const string code = "param x = Foo(1, new[] { 2, 3 }, (4, 5));";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Null(syntax.Type);
        Assert.Equal("x", syntax.Name.ToString());
        Assert.Equal("Foo(1, new[] { 2, 3 }, (4, 5))", syntax.DefaultValue?.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_VerbatimIdentifierName_ParseSuccessfully()
    {
        const string code = "param int @class = 1;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("@class", syntax.Name.ToString());
        Assert.Equal("1", syntax.DefaultValue?.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_PreservesSpacesBetweenTypeAndName()
    {
        const string code = "param int   a = 1;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("int   ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_MissingSemicolon_ProducesMissingToken()
    {
        const string code = "param int a = 1";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal("1", syntax.DefaultValue?.ToString());

        Assert.True(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ParamDeclaration_MissingDefaultValue_DoesNotCrash()
    {
        // Equals exists but expression is empty.
        const string code = "param int a = ;";

        var parser = MakeParser(code);
        var syntax = parser.ParseParamDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal("=", syntax.EqualsToken?.ToString());

        Assert.False(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }
}
