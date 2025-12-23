using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Text;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class UseEffectDeclarationSyntaxParseTest
{
    [Fact]
    public void UseEffect_NoTails_ParseSuccessfully()
    {
        const string code = "useEffect(a, b) { }";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("useEffect", syntax.UseEffectKeyword.ToString());
        Assert.Equal("(a, b) ", syntax.Arguments.ToString());
        Assert.Equal("{ }", syntax.Body.ToString());

        Assert.Equal(default, syntax.Tails);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void UseEffect_CancelTail_ParseSuccessfully()
    {
        const string code = "useEffect() { } cancel { }";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("useEffect", syntax.UseEffectKeyword.ToString());
        Assert.Equal("() ", syntax.Arguments.ToString());
        Assert.Equal("{ }", syntax.Body.ToString());

        Assert.Equal(1, syntax.Tails.Count);
        Assert.Equal("cancel { }", syntax.Tails[0]!.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void UseEffect_FinallyTail_ParseSuccessfully()
    {
        const string code = "useEffect() { } finally { }";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("useEffect", syntax.UseEffectKeyword.ToString());
        Assert.Equal("() ", syntax.Arguments.ToString());

        Assert.Equal(1, syntax.Tails.Count);
        Assert.Equal("finally { }", syntax.Tails[0]!.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void UseEffect_CancelThenFinally_ParseSuccessfully()
    {
        const string code = "useEffect(a) { } cancel { } finally { }";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("(a) ", syntax.Arguments.ToString());

        Assert.Equal(2, syntax.Tails.Count);
        Assert.Equal("cancel { }", syntax.Tails[0]!.ToString());
        Assert.Equal("finally { }", syntax.Tails[1]!.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void UseEffect_FinallyThenCancel_ParseSuccessfully()
    {
        // Your parser allows two tails in any order: it checks "cancel or finally" twice.
        const string code = "useEffect(a) { } finally { } cancel { }";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal(2, syntax.Tails.Count);
        Assert.Equal("finally { }", syntax.Tails[0]!.ToString());
        Assert.Equal("cancel { }", syntax.Tails[1]!.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void UseEffect_ThirdTail_IsNotConsumed()
    {
        // Parser only consumes up to 2 tails; the 3rd should remain for the caller.
        const string code = "useEffect() { } cancel { } finally { } cancel { }";

        var parser = MakeParser(code);

        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);
        Assert.Equal(2, syntax.Tails.Count);
    }

    [Fact]
    public void UseEffect_PreservesTrivia_Newlines()
    {
        const string code = "useEffect(\n)\n{\n}\ncancel\n{\n}\n";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void UseEffect_MissingCloseParen_DoesNotCrash()
    {
        // Bad argument list; lexer should still return a CSharpRawToken,
        // and parser must not crash.
        const string code = "useEffect( { }";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        Assert.NotNull(syntax);
        Assert.Equal("useEffect", syntax.UseEffectKeyword.ToString());
    }

    [Fact]
    public void UseEffect_MissingMainBlock_DoesNotCrash()
    {
        // No block after args; parser will try ParseCSharpBlock and should produce missing tokens/recover.
        const string code = "useEffect()";

        var parser = MakeParser(code);
        var syntax = parser.ParseUseEffectDeclarationSyntax();

        var body = syntax.Body;

        Assert.NotNull(body);

        Assert.True(body.OpenBrace.IsMissing);
        Assert.True(body.Tokens == default);
        Assert.True(body.CloseBrace.IsMissing);

        Assert.NotNull(syntax);
    }
}
