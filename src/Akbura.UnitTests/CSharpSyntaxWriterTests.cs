using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class CSharpSyntaxWriterTests
{
    [Fact]
    public void WriteExpression_TrimsOuterWhitespaceAndIndentsEveryLine()
    {
        var expression = SyntaxFactory.ParseExpression(
            "  Invoke(\n    first,\n    second)  \r\n");
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new CSharpSyntaxWriter(codeWriter);

        writer.WriteExpression(expression);

        Assert.Equal(
            "    Invoke(\r\n" +
            "        first,\r\n" +
            "        second)",
            codeWriter.GetText().ToString());
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void WriteStatement_PreservesRelativeIndentationAndAddsOneNewLine()
    {
        var statement = SyntaxFactory.ParseStatement(
            "\r\nif (ready)\r\n{\r\n    Run();\r\n}\r\n");
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new CSharpSyntaxWriter(codeWriter);
        codeWriter.WriteLine();
        var outputStart = codeWriter.Length;

        writer.WriteStatement(statement);

        Assert.Equal(
            "    if (ready)\r\n" +
            "    {\r\n" +
            "        Run();\r\n" +
            "    }\r\n",
            codeWriter.GetText().ToString().Substring(outputStart));
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void WriteMember_WritesCompleteMemberWithOneTrailingNewLine()
    {
        var member = SyntaxFactory.ParseMemberDeclaration(
            "\nprivate void Run()\n{\n    Complete();\n}\n");
        Assert.NotNull(member);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 8,
        };
        var writer = new CSharpSyntaxWriter(codeWriter);
        codeWriter.WriteLine();
        var outputStart = codeWriter.Length;

        writer.WriteMember(member!);

        Assert.Equal(
            "        private void Run()\r\n" +
            "        {\r\n" +
            "            Complete();\r\n" +
            "        }\r\n",
            codeWriter.GetText().ToString().Substring(outputStart));
        Assert.Equal(8, codeWriter.CurrentIndent);
    }

    [Fact]
    public void WriteNormalizedNode_AddsRequiredTokenWhitespace()
    {
        var directive = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName("Avalonia.Controls"));

        using var codeWriter = new CodeWriter("\r\n");

        var writer = new CSharpSyntaxWriter(codeWriter);

        writer.WriteNormalizedNode(directive);

        Assert.Equal(
            "using Avalonia.Controls;\r\n",
            codeWriter.GetText().ToString());
    }
}
