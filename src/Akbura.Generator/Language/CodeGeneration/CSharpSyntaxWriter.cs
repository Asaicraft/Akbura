using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct CSharpSyntaxWriter
{
    private readonly CodeWriter _writer;

    public CSharpSyntaxWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
    }

    public void WriteNormalizedNode(CSharpSyntaxNode syntax)
    {
        Debug.Assert(syntax != null);

        var normalized = syntax!.NormalizeWhitespace();

        if (WriteTrimmed(normalized))
        {
            _writer.WriteLine();
        }
    }

    public void WriteExpression(ExpressionSyntax expression)
    {
        Debug.Assert(expression != null);

        WriteTrimmed(expression!);
    }

    public void WriteStatement(StatementSyntax statement)
    {
        Debug.Assert(statement != null);

        if (WriteTrimmed(statement!))
        {
            _writer.WriteLine();
        }
    }

    public void WriteMember(MemberDeclarationSyntax member)
    {
        Debug.Assert(member != null);

        if (WriteTrimmed(member!))
        {
            _writer.WriteLine();
        }
    }

    private bool WriteTrimmed(CSharpSyntaxNode syntax)
    {
        var text = syntax.ToFullString();
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

        if (start == end)
        {
            return false;
        }

        if (_writer.Length == 0)
        {
            _writer.WriteCurrentIndent();
        }

        WriteIndentedLines(text, start, end);
        return true;
    }

    private void WriteIndentedLines(
        string text,
        int start,
        int end)
    {
        var lineStart = start;

        for (var i = start; i < end; i++)
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
}
