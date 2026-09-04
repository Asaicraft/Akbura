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

    public SourceMappingToken WriteStart(
        AkburaSyntax syntax,
        int valueOffset = 0)
    {
        if (!_sourceMap.TryGetLineDirective(
                syntax,
                out var span,
                out var path))
        {
            return default;
        }

        return WriteStartCore(span, path, valueOffset);
    }

    public SourceMappingToken WriteStart(
        AkburaSyntax syntax,
        TextSpan relativeSpan,
        int valueOffset = 0)
    {
        if (!_sourceMap.TryGetLineDirective(
                syntax,
                relativeSpan,
                out var span,
                out var path))
        {
            return default;
        }

        return WriteStartCore(span, path, valueOffset);
    }

    private SourceMappingToken WriteStartCore(
        LinePositionSpan span,
        string path,
        int valueOffset)
    {
        EnsureDirectiveLine(_writer);

        var generatedOffset = _writer.CurrentIndent + Math.Max(0, valueOffset);

        _writer
            .Write("#line (")
            .WriteIntegerLiteral(span.Start.Line + 1)
            .Write(",")
            .WriteIntegerLiteral(span.Start.Character + 1)
            .Write(")-(")
            .WriteIntegerLiteral(span.End.Line + 1)
            .Write(",")
            .WriteIntegerLiteral(span.End.Character + 1)
            .Write(") ")
            .WriteIntegerLiteral(generatedOffset)
            .Write(" \"")
            .Write(path)
            .WriteLine("\"");

        return new SourceMappingToken(_writer);
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
