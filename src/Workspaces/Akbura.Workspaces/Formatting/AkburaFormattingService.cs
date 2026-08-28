using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Workspaces.Formatting;

internal sealed class AkburaFormattingService :
    IAkburaFormattingService
{
    public ImmutableArray<TextChange> FormatDocument(
        AkburaSyntacticDocument document,
        AkburaFormattingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        return FormatLines(
            document,
            firstLine: 0,
            lastLine: document.Text.Lines.Count - 1,
            options,
            includeDocumentEnding: true,
            cancellationToken);
    }

    public ImmutableArray<TextChange> FormatRange(
        AkburaSyntacticDocument document,
        TextSpan range,
        AkburaFormattingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        ValidateRange(document.Text, range);
        var firstLine = document.Text.Lines.GetLineFromPosition(
            range.Start).LineNumber;
        var endPosition = range.End > range.Start
            ? range.End - 1
            : range.End;
        var lastLine = document.Text.Lines.GetLineFromPosition(
            Math.Min(endPosition, document.Text.Length)).LineNumber;
        return FormatLines(
            document,
            firstLine,
            lastLine,
            options,
            includeDocumentEnding: false,
            cancellationToken);
    }

    public ImmutableArray<TextChange> FormatOnType(
        AkburaSyntacticDocument document,
        int position,
        char typedCharacter,
        AkburaFormattingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if ((uint)position > (uint)document.Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (typedCharacter is not ('}' or '>' or '\n'))
        {
            return ImmutableArray<TextChange>.Empty;
        }

        var lookup = Math.Min(position, document.Text.Length);
        var line = document.Text.Lines.GetLineFromPosition(lookup);
        return FormatLines(
            document,
            line.LineNumber,
            line.LineNumber,
            options,
            includeDocumentEnding: false,
            cancellationToken);
    }

    private static ImmutableArray<TextChange> FormatLines(
        AkburaSyntacticDocument document,
        int firstLine,
        int lastLine,
        AkburaFormattingOptions options,
        bool includeDocumentEnding,
        CancellationToken cancellationToken)
    {
        if (firstLine > lastLine)
        {
            return ImmutableArray<TextChange>.Empty;
        }

        var text = document.Text;
        var first = text.Lines[firstLine];
        var last = text.Lines[lastLine];
        var replaceSpan = TextSpan.FromBounds(
            first.Start,
            last.EndIncludingLineBreak);
        var builder = new StringBuilder(replaceSpan.Length + 32);
        for (var lineNumber = firstLine;
             lineNumber <= lastLine;
             lineNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = text.Lines[lineNumber];
            var content = text.ToString(line.Span);
            var lineBreak = text.ToString(TextSpan.FromBounds(
                line.End,
                line.EndIncludingLineBreak));
            AppendFormattedLine(
                builder,
                document,
                lineNumber,
                content,
                lineBreak,
                options,
                cancellationToken);
        }

        if (includeDocumentEnding)
        {
            NormalizeDocumentEnding(builder, options, DetectLineBreak(text));
        }

        var replacement = builder.ToString();
        if (string.Equals(
                replacement,
                text.ToString(replaceSpan),
                StringComparison.Ordinal))
        {
            return ImmutableArray<TextChange>.Empty;
        }

        return ImmutableArray.Create(
            new TextChange(replaceSpan, replacement));
    }

    private static void AppendFormattedLine(
        StringBuilder builder,
        AkburaSyntacticDocument document,
        int lineNumber,
        string content,
        string lineBreak,
        AkburaFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var firstContent = 0;
        while (firstContent < content.Length &&
               content[firstContent] is ' ' or '\t')
        {
            firstContent++;
        }

        var end = content.Length;
        if (options.TrimTrailingWhitespace)
        {
            while (end > firstContent &&
                   content[end - 1] is ' ' or '\t')
            {
                end--;
            }
        }

        if (firstContent == content.Length)
        {
            builder.Append(lineBreak);
            return;
        }

        var absoluteContentPosition =
            document.Text.Lines[lineNumber].Start + firstContent;
        var isEmbeddedCSharp = document.TryGetCSharpCompletionContext(
            absoluteContentPosition,
            out _,
            cancellationToken);
        var requiredIndentation = CreateIndentation(
            document.GetDesiredIndentationLevel(
                lineNumber,
                cancellationToken),
            options);
        var currentIndentation = content[..firstContent];
        var indentation = isEmbeddedCSharp &&
                          GetVisualWidth(currentIndentation,
                              options.EffectiveTabSize) >=
                          GetVisualWidth(requiredIndentation,
                              options.EffectiveTabSize)
            ? currentIndentation
            : requiredIndentation;

        builder.Append(indentation);
        builder.Append(content, firstContent, end - firstContent);
        builder.Append(lineBreak);
    }

    private static string CreateIndentation(
        int level,
        AkburaFormattingOptions options)
    {
        if (level <= 0)
        {
            return string.Empty;
        }

        return options.InsertSpaces
            ? new string(' ', level * options.EffectiveTabSize)
            : new string('\t', level);
    }

    private static int GetVisualWidth(string value, int tabSize)
    {
        var width = 0;
        foreach (var character in value)
        {
            width += character == '\t'
                ? tabSize - width % tabSize
                : 1;
        }

        return width;
    }

    private static void NormalizeDocumentEnding(
        StringBuilder builder,
        AkburaFormattingOptions options,
        string lineBreak)
    {
        if (options.TrimFinalNewlines)
        {
            while (builder.Length > 0 &&
                   builder[builder.Length - 1] is '\r' or '\n')
            {
                builder.Length--;
            }
        }

        if (options.InsertFinalNewline &&
            (builder.Length == 0 ||
             builder[builder.Length - 1] is not ('\r' or '\n')))
        {
            builder.Append(lineBreak);
        }
    }

    private static string DetectLineBreak(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            if (line.EndIncludingLineBreak > line.End)
            {
                return text.ToString(TextSpan.FromBounds(
                    line.End,
                    line.EndIncludingLineBreak));
            }
        }

        return Environment.NewLine;
    }

    private static void ValidateRange(SourceText text, TextSpan range)
    {
        if ((uint)range.Start > (uint)text.Length ||
            (uint)range.End > (uint)text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }
    }
}