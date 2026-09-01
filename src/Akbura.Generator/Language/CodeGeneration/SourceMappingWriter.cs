using Akbura.Language.Syntax;
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

    public void WriteEnd(in SourceMappingToken token)
    {
        Debug.Assert(!token.IsMapped || token.IsFor(_writer));
        token.WriteEnd();
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
        WriteEnd(writer);
    }

    internal readonly bool IsFor(CodeWriter writer)
    {
        return ReferenceEquals(_writer, writer);
    }

    internal readonly void WriteEnd()
    {
        WriteEnd(_writer);
    }

    private static void WriteEnd(CodeWriter? writer)
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
