using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class UsingAndNamespaceSyntaxParseTests
{
    [Theory]
    [InlineData("using System;", false, false, false, false, "System")]
    [InlineData("using System.Text;", false, false, false, false, "System.Text")]
    [InlineData("using static System.Math;", false, true, false, false, "System.Math")]
    [InlineData("using Alias = My.Namespace.Type;", false, false, false, true, "My.Namespace.Type")]
    [InlineData("using unsafe Alias = int*;", false, false, true, true, "int*")]
    [InlineData("global using System;", true, false, false, false, "System")]
    [InlineData("global using static System.Math;", true, true, false, false, "System.Math")]
    [InlineData("global using Alias = My.Namespace.Type;", true, false, false, true, "My.Namespace.Type")]
    [InlineData("global using unsafe Alias = int*;", true, false, true, true, "int*")]
    public void UsingDirective_ParseSuccessfully(
        string code,
        bool hasGlobal,
        bool hasStatic,
        bool hasUnsafe,
        bool hasAlias,
        string nameText)
    {
        var parser = MakeParser(code);

        var syntax = parser.ParseUsingDirectiveSyntax();

        Assert.Equal(hasGlobal, syntax.GlobalKeyword is not null);
        Assert.Equal(hasStatic, syntax.StaticKeyword is not null);
        Assert.Equal(hasUnsafe, syntax.UnsafeKeyword is not null);
        Assert.Equal(hasAlias, syntax.Alias is not null);
        Assert.Equal(nameText, syntax.Name.ToFullString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void UsingDirective_Alias_ParseSuccessfully()
    {
        const string code = "using Alias = My.Namespace.Type;";

        var parser = MakeParser(code);

        var syntax = parser.ParseUsingDirectiveSyntax();

        var alias = Assert.IsType<GreenUsingAliasSyntax>(syntax.Alias);
        Assert.Equal("Alias", alias.Name.Identifier.ValueText);
        Assert.Equal(SyntaxKind.EqualsToken, alias.EqualsToken.Kind);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Theory]
    [InlineData("using ;")]
    [InlineData("using Alias = ;")]
    [InlineData("global using ;")]
    public void UsingDirective_InvalidTarget_ProducesMissingType(string code)
    {
        var parser = MakeParser(code);

        var syntax = parser.ParseUsingDirectiveSyntax();

        Assert.True(syntax.Name.Tokens.Count > 0);
        Assert.True(syntax.Name.Tokens[0]!.IsMissing);
        Assert.False(syntax.Semicolon.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void NamespaceDeclaration_ParseSuccessfully()
    {
        const string code = "namespace My.App;";

        var parser = MakeParser(code);

        var syntax = parser.ParseNamespaceDeclarationSyntax();

        Assert.Equal("My.App", syntax.Name.ToFullString());
        Assert.False(syntax.Semicolon.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void NamespaceDeclaration_MissingName_ProducesMissingType()
    {
        const string code = "namespace ;";

        var parser = MakeParser(code);

        var syntax = parser.ParseNamespaceDeclarationSyntax();

        Assert.True(syntax.Name.Tokens.Count > 0);
        Assert.True(syntax.Name.Tokens[0]!.IsMissing);
        Assert.False(syntax.Semicolon.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void NamespaceDeclaration_MissingSemicolon_ProducesMissingToken()
    {
        const string code = "namespace My.App";

        var parser = MakeParser(code);

        var syntax = parser.ParseNamespaceDeclarationSyntax();

        Assert.Equal("My.App", syntax.Name.ToFullString());
        Assert.True(syntax.Semicolon.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void CompilationUnit_UsingNamespaceAndMembers_RemainFlat()
    {
        const string code =
            "using System;\n" +
            "global using static System.Math;\n" +
            "namespace My.App;\n" +
            "inject ILogger log;\n";

        var parser = MakeParser(code);

        var syntax = parser.ParseCompilationUnit();

        Assert.Equal(4, syntax.Members.Count);
        Assert.IsType<GreenUsingDirectiveSyntax>(syntax.Members[0]);
        Assert.IsType<GreenUsingDirectiveSyntax>(syntax.Members[1]);
        Assert.IsType<GreenNamespaceDeclarationSyntax>(syntax.Members[2]);
        Assert.IsType<GreenInjectDeclarationSyntax>(syntax.Members[3]);
        Assert.Equal(code, syntax.ToFullString());
    }
}
