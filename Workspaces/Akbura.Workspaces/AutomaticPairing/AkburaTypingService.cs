using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.AutomaticPairing;

internal sealed class AkburaTypingService : IAkburaTypingService
{
    private static readonly ImmutableArray<TextChange> NoChanges = [];

    public AkburaTypingResult GetResult(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (command.Options == null)
        {
            throw new ArgumentNullException(nameof(command.Options));
        }

        if ((uint)command.Position > (uint)document.Text.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.Position));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var session = ValidateSession(document.Text, command.Session)
            ? command.Session
            : null;
        var normalized = command with { Session = session };

        return command.Kind switch
        {
            AkburaTypingCommandKind.Type => HandleType(
                document,
                normalized,
                cancellationToken),
            AkburaTypingCommandKind.Backspace => HandleBackspace(
                document,
                normalized),
            AkburaTypingCommandKind.Tab => HandleTab(
                document,
                normalized),
            AkburaTypingCommandKind.Return => HandleReturn(
                document,
                normalized,
                cancellationToken),
            _ => AkburaTypingResult.PassThrough(
                command.Position,
                session),
        };
    }

    private static AkburaTypingResult HandleType(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Text.Length != 1)
        {
            return AkburaTypingResult.PassThrough(
                command.Position,
                command.Session);
        }

        var character = command.Text[0];

        if (character == '"' &&
            command.Options.RawStringCompletion &&
            TryHandleRawStringQuote(
                document,
                command,
                cancellationToken,
                out var rawResult))
        {
            return rawResult;
        }

        if (character == '{' &&
            TryHandleInterpolationBrace(
                document,
                command,
                cancellationToken,
                out var interpolationResult))
        {
            return interpolationResult;
        }

        if (character == '>' &&
            TryHandleGeneratedAngleClose(
                document,
                command,
                cancellationToken,
                out var angleResult))
        {
            return angleResult;
        }

        if (TryHandleGeneratedClose(
                document.Text,
                command,
                character,
                out var overtypeResult))
        {
            return overtypeResult;
        }

        if (character == '>')
        {
            return HandleGreaterThan(
                document,
                command,
                cancellationToken);
        }

        if (character == '/')
        {
            return HandleSlash(
                document,
                command,
                cancellationToken);
        }

        var decision = document.GetAutomaticPairDecision(
            command.Position,
            character,
            cancellationToken);
        if (!decision.IsFixed)
        {
            return InsertOnly(command, character.ToString());
        }

        if (character == '{' &&
            decision.ContextKind == AkburaPairContextKind.AkcssSyntax &&
            !IsStructuralAkcssBraceAfterInsertion(
                document,
                command.Position,
                cancellationToken))
        {
            return InsertOnly(command, character.ToString());
        }

        var insertedText = character + decision.ClosingText;
        var kind = character == '<'
            ? AkburaPairSessionKind.MarkupAnglePair
            : AkburaPairSessionKind.FixedPair;
        var session = new AkburaPairSession(
            kind,
            new TextSpan(command.Position, 1),
            new TextSpan(
                command.Position + 1,
                decision.ClosingText.Length),
            character.ToString(),
            decision.ClosingText,
            RequiredDelimiterLength: 1,
            OuterLiteralDelimiterCount: 0);

        return Handled(
            ImmutableArray.Create(
                new TextChange(
                    new TextSpan(command.Position, 0),
                    insertedText)),
            command.Position + 1,
            session,
            triggerCompletion: character == '<',
            triggerSignatureHelp: character == '(');
    }

    private static bool TryHandleGeneratedAngleClose(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken,
        out AkburaTypingResult result)
    {
        var session = command.Session;
        if (session == null ||
            session.Kind != AkburaPairSessionKind.MarkupAnglePair ||
            command.Position != session.ClosingSpan.Start ||
            session.ClosingText.Length == 0 ||
            session.ClosingText[0] != '>')
        {
            result = default!;
            return false;
        }

        var afterGreater = session.ClosingSpan.Start + 1;
        var closingTag = command.Options.AutoClosingTags
            ? document.GetAutoClosingTagText(
                afterGreater,
                cancellationToken)
            : null;
        var changes = string.IsNullOrEmpty(closingTag)
            ? NoChanges
            : ImmutableArray.Create(
                new TextChange(
                    new TextSpan(afterGreater, 0),
                    closingTag!));

        result = Handled(
            changes,
            afterGreater,
            session: null);
        return true;
    }

    private static bool TryHandleGeneratedClose(
        SourceText text,
        AkburaTypingCommand command,
        char character,
        out AkburaTypingResult result)
    {
        var session = command.Session;
        if (session == null ||
            command.Position < session.ClosingSpan.Start ||
            command.Position >= session.ClosingSpan.End)
        {
            result = default!;
            return false;
        }

        var relative = command.Position - session.ClosingSpan.Start;
        if (relative >= session.ClosingText.Length ||
            session.ClosingText[relative] != character ||
            text[command.Position] != character)
        {
            result = default!;
            return false;
        }

        var newPosition = command.Position + 1;
        result = Handled(
            NoChanges,
            newPosition,
            newPosition == session.ClosingSpan.End
                ? null
                : session);
        return true;
    }

    private static AkburaTypingResult HandleGreaterThan(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Options.AutoClosingTags)
        {
            return InsertOnly(command, ">");
        }

        var changedDocument = InsertTemporary(
            document,
            command.Position,
            ">",
            cancellationToken);
        var closingTag = changedDocument.GetAutoClosingTagText(
            command.Position + 1,
            cancellationToken);
        var insertedText = string.IsNullOrEmpty(closingTag)
            ? ">"
            : ">" + closingTag;

        return Handled(
            ImmutableArray.Create(
                new TextChange(
                    new TextSpan(command.Position, 0),
                    insertedText)),
            command.Position + 1,
            session: null);
    }

    private static AkburaTypingResult HandleSlash(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken)
    {
        var changedDocument = InsertTemporary(
            document,
            command.Position,
            "/",
            cancellationToken);
        if (!changedDocument.TryGetSlashCompletionEdit(
                command.Position + 1,
                out var completion,
                cancellationToken) ||
            completion.CompletesClosingTag &&
            !command.Options.AutoClosingTags)
        {
            return InsertOnly(command, "/");
        }

        var insertedText = "/" + completion.InsertionText;
        using var changes = ImmutableArrayBuilder<TextChange>.Rent(2);
        changes.Add(
            new TextChange(
                new TextSpan(command.Position, 0),
                insertedText));

        var indentationDelta = 0;
        if (completion.CompletesClosingTag &&
            TryCreateClosingTagIndentationChange(
                document,
                changedDocument,
                command,
                cancellationToken,
                out var indentationChange))
        {
            changes.Add(indentationChange);
            indentationDelta =
                (indentationChange.NewText?.Length ?? 0) -
                indentationChange.Span.Length;
        }

        return Handled(
            changes.ToImmutable(),
            command.Position +
                insertedText.Length +
                completion.OvertypeLength +
                indentationDelta,
            session: null);
    }

    private static bool TryCreateClosingTagIndentationChange(
        AkburaSyntacticDocument original,
        AkburaSyntacticDocument changed,
        AkburaTypingCommand command,
        CancellationToken cancellationToken,
        out TextChange change)
    {
        change = default;
        var indentationLevel = changed.GetSlashCompletionIndentationLevel(
            command.Position + 1,
            cancellationToken);
        if (indentationLevel == null || command.Position == 0)
        {
            return false;
        }

        var lessPosition = command.Position - 1;
        var line = original.Text.Lines.GetLineFromPosition(lessPosition);
        for (var current = line.Start; current < lessPosition; current++)
        {
            if (original.Text[current] is not (' ' or '\t'))
            {
                return false;
            }
        }

        var desired = CreateIndentation(
            indentationLevel.Value,
            command.Options);
        var span = TextSpan.FromBounds(line.Start, lessPosition);
        if (string.Equals(
                original.Text.ToString(span),
                desired,
                StringComparison.Ordinal))
        {
            return false;
        }

        change = new TextChange(span, desired);
        return true;
    }

    private static bool TryHandleRawStringQuote(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken,
        out AkburaTypingResult result)
    {
        var session = command.Session;
        if (session != null &&
            session.Kind == AkburaPairSessionKind.RawStringQuotes &&
            command.Position == session.OpeningSpan.End &&
            command.Position == session.ClosingSpan.Start)
        {
            var delimiterLength = session.OpeningSpan.Length + 1;
            result = Handled(
                ImmutableArray.Create(
                    new TextChange(
                        new TextSpan(command.Position, 0),
                        "\""),
                    new TextChange(
                        new TextSpan(session.ClosingSpan.End, 0),
                        "\"")),
                command.Position + 1,
                new AkburaPairSession(
                    AkburaPairSessionKind.RawStringQuotes,
                    new TextSpan(
                        session.OpeningSpan.Start,
                        delimiterLength),
                    new TextSpan(
                        session.ClosingSpan.Start + 1,
                        session.ClosingSpan.Length + 1),
                    new string('"', delimiterLength),
                    new string('"', session.ClosingSpan.Length + 1),
                    delimiterLength,
                    OuterLiteralDelimiterCount: 0));
            return true;
        }

        if (document.TryGetRawStringInfo(
                command.Position,
                out var currentInfo,
                cancellationToken) &&
            currentInfo.IsAtEndOfOpeningDelimiter &&
            currentInfo.HasClosingDelimiter &&
            currentInfo.OpeningSpan.End == command.Position &&
            currentInfo.ClosingSpan.Start >= command.Position)
        {
            var quoteCount = currentInfo.QuoteCount + 1;
            result = Handled(
                ImmutableArray.Create(
                    new TextChange(
                        new TextSpan(command.Position, 0),
                        "\""),
                    new TextChange(
                        new TextSpan(currentInfo.ClosingSpan.End, 0),
                        "\"")),
                command.Position + 1,
                new AkburaPairSession(
                    AkburaPairSessionKind.RawStringQuotes,
                    new TextSpan(
                        currentInfo.OpeningSpan.Start,
                        quoteCount),
                    new TextSpan(
                        currentInfo.ClosingSpan.Start + 1,
                        quoteCount),
                    new string('"', quoteCount),
                    new string('"', quoteCount),
                    quoteCount,
                    OuterLiteralDelimiterCount: 0));
            return true;
        }

        var changedDocument = InsertTemporary(
            document,
            command.Position,
            "\"",
            cancellationToken);
        if (!changedDocument.TryGetRawStringInfo(
                command.Position + 1,
                out var info,
                cancellationToken) ||
            !info.IsAtEndOfOpeningDelimiter ||
            info.HasClosingDelimiter ||
            info.QuoteCount != 3)
        {
            result = default!;
            return false;
        }

        var closingText = new string('"', info.QuoteCount);
        result = Handled(
            ImmutableArray.Create(
                new TextChange(
                    new TextSpan(command.Position, 0),
                    "\"" + closingText)),
            command.Position + 1,
            new AkburaPairSession(
                AkburaPairSessionKind.RawStringQuotes,
                info.OpeningSpan,
                new TextSpan(
                    command.Position + 1,
                    info.QuoteCount),
                closingText,
                closingText,
                info.QuoteCount,
                OuterLiteralDelimiterCount: 0));
        return true;
    }

    private static bool TryHandleInterpolationBrace(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken,
        out AkburaTypingResult result)
    {
        var session = command.Session;
        if (session != null &&
            session.Kind == AkburaPairSessionKind.InterpolationBraces &&
            command.Position == session.OpeningSpan.End &&
            command.Position == session.ClosingSpan.Start)
        {
            if (session.RequiredDelimiterLength == 1 &&
                session.OuterLiteralDelimiterCount == 0)
            {
                result = Handled(
                    ImmutableArray.Create(
                        new TextChange(
                            session.ClosingSpan,
                            "{")),
                    command.Position + 1,
                    session: null);
                return true;
            }

            result = Handled(
                ImmutableArray.Create(
                    new TextChange(
                        new TextSpan(command.Position, 0),
                        "{"),
                    new TextChange(
                        new TextSpan(session.ClosingSpan.End, 0),
                        "}")),
                command.Position + 1,
                new AkburaPairSession(
                    AkburaPairSessionKind.InterpolationBraces,
                    new TextSpan(
                        session.OpeningSpan.Start,
                        session.OpeningSpan.Length + 1),
                    new TextSpan(
                        session.ClosingSpan.Start + 1,
                        session.ClosingSpan.Length + 1),
                    session.OpeningText + "{",
                    session.ClosingText + "}",
                    session.RequiredDelimiterLength,
                    session.OuterLiteralDelimiterCount + 1));
            return true;
        }

        var changedDocument = InsertTemporary(
            document,
            command.Position,
            "{",
            cancellationToken);
        if (!changedDocument.TryGetInterpolationInfo(
                command.Position + 1,
                out var info,
                cancellationToken) ||
            !info.IsAtEndOfOpeningDelimiter ||
            info.HasClosingDelimiter ||
            info.RequiredBraceCount <= 0)
        {
            result = default!;
            return false;
        }

        var closingText = new string('}', info.RequiredBraceCount);
        var openingText = new string('{', info.RequiredBraceCount);
        result = Handled(
            ImmutableArray.Create(
                new TextChange(
                    new TextSpan(command.Position, 0),
                    "{" + closingText)),
            command.Position + 1,
            new AkburaPairSession(
                AkburaPairSessionKind.InterpolationBraces,
                info.OpeningSpan,
                new TextSpan(
                    command.Position + 1,
                    closingText.Length),
                openingText,
                closingText,
                info.RequiredBraceCount,
                OuterLiteralDelimiterCount: 0));
        return true;
    }

    private static AkburaTypingResult HandleBackspace(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command)
    {
        var session = command.Session;
        if (session == null ||
            command.Position != session.OpeningSpan.End ||
            command.Position != session.ClosingSpan.Start)
        {
            return AkburaTypingResult.PassThrough(
                command.Position,
                session);
        }

        return Handled(
            ImmutableArray.Create(
                new TextChange(
                    TextSpan.FromBounds(
                        session.OpeningSpan.Start,
                        session.ClosingSpan.End),
                    string.Empty)),
            session.OpeningSpan.Start,
            session: null);
    }

    private static AkburaTypingResult HandleTab(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command)
    {
        var session = command.Session;
        if (session == null ||
            command.Position > session.ClosingSpan.Start ||
            !ContainsOnlyWhitespace(
                document.Text,
                TextSpan.FromBounds(
                    command.Position,
                    session.ClosingSpan.Start)))
        {
            return AkburaTypingResult.PassThrough(
                command.Position,
                session);
        }

        return Handled(
            NoChanges,
            session.ClosingSpan.End,
            session: null);
    }

    private static AkburaTypingResult HandleReturn(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Options.RawStringCompletion)
        {
            return AkburaTypingResult.PassThrough(
                command.Position,
                command.Session);
        }

        TextSpan opening;
        TextSpan closing;
        int quoteCount;
        if (command.Session is
                {
                    Kind: AkburaPairSessionKind.RawStringQuotes,
                } session)
        {
            opening = session.OpeningSpan;
            closing = session.ClosingSpan;
            quoteCount = session.RequiredDelimiterLength;
        }
        else if (document.TryGetRawStringInfo(
                     command.Position,
                     out var info,
                     cancellationToken) &&
                 info.HasClosingDelimiter)
        {
            opening = info.OpeningSpan;
            closing = info.ClosingSpan;
            quoteCount = info.QuoteCount;
        }
        else
        {
            return AkburaTypingResult.PassThrough(
                command.Position,
                command.Session);
        }

        var contentSpan = TextSpan.FromBounds(
            opening.End,
            closing.Start);
        if (command.Position < contentSpan.Start ||
            command.Position > contentSpan.End ||
            document.Text.Lines.GetLineFromPosition(opening.Start).LineNumber !=
                document.Text.Lines.GetLineFromPosition(closing.Start).LineNumber)
        {
            return AkburaTypingResult.PassThrough(
                command.Position,
                command.Session);
        }

        var content = document.Text.ToString(contentSpan);
        if (content.IndexOfAny(['\r', '\n']) >= 0)
        {
            return AkburaTypingResult.PassThrough(
                command.Position,
                command.Session);
        }

        var line = document.Text.Lines.GetLineFromPosition(opening.Start);
        var baseIndentation = GetLeadingWhitespace(
            document.Text,
            line.Start,
            opening.Start);
        var innerIndentation = baseIndentation +
            CreateSingleIndentation(command.Options);
        var newLine = string.IsNullOrEmpty(command.Options.NewLine)
            ? Environment.NewLine
            : command.Options.NewLine;
        var relativeCaret = command.Position - contentSpan.Start;

        string replacement;
        int newPosition;
        if (content.Length == 0)
        {
            replacement = newLine + innerIndentation +
                newLine + baseIndentation;
            newPosition = contentSpan.Start +
                newLine.Length + innerIndentation.Length;
        }
        else
        {
            var before = content.Substring(0, relativeCaret);
            var after = content.Substring(relativeCaret);
            var caretPrefix = newLine + innerIndentation + before +
                newLine + innerIndentation;
            replacement = caretPrefix + after +
                newLine + baseIndentation;
            newPosition = contentSpan.Start + caretPrefix.Length;
        }

        var closingStart = contentSpan.Start + replacement.Length;
        var delimiterText = new string('"', quoteCount);
        return Handled(
            ImmutableArray.Create(
                new TextChange(contentSpan, replacement)),
            newPosition,
            new AkburaPairSession(
                AkburaPairSessionKind.RawStringQuotes,
                opening,
                new TextSpan(closingStart, quoteCount),
                delimiterText,
                delimiterText,
                quoteCount,
                OuterLiteralDelimiterCount: 0));
    }

    private static AkburaTypingResult InsertOnly(
        AkburaTypingCommand command,
        string text)
    {
        return Handled(
            ImmutableArray.Create(
                new TextChange(
                    new TextSpan(command.Position, 0),
                    text)),
            command.Position + text.Length,
            TransformSessionForInsertion(
                command.Session,
                command.Position,
                text.Length));
    }

    private static AkburaPairSession? TransformSessionForInsertion(
        AkburaPairSession? session,
        int position,
        int length)
    {
        if (session == null || length == 0)
        {
            return session;
        }

        if (position <= session.OpeningSpan.Start)
        {
            return session with
            {
                OpeningSpan = new TextSpan(
                    session.OpeningSpan.Start + length,
                    session.OpeningSpan.Length),
                ClosingSpan = new TextSpan(
                    session.ClosingSpan.Start + length,
                    session.ClosingSpan.Length),
            };
        }

        if (position < session.OpeningSpan.End)
        {
            return null;
        }

        if (position <= session.ClosingSpan.Start)
        {
            return session with
            {
                ClosingSpan = new TextSpan(
                    session.ClosingSpan.Start + length,
                    session.ClosingSpan.Length),
            };
        }

        return position < session.ClosingSpan.End
            ? null
            : session;
    }

    private static bool IsStructuralAkcssBraceAfterInsertion(
        AkburaSyntacticDocument document,
        int position,
        CancellationToken cancellationToken)
    {
        var changedDocument = InsertTemporary(
            document,
            position,
            "{",
            cancellationToken);
        return changedDocument.ShouldAutoCloseCurlyBrace(
            position + 1,
            cancellationToken);
    }

    private static AkburaSyntacticDocument InsertTemporary(
        AkburaSyntacticDocument document,
        int position,
        string text,
        CancellationToken cancellationToken)
    {
        var change = new TextChange(
            new TextSpan(position, 0),
            text);
        var changedText = document.Text.WithChanges(change);
        return document.WithText(
            changedText,
            ImmutableArray.Create(
                new TextChangeRange(
                    change.Span,
                    text.Length)),
            cancellationToken);
    }

    private static bool ValidateSession(
        SourceText text,
        AkburaPairSession? session)
    {
        return session == null ||
            IsExpectedText(text, session.OpeningSpan, session.OpeningText) &&
            IsExpectedText(text, session.ClosingSpan, session.ClosingText) &&
            session.OpeningSpan.End <= session.ClosingSpan.Start;
    }

    private static bool IsExpectedText(
        SourceText text,
        TextSpan span,
        string expected)
    {
        return span.Start >= 0 &&
            span.Length >= 0 &&
            span.End <= text.Length &&
            span.Length == expected.Length &&
            string.Equals(
                text.ToString(span),
                expected,
                StringComparison.Ordinal);
    }

    private static bool ContainsOnlyWhitespace(
        SourceText text,
        TextSpan span)
    {
        for (var index = span.Start; index < span.End; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetLeadingWhitespace(
        SourceText text,
        int start,
        int limit)
    {
        var end = start;
        while (end < limit && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text.ToString(TextSpan.FromBounds(start, end));
    }

    private static string CreateSingleIndentation(
        AkburaTypingOptions options)
    {
        var indentationSize = Math.Max(0, options.IndentSize);
        if (indentationSize == 0)
        {
            return string.Empty;
        }

        if (options.InsertSpaces)
        {
            return new string(' ', indentationSize);
        }

        var tabSize = Math.Max(1, options.TabSize);
        return new string('\t', indentationSize / tabSize) +
            new string(' ', indentationSize % tabSize);
    }

    private static string CreateIndentation(
        int level,
        AkburaTypingOptions options)
    {
        var width = Math.Max(0, level) *
            Math.Max(0, options.IndentSize);
        if (options.InsertSpaces)
        {
            return new string(' ', width);
        }

        var tabSize = Math.Max(1, options.TabSize);
        return new string('\t', width / tabSize) +
            new string(' ', width % tabSize);
    }

    private static AkburaTypingResult Handled(
        ImmutableArray<TextChange> changes,
        int newPosition,
        AkburaPairSession? session,
        bool triggerCompletion = false,
        bool triggerSignatureHelp = false)
    {
        return new AkburaTypingResult(
            Handled: true,
            Changes: changes,
            NewPosition: newPosition,
            Session: session,
            TriggerCompletion: triggerCompletion,
            TriggerSignatureHelp: triggerSignatureHelp);
    }
}
