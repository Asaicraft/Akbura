using System.Security.Cryptography;
using System.Text;

namespace Akbura.LanguageServer.Mapping;

internal sealed class AkburaSemanticTokenEncoder
{
    public static readonly string[] TokenTypes =
    [
        "keyword",
        "namespace",
        "type",
        "class",
        "struct",
        "interface",
        "enum",
        "typeParameter",
        "method",
        "property",
        "event",
        "variable",
        "parameter",
        "enumMember",
        "string",
        "number",
        "comment",
        "operator",
        "function",
        "modifier",
    ];

    public SemanticTokens Encode(
        SourceText text,
        ImmutableArray<AkburaClassifiedSpan> classifications,
        IAkburaPositionConverter positions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(positions);

        var spans = classifications
            .Where(static item => item.Span.Length > 0)
            .OrderBy(static item => item.Span.Start)
            .ThenByDescending(static item => item.Span.Length)
            .ToArray();
        var data = new List<int>(spans.Length * 5);
        var previousLine = 0;
        var previousCharacter = 0;
        var coveredUntil = -1;

        foreach (var classified in spans)
        {
            var span = Clamp(classified.Span, text.Length);
            if (span.Length == 0 || span.Start < coveredUntil)
            {
                continue;
            }

            var tokenType = GetTokenType(classified.Kind);
            var firstLine = text.Lines.GetLineFromPosition(span.Start);
            var lastPosition = Math.Max(span.Start, span.End - 1);
            var lastLine = text.Lines.GetLineFromPosition(lastPosition);

            for (var lineNumber = firstLine.LineNumber;
                 lineNumber <= lastLine.LineNumber;
                 lineNumber++)
            {
                var line = text.Lines[lineNumber];
                var segmentStart = Math.Max(span.Start, line.Start);
                var segmentEnd = Math.Min(span.End, line.End);
                if (segmentEnd <= segmentStart)
                {
                    continue;
                }

                var position = positions.ToPosition(text, segmentStart);
                var deltaLine = position.Line - previousLine;
                var deltaCharacter = deltaLine == 0
                    ? position.Character - previousCharacter
                    : position.Character;
                data.Add(deltaLine);
                data.Add(deltaCharacter);
                data.Add(segmentEnd - segmentStart);
                data.Add(tokenType);
                data.Add(0);
                previousLine = position.Line;
                previousCharacter = position.Character;
            }

            coveredUntil = span.End;
        }

        var result = data.ToArray();
        return new SemanticTokens
        {
            Data = result,
            ResultId = CreateResultId(result),
        };
    }

    private static int GetTokenType(AkburaClassificationKind kind)
    {
        return kind switch
        {
            AkburaClassificationKind.Keyword or
            AkburaClassificationKind.Directive => 0,
            AkburaClassificationKind.Namespace => 1,
            AkburaClassificationKind.Type or
            AkburaClassificationKind.MarkupExtensionType => 2,
            AkburaClassificationKind.Component or
            AkburaClassificationKind.ClassName => 3,
            AkburaClassificationKind.StructName => 4,
            AkburaClassificationKind.InterfaceName => 5,
            AkburaClassificationKind.EnumName => 6,
            AkburaClassificationKind.TypeParameterName => 7,
            AkburaClassificationKind.MethodName or
            AkburaClassificationKind.ExtensionMethodName => 8,
            AkburaClassificationKind.PropertyName or
            AkburaClassificationKind.Attribute or
            AkburaClassificationKind.MarkupExtensionProperty => 9,
            AkburaClassificationKind.EventName => 10,
            AkburaClassificationKind.ParameterName => 12,
            AkburaClassificationKind.EnumMemberName => 13,
            AkburaClassificationKind.String or
            AkburaClassificationKind.MarkupText or
            AkburaClassificationKind.MarkupExtensionValue => 14,
            AkburaClassificationKind.Number => 15,
            AkburaClassificationKind.Comment => 16,
            AkburaClassificationKind.Operator or
            AkburaClassificationKind.Punctuation or
            AkburaClassificationKind.MarkupExtensionPunctuation => 17,
            AkburaClassificationKind.Utility => 18,
            AkburaClassificationKind.UtilityModifier => 19,
            _ => 11,
        };
    }

    private static TextSpan Clamp(TextSpan span, int length)
    {
        var start = Math.Clamp(span.Start, 0, length);
        var end = Math.Clamp(span.End, start, length);
        return TextSpan.FromBounds(start, end);
    }

    private static string CreateResultId(int[] data)
    {
        var text = string.Join(",", data);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }
}