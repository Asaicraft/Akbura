using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct SourceMappingWriter
{
    private readonly CodeWriter _writer;
    private readonly ComponentGenerationSourceMap _sourceMap;

    public SourceMappingWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _sourceMap = sourceMap ?? throw new ArgumentNullException(nameof(sourceMap));
    }

    public SourceMappingToken WriteStart(AkburaSyntax syntax, int valueOffset = 0)
    {
        AkburaDebug.Assert(syntax != null);

        if (!_sourceMap.TryGetLineDirective(syntax, out var span, out var path))
        {
            return default;
        }

        return WriteStartCore(_writer, span, path, valueOffset);
    }

    public SourceMappingToken WriteStart(
        AkburaSyntax syntax,
        TextSpan relativeSpan,
        int valueOffset = 0)
    {
        AkburaDebug.Assert(syntax != null);

        if (!_sourceMap.TryGetLineDirective(syntax, relativeSpan, out var span, out var path))
        {
            return default;
        }

        return WriteStartCore(_writer, span, path, valueOffset);
    }

    internal static SourceMappingToken WriteStartCore(
        CodeWriter writer,
        LinePositionSpan span,
        string path,
        int valueOffset)
    {
        EnsureDirectiveLine(writer);

        var generatedOffset = writer.CurrentIndent + Math.Max(valueOffset, 0);
        var startLine = span.Start.Line + 1;
        var startCharacter = span.Start.Character + 1;
        var endLine = span.End.Line + 1;
        var endCharacter = span.End.Character + 1;

        writer.Write("#line (");
        writer.WriteIntegerLiteral(startLine);
        writer.Write(",");
        writer.WriteIntegerLiteral(startCharacter);
        writer.Write(")-(");
        writer.WriteIntegerLiteral(endLine);
        writer.Write(",");
        writer.WriteIntegerLiteral(endCharacter);
        writer.Write(") ");
        writer.WriteIntegerLiteral(generatedOffset);
        writer.Write(" ");
        writer.WriteStringLiteral(path);
        writer.WriteLine();

        return new SourceMappingToken(writer);
    }

    internal static void EnsureDirectiveLine(CodeWriter writer)
    {
        if (writer.LastChar is char lastCharacter && lastCharacter != '\n')
        {
            writer.WriteLine();
        }
    }
}

internal ref struct SourceMappingToken
{
    private CodeWriter? _writer;

    internal SourceMappingToken(CodeWriter writer)
    {
        _writer = writer;
    }

    public readonly bool IsMapped => _writer != null;

    public void Dispose()
    {
        var writer = _writer;
        _writer = null;

        WriteEndDirectives(writer);
    }

    private static void WriteEndDirectives(CodeWriter? writer)
    {
        if (writer == null)
        {
            return;
        }

        SourceMappingWriter.EnsureDirectiveLine(writer);
        writer.WriteLine("#line default");
        writer.WriteLine("#line hidden");
    }
}
