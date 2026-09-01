using Akbura.Language;
using Akbura.Language.CodeGeneration;

namespace Akbura.UnitTests;

public sealed class SourceMappingWriterTests
{
    [Fact]
    public void WriteStartAndDispose_WriteEnhancedDirectiveAndHideGeneratedCode()
    {
        var syntaxTree = ComponentSyntaxTree.ParseText(
            "<Button />",
            "Views/Main.akbura");
        var syntax = syntaxTree.GetRoot();

        using var writer = new CodeWriter()
        {
            CurrentIndent = 8,
        };

        writer.WriteLine("before");

        var sourceMappingWriter = new SourceMappingWriter(
            writer,
            new ComponentGenerationSourceMap(syntaxTree));

        {
            using var token = sourceMappingWriter.WriteStart(
                syntax,
                valueOffset: 3);

            Assert.True(token.IsMapped);
            writer.WriteLine("mapped();");
        }

        Assert.Equal(
            "before\r\n" +
            "        #line (1,1)-(1,11) 11 \"Views/Main.akbura\"\r\n" +
            "        mapped();\r\n" +
            "        #line default\r\n" +
            "        #line hidden\r\n",
            writer.GetText().ToString());
    }

    [Fact]
    public void Dispose_PutsDirectivesOnTheirOwnLines()
    {
        var syntaxTree = ComponentSyntaxTree.ParseText(
            "<Button />",
            "Main.akbura");
        using var writer = new CodeWriter();
        var sourceMappingWriter = new SourceMappingWriter(
            writer,
            new ComponentGenerationSourceMap(syntaxTree));

        writer.Write("return ");

        var token = sourceMappingWriter.WriteStart(syntaxTree.GetRoot());
        writer.Write("mapped();");
        token.Dispose();

        Assert.Equal(
            "return \r\n" +
            "#line (1,1)-(1,11) 0 \"Main.akbura\"\r\n" +
            "mapped();\r\n" +
            "#line default\r\n" +
            "#line hidden\r\n",
            writer.GetText().ToString());
    }

    [Fact]
    public void Dispose_WhenCalledRepeatedly_WritesEndDirectivesOnce()
    {
        var syntaxTree = ComponentSyntaxTree.ParseText(
            "<Button />",
            "Main.akbura");
        using var writer = new CodeWriter();
        var sourceMappingWriter = new SourceMappingWriter(
            writer,
            new ComponentGenerationSourceMap(syntaxTree));

        var token = sourceMappingWriter.WriteStart(syntaxTree.GetRoot());
        writer.Write("mapped();");

        token.Dispose();
        token.Dispose();

        Assert.False(token.IsMapped);
        Assert.Equal(
            "#line (1,1)-(1,11) 0 \"Main.akbura\"\r\n" +
            "mapped();\r\n" +
            "#line default\r\n" +
            "#line hidden\r\n",
            writer.GetText().ToString());
    }

    [Fact]
    public void InvalidSourcePath_ProducesNoMappingDirectives()
    {
        var syntaxTree = ComponentSyntaxTree.ParseText(
            "<Button />",
            "Invalid\"Path.akbura");

        using var writer = new CodeWriter();
        var sourceMappingWriter = new SourceMappingWriter(
            writer,
            new ComponentGenerationSourceMap(syntaxTree));

        var token = sourceMappingWriter.WriteStart(syntaxTree.GetRoot());
        writer.Write("mapped();");
        token.Dispose();
        token.Dispose();

        Assert.False(token.IsMapped);
        Assert.Equal("mapped();", writer.GetText().ToString());
    }
}
