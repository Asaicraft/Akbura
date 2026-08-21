using System.Collections.Generic;
using System.Globalization;
using Akbura.Language.CodeGeneration;

namespace Akbura.UnitTests;

public sealed class CodeWriterTests
{
    [Fact]
    public void Write_TracksMixedLineEndings()
    {
        using var writer = new CodeWriter("\n");

        writer.Write("1234\r123\r\n12\n1");

        AssertPosition(writer.Location, absoluteIndex: 14, lineIndex: 3, characterIndex: 1);
        Assert.Equal(14, writer.Length);
        Assert.Equal('1', writer.LastChar);
    }

    [Fact]
    public void Write_DoesNotDoubleCountCrLfSplitAcrossWrites()
    {
        using var writer = new CodeWriter("\n");

        writer.Write("1234\r");
        AssertPosition(writer.Location, absoluteIndex: 5, lineIndex: 1, characterIndex: 0);

        writer.Write("\n");
        AssertPosition(writer.Location, absoluteIndex: 6, lineIndex: 1, characterIndex: 0);
    }

    [Theory]
    [InlineData(false, 6, "      ")]
    [InlineData(true, 6, "\t  ")]
    public void Write_IndentsLinesUsingConfiguredStyle(
        bool indentWithTabs,
        int indent,
        string expectedIndent)
    {
        using var writer = new CodeWriter(
            newLine: "\n",
            indentWithTabs: indentWithTabs,
            tabSize: 4)
        {
            CurrentIndent = indent,
        };

        writer.Write("header");
        writer.WriteLine();
        writer.Write("body");

        Assert.Equal($"header\n{expectedIndent}body", writer.GetText().ToString());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Constructor_AcceptsSupportedNewLines(string newLine)
    {
        using var writer = new CodeWriter(newLine);

        writer.WriteLine("text");

        Assert.Equal("text" + newLine, writer.GetText().ToString());
        Assert.Equal(newLine, writer.NewLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r")]
    [InlineData("text")]
    public void Constructor_RejectsUnsupportedNewLines(string newLine)
    {
        Assert.Throws<ArgumentException>(() => new CodeWriter(newLine));
    }

    [Fact]
    public void Constructor_RejectsNullNewLine()
    {
        Assert.Throws<ArgumentNullException>(() => new CodeWriter(null!));
    }

    [Fact]
    public void NewLineSetter_UsesTheSameValidationAsConstructor()
    {
        using var writer = new CodeWriter("\n");

        writer.NewLine = "\r\n";
        Assert.Equal("\r\n", writer.NewLine);

        Assert.Throws<ArgumentException>(() => writer.NewLine = "\r");
        Assert.Throws<ArgumentNullException>(() => writer.NewLine = null!);
    }

    [Fact]
    public void GetText_RoundTripsChunksAndUpdatesLengthAndLastChar()
    {
        using var writer = new CodeWriter("\n");

        writer.Write("ab");
        writer.Write("cdef".AsMemory(1, 2));

        Assert.Equal("abde", writer.GetText().ToString());
        Assert.Equal(4, writer.Length);
        Assert.Equal('e', writer.LastChar);
    }

    [Fact]
    public void TestReader_ReadsAcrossChunkAndPageBoundaries()
    {
        var pages = new LinkedList<ReadOnlyMemory<char>[]>();
        pages.AddLast(new[]
        {
            "ab".AsMemory(),
            "cde".AsMemory(),
        });
        pages.AddLast(new[]
        {
            "fg".AsMemory(),
        });

        using var reader = CodeWriter.GetTestTextReader(pages);
        var buffer = new char[4];

        Assert.Equal('a', reader.Peek());
        Assert.Equal(4, reader.Read(buffer, 0, buffer.Length));
        Assert.Equal("abcd", new string(buffer));
        Assert.Equal('e', reader.Peek());

        Array.Clear(buffer, 0, buffer.Length);
        Assert.Equal(3, reader.Read(buffer, 0, buffer.Length));
        Assert.Equal("efg\0", new string(buffer));
        Assert.Equal(-1, reader.Peek());
        Assert.Equal(-1, reader.Read());
    }

    [Fact]
    public void InterpolatedStringHandler_WritesSupportedValuesWithoutFormattingNull()
    {
        using var writer = new CodeWriter("\n");
        string? missing = null;
        ReadOnlyMemory<char> memory = "memory".AsMemory();
        IWriteableValue writeable = new TestWriteableValue("writeable");

        writer.Write($"before:{missing}:{memory}:{writeable}:after");

        Assert.Equal(
            "before::memory:<writeable>:after",
            writer.GetText().ToString());
    }

    [Fact]
    public void InterpolatedStringHandler_WriteLineAppendsConfiguredNewLine()
    {
        using var writer = new CodeWriter("\n");
        var value = "value";

        writer.WriteLine($"<{value}>");

        Assert.Equal("<value>\n", writer.GetText().ToString());
    }

    public static TheoryData<int> IntegerLiterals => new()
    {
        0,
        1,
        -1,
        999,
        -999,
        1000,
        -1000,
        1_000_001,
        -1_000_010,
        int.MinValue,
        int.MaxValue,
    };

    [Theory]
    [MemberData(nameof(IntegerLiterals))]
    public void WriteIntegerLiteral_WritesInvariantRepresentation(int value)
    {
        using var writer = new CodeWriter("\n");

        writer.WriteIntegerLiteral(value);

        Assert.Equal(
            value.ToString(CultureInfo.InvariantCulture),
            writer.GetText().ToString());
    }

    [Fact]
    public void WriteStringLiteral_EscapesCSharpSpecialCharacters()
    {
        using var writer = new CodeWriter("\n");

        writer.WriteStringLiteral("\r\n\t\"'\\\0\u2028\u2029");

        Assert.Equal(
            "\"\\r\\n\\t\\\"\\'\\\\\\0\\u2028\\u2029\"",
            writer.GetText().ToString());
    }

    [Fact]
    public void WriteStringLiteral_UsesVerbatimLiteralForLongTextAndDoublesQuotes()
    {
        using var writer = new CodeWriter("\n");
        var literal = new string('a', 255) + "\"\n";

        writer.WriteStringLiteral(literal);

        Assert.Equal(
            "@\"" + new string('a', 255) + "\"\"\n\"",
            writer.GetText().ToString());
    }

    [Theory]
    [InlineData("text", "\"text\"u8")]
    [InlineData("line\n", "\"line\\n\"u8")]
    public void WriteStringLiteral_AppendsUtf8Suffix(string literal, string expected)
    {
        using var writer = new CodeWriter("\n");

        writer.WriteStringLiteral(literal, utf8: true);

        Assert.Equal(expected, writer.GetText().ToString());
    }

    [Fact]
    public void BuildScope_RestoresIndentAndWritesBalancedBraces()
    {
        using var writer = new CodeWriter(
            newLine: "\n",
            indentWithTabs: false,
            tabSize: 4)
        {
            CurrentIndent = 2,
        };

        writer.Write("if (true)");
        using (writer.BuildScope())
        {
            Assert.Equal(6, writer.CurrentIndent);
            writer.WriteLine("Do();");
        }

        Assert.Equal(2, writer.CurrentIndent);
        Assert.Equal(
            "if (true) {\n      Do();\n  }\n",
            writer.GetText().ToString());
    }

    private static void AssertPosition(
        CodeWriterPosition position,
        int absoluteIndex,
        int lineIndex,
        int characterIndex)
    {
        Assert.Equal(absoluteIndex, position.AbsoluteIndex);
        Assert.Equal(lineIndex, position.LineIndex);
        Assert.Equal(characterIndex, position.CharacterIndex);
    }

    private sealed class TestWriteableValue(string value) : IWriteableValue
    {
        void IWriteableValue.WriteTo(CodeWriter writer)
        {
            writer.Write("<");
            writer.Write(value);
            writer.Write(">");
        }
    }
}
