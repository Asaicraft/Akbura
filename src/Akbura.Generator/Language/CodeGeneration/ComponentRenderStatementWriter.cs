using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentRenderStatementWriter
{
    private readonly CodeWriter _writer;
    private readonly SourceMappingWriter _mappings;

    public ComponentRenderStatementWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
    }

    public void Write(in ComponentRenderStatementPlan plan)
    {
        using var mapping = _mappings.WriteStart(plan.Syntax);

        switch (plan.Kind)
        {
            case ComponentRenderStatementKind.Statement:
                WriteStatement(plan.Node);
                return;

            case ComponentRenderStatementKind.UseHookInvocation:
                WriteUseHookInvocation(plan.Node);
                return;

            default:
                Debug.Fail("An invalid render statement reached code generation.");
                return;
        }
    }

    private void WriteStatement(CSharpSyntaxNode syntax)
    {
        var text = syntax.ToFullString();
        var range = GetTrimmedRange(text);

        if (range.Length == 0)
        {
            return;
        }

        WriteIndentedLines(text, range);
        _writer.WriteLine();
    }

    private void WriteIndentedLines(
        string text,
        in TextRange range)
    {
        var end = range.Start + range.Length;
        var lineStart = range.Start;

        for (var i = range.Start; i < end; i++)
        {
            var current = text[i];
            if (current is not '\r' and not '\n')
            {
                continue;
            }

            if (i > lineStart)
            {
                _writer.Write(text, lineStart, i - lineStart);
            }

            _writer.WriteLine();

            if (current == '\r' &&
                i + 1 < end &&
                text[i + 1] == '\n')
            {
                i++;
            }

            lineStart = i + 1;
        }

        if (lineStart < end)
        {
            _writer.Write(text, lineStart, end - lineStart);
        }
    }

    private void WriteUseHookInvocation(CSharpSyntaxNode syntax)
    {
        var text = syntax.ToString();
        var range = GetTrimmedRange(text);

        if (range.Length == 0)
        {
            return;
        }

        WriteIndentedLines(text, range);
        _writer.WriteLine(";");
    }

    private static TextRange GetTrimmedRange(string text)
    {
        var start = 0;
        var end = text.Length;

        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return new TextRange(start, end - start);
    }

    private readonly struct TextRange
    {
        public TextRange(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }
    }
}
