using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Text;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class MarkupComponentNameSyntaxParseTests
{
    [Fact]
    public void SimpleName_ParseSuccessfully()
    {
        const string code = "Button";

        var parser = MakeParser(code);
        var syntax = parser.ParseMarkupComponentNameSyntax();

        Assert.NotNull(syntax);

        Assert.IsType<GreenMarkupSimpleComponentNameSyntax>(syntax);
        Assert.Equal("Button", syntax.ToString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void QualifiedName_TwoSegments_ParseSuccessfully()
    {
        const string code = "Namespace.Component";

        var parser = MakeParser(code);
        var syntax = parser.ParseMarkupComponentNameSyntax();

        Assert.NotNull(syntax);

        Assert.IsType<GreenMarkupQualifiedComponentNameSyntax>(syntax);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void QualifiedName_MultiSegments_ParseSuccessfully()
    {
        const string code = "Namespace.Sub.Component";

        var parser = MakeParser(code);
        var syntax = parser.ParseMarkupComponentNameSyntax();

        Assert.NotNull(syntax);

        Assert.IsType<GreenMarkupQualifiedComponentNameSyntax>(syntax);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void QualifiedName_WithGenericsOnLastSegment_ParseSuccessfully()
    {
        const string code = "Namespace.Component{int}";

        var parser = MakeParser(code);
        var syntax = parser.ParseMarkupComponentNameSyntax();

        Assert.NotNull(syntax);

        Assert.IsType<GreenMarkupQualifiedComponentNameSyntax>(syntax);
        Assert.Equal(code, syntax.ToFullString());
    }
}
