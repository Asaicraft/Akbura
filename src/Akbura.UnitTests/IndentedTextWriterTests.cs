using Akbura.Language.CodeGeneration;

namespace Akbura.UnitTests;

public sealed class IndentedTextWriterTests
{
    [Fact]
    public void Constructor_RejectsNullWriter()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            static () => new IndentedTextWriter(null!));

        Assert.Equal("writer", exception.ParamName);
    }

    [Fact]
    public void Write_IndentsInitialAndSubsequentLinesOnce()
    {
        using var innerWriter = new StringWriter
        {
            NewLine = "\r\n",
        };
        using var writer = new IndentedTextWriter(innerWriter, "--")
        {
            Indent = 2,
        };

        writer.Write("first");
        writer.Write(" second");
        writer.WriteLine();
        writer.WriteLine("third");

        Assert.Equal(
            "----first second\r\n----third\r\n",
            innerWriter.ToString());
    }

    [Fact]
    public void Indent_ClampsNegativeValuesToZero()
    {
        using var innerWriter = new StringWriter
        {
            NewLine = "\n",
        };
        using var writer = new IndentedTextWriter(innerWriter)
        {
            Indent = -1,
        };

        writer.WriteLine("text");

        Assert.Equal(0, writer.Indent);
        Assert.Equal("text\n", innerWriter.ToString());
    }

    [Fact]
    public void WriteLineNoTabs_BypassesIndentWithoutClearingPendingIndent()
    {
        using var innerWriter = new StringWriter
        {
            NewLine = "\n",
        };
        using var writer = new IndentedTextWriter(innerWriter, ">")
        {
            Indent = 1,
        };

        writer.WriteLineNoTabs("raw");
        writer.WriteLine("indented");

        Assert.Equal("raw\n>indented\n", innerWriter.ToString());
    }

    [Fact]
    public void WriterProperties_ForwardToInnerWriter()
    {
        using var innerWriter = new StringWriter();
        using var writer = new IndentedTextWriter(innerWriter);

        writer.NewLine = "\r\n";

        Assert.Same(innerWriter, writer.InnerWriter);
        Assert.Same(innerWriter.Encoding, writer.Encoding);
        Assert.Equal("\r\n", innerWriter.NewLine);
        Assert.Equal("\r\n", writer.NewLine);
    }

    [Fact]
    public async Task WriteAsync_UsesTheSameIndentationSemantics()
    {
        using var innerWriter = new StringWriter
        {
            NewLine = "\n",
        };
        using var writer = new IndentedTextWriter(innerWriter, " ")
        {
            Indent = 2,
        };

        await writer.WriteAsync("first");
        await writer.WriteAsync('!');
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("second");

        Assert.Equal("  first!\n  second\n", innerWriter.ToString());
    }
}
