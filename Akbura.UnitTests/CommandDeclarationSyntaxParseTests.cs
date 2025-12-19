using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Text;

using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class CommandDeclarationSyntaxParseTests
{
    [Fact]
    public void SimpleCommandDeclaration_ParseSuccessfully()
    {
        const string code = "command int Add(int a);";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("command", syntax.CommandKeyword.ToString());
        Assert.Equal("int ", syntax.ReturnType.ToString());
        Assert.Equal("Add", syntax.Name.ToString());
        Assert.Equal("(", syntax.OpenParen.ToString());
        Assert.Equal(")", syntax.CloseParen.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void VoidCommandDeclaration_ParseSuccessfully()
    {
        const string code = "command void Log();";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("void ", syntax.ReturnType.ToString());
        Assert.Equal("Log", syntax.Name.ToString());
        Assert.Equal("(", syntax.OpenParen.ToString());
        Assert.Equal(default, syntax.Parameters);
        Assert.Equal(")", syntax.CloseParen.ToString());
        Assert.Equal(";", syntax.Semicolon.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void GenericReturnType_CommandDeclaration_ParseSuccessfully()
    {
        const string code = "command System.Collections.Generic.List<int> Get();";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("System.Collections.Generic.List<int> ", syntax.ReturnType.ToString());
        Assert.Equal("Get", syntax.Name.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void NullableReturnType_CommandDeclaration_ParseSuccessfully()
    {
        const string code = "command int? TryGet();";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("int? ", syntax.ReturnType.ToString());
        Assert.Equal("TryGet", syntax.Name.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ArrayReturnType_CommandDeclaration_ParseSuccessfully()
    {
        const string code = "command int[] GetAll();";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("int[] ", syntax.ReturnType.ToString());
        Assert.Equal("GetAll", syntax.Name.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TupleReturnType_CommandDeclaration_ParseSuccessfully()
    {
        const string code = "command (int a, string b) Pair();";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("(int a, string b) ", syntax.ReturnType.ToString());
        Assert.Equal("Pair", syntax.Name.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void CommandDeclaration_WithMultipleParameters_ParseSuccessfully()
    {
        const string code = "command int Sum(int a, int b, int c);";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("Sum", syntax.Name.ToString());
        Assert.Equal("int ", syntax.ReturnType.ToString());

        Assert.Equal("int a, int b, int c", syntax.Parameters.ToString());

        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void CommandDeclaration_PreservesTrivia_NewlineInSignature()
    {
        const string code = "command int Add(\nint a\n);\n";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.Equal("Add", syntax.Name.ToString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void CommandDeclaration_MissingSemicolon_ProducesMissingToken()
    {
        const string code = "command int Add(int a)";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.True(syntax.Semicolon.IsMissing);
    }

    [Fact]
    public void CommandDeclaration_MissingCloseParen_Recovers()
    {
        const string code = "command int Add(int a;";

        var parser = MakeParser(code);
        var syntax = parser.ParseCommandDeclarationSyntax();

        Assert.NotNull(syntax);

        Assert.True(syntax.CloseParen.IsMissing || !syntax.Semicolon.IsMissing);
    }
}
