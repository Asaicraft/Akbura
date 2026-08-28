using Microsoft.CodeAnalysis.Text;

namespace Akbura.LanguageServer.Protocol.PositionEncoding;

public enum AkburaPositionEncoding
{
    Utf16,
}

public interface IAkburaPositionConverter
{
    int ToOffset(SourceText text, Position position);

    Position ToPosition(SourceText text, int offset);

    TextSpan ToTextSpan(SourceText text, Range range);

    Range ToRange(SourceText text, TextSpan span);
}

public sealed class Utf16PositionConverter : IAkburaPositionConverter
{
    public int ToOffset(SourceText text, Position position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(position);

        if ((uint)position.Line >= (uint)text.Lines.Count)
        {
            throw InvalidPosition(position, "line is outside the document");
        }

        var line = text.Lines[position.Line];
        var lineLength = line.End - line.Start;
        if ((uint)position.Character > (uint)lineLength)
        {
            throw InvalidPosition(position, "character is outside the line");
        }

        return line.Start + position.Character;
    }

    public Position ToPosition(SourceText text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if ((uint)offset > (uint)text.Length)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                $"Offset {offset} is outside the document.");
        }

        var line = text.Lines.GetLineFromPosition(offset);
        return new Position
        {
            Line = line.LineNumber,
            Character = offset - line.Start,
        };
    }

    public TextSpan ToTextSpan(SourceText text, Range range)
    {
        ArgumentNullException.ThrowIfNull(range);
        var start = ToOffset(text, range.Start);
        var end = ToOffset(text, range.End);
        if (end < start)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                "Range end precedes range start.");
        }

        return TextSpan.FromBounds(start, end);
    }

    public Range ToRange(SourceText text, TextSpan span)
    {
        ArgumentNullException.ThrowIfNull(text);
        if ((uint)span.Start > (uint)text.Length ||
            (uint)span.End > (uint)text.Length)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                $"Span {span} is outside the document.");
        }

        return new Range
        {
            Start = ToPosition(text, span.Start),
            End = ToPosition(text, span.End),
        };
    }

    private static AkburaProtocolException InvalidPosition(
        Position position,
        string reason)
    {
        return new AkburaProtocolException(
            LspErrorCodes.InvalidParams,
            $"Position ({position.Line}, {position.Character}) is invalid: " +
            reason + ".");
    }
}