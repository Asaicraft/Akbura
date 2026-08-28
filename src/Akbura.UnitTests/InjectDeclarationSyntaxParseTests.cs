using System;
using System.Collections.Generic;
using System.Text;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class InjectDeclarationSyntaxParseTests
{
    [Fact]
    public void SimpleInjectDeclaration_ParseSuccessfully()
    {
        const string code = "inject int a;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("inject", syntax.InjectKeyword.ToString());
        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void InjectDeclaration_QualifiedGenericType_ParseSuccessfully()
    {
        const string code = "inject System.Collections.Generic.List<int> items;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("inject", syntax.InjectKeyword.ToString());
        Assert.Equal("System.Collections.Generic.List<int> ", syntax.Type?.ToString());
        Assert.Equal("items", syntax.Name.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void InjectDeclaration_VerbatimIdentifierName_ParseSuccessfully()
    {
        const string code = "inject int @class;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("inject", syntax.InjectKeyword.ToString());
        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("@class", syntax.Name.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void InjectDeclaration_PreservesSpacesBetweenTypeAndName()
    {
        const string code = "inject int   a;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        // Type.ToString() includes the trailing trivia between type and name.
        Assert.Equal("int   ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void InjectDeclaration_NewlineBetweenTypeAndName_PreservesTrivia()
    {
        const string code = "inject int\na;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("int\n", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void InjectDeclaration_MissingSemicolon_ProducesMissingToken()
    {
        const string code = "inject int a";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("inject", syntax.InjectKeyword.ToString());
        Assert.Equal("int ", syntax.Type?.ToString());
        Assert.Equal("a", syntax.Name.ToString());

        Assert.True(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void Inject_MissingType_StillBuildsNode()
    {
        // No type at all.
        const string code = "inject a;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("inject", syntax.InjectKeyword.ToString());
        Assert.False(syntax.Semicolon.IsMissing);
        Assert.Equal("a", syntax.Name.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void Inject_MissingName_StillBuildsNode()
    {
        // Type exists but name is missing.
        const string code = "inject int ;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("inject", syntax.InjectKeyword.ToString());
        Assert.Equal("int ", syntax.Type?.ToString());

        // Name should be missing because current token is ';' when expecting identifier.
        Assert.True(syntax.Name.Identifier.IsMissing);

        Assert.False(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void Inject_MissingSemicolon_StillBuildsNode()
    {
        // Missing ';'
        const string code = "inject int a";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);
        Assert.True(syntax.Semicolon.IsMissing);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void Inject_GarbageBetweenTypeAndName()
    {
        // Random garbage before name.
        const string code = "inject int < > a;";

        var parser = MakeParser(code);
        var syntax = parser.ParseInjectDeclarationSyntax();

        Assert.NotNull(syntax);
    }
}
