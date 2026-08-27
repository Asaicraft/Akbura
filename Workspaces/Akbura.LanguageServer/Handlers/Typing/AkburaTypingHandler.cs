using Akbura.Workspaces.AutomaticPairing;
using System.Text.Json;

namespace Akbura.LanguageServer.Handlers.Typing;

internal sealed class AkburaTypingHandler :
    AkburaLspHandler<AkburaTypingParams, AkburaTypingResponse>
{
    public override string Method => LspMethods.Typing;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(AkburaTypingParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<AkburaLspHandlerResult<AkburaTypingResponse>>
        HandleAsync(
            AkburaTypingParams parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
    {
        var document = context.OpenDocument!;
        if (parameters.TextDocument.Version != document.Version)
        {
            return Task.FromResult(
                new AkburaLspHandlerResult<AkburaTypingResponse>(
                    new AkburaTypingResponse
                    {
                        Handled = false,
                        Stale = true,
                        Version = document.Version,
                        Position = parameters.Position,
                        Session = parameters.Session,
                    }));
        }

        var position = context.Services.PositionConverter.ToOffset(
            document.Text,
            parameters.Position);
        var command = new AkburaTypingCommand(
            ParseCommand(parameters.Command),
            position,
            parameters.Text ?? string.Empty,
            CreateOptions(parameters.Options, document.Text),
            MapSessionFromProtocol(
                parameters.Session,
                document.Text,
                context.Services.PositionConverter));
        var result = context.Services.Workspace.LanguageServices.Typing
            .GetResult(
                document.SyntacticDocument,
                command,
                cancellationToken);

        var changedText = result.Changes.IsDefaultOrEmpty
            ? document.Text
            : document.Text.WithChanges(
                result.Changes.OrderBy(
                    static change => change.Span.Start));
        var response = new AkburaTypingResponse
        {
            Handled = result.Handled,
            Stale = false,
            Version = document.Version,
            Edits = AkburaProtocolMapper.ToTextEdits(
                document.Text,
                result.Changes,
                context.Services.PositionConverter),
            Position = context.Services.PositionConverter.ToPosition(
                changedText,
                result.NewPosition),
            Session = MapSessionToProtocol(
                result.Session,
                changedText,
                context.Services.PositionConverter),
            TriggerCompletion = result.TriggerCompletion,
            TriggerSignatureHelp = result.TriggerSignatureHelp,
        };

        return Task.FromResult(
            new AkburaLspHandlerResult<AkburaTypingResponse>(response));
    }

    private static AkburaTypingCommandKind ParseCommand(string value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "type" => AkburaTypingCommandKind.Type,
            "backspace" => AkburaTypingCommandKind.Backspace,
            "tab" => AkburaTypingCommandKind.Tab,
            "return" => AkburaTypingCommandKind.Return,
            _ => throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                $"Unknown Akbura typing command '{value}'."),
        };
    }

    private static AkburaTypingOptions CreateOptions(
        FormattingOptions options,
        SourceText text)
    {
        options ??= new FormattingOptions();
        var tabSize = Math.Max(1, options.TabSize);
        return new AkburaTypingOptions(
            tabSize,
            Math.Max(
                0,
                GetAdditionalInt(
                    options,
                    "indentSize",
                    tabSize)),
            options.InsertSpaces,
            GetAdditionalString(
                options,
                "newLine") ?? DetectNewLine(text))
        {
            AutoClosingTags = GetAdditionalBool(
                options,
                "autoClosingTags",
                fallback: true),
            RawStringCompletion = GetAdditionalBool(
                options,
                "rawStringCompletion",
                fallback: true),
        };
    }

    private static int GetAdditionalInt(
        FormattingOptions options,
        string name,
        int fallback)
    {
        return options.AdditionalOptions != null &&
            options.AdditionalOptions.TryGetValue(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var result)
                ? result
                : fallback;
    }

    private static bool GetAdditionalBool(
        FormattingOptions options,
        string name,
        bool fallback)
    {
        return options.AdditionalOptions != null &&
            options.AdditionalOptions.TryGetValue(name, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;
    }

    private static string? GetAdditionalString(
        FormattingOptions options,
        string name)
    {
        return options.AdditionalOptions != null &&
            options.AdditionalOptions.TryGetValue(name, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string DetectNewLine(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            if (line.EndIncludingLineBreak > line.End)
            {
                return text.ToString(
                    TextSpan.FromBounds(
                        line.End,
                        line.EndIncludingLineBreak));
            }
        }

        return Environment.NewLine;
    }

    private static AkburaPairSession? MapSessionFromProtocol(
        AkburaPairSessionDto? session,
        SourceText text,
        IAkburaPositionConverter positions)
    {
        if (session == null)
        {
            return null;
        }

        if (!Enum.TryParse<AkburaPairSessionKind>(
                session.Kind,
                ignoreCase: true,
                out var kind))
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                $"Unknown Akbura pair session kind '{session.Kind}'.");
        }

        return new AkburaPairSession(
            kind,
            positions.ToTextSpan(text, session.OpeningRange),
            positions.ToTextSpan(text, session.ClosingRange),
            session.OpeningText ?? string.Empty,
            session.ClosingText ?? string.Empty,
            session.RequiredDelimiterLength,
            session.OuterLiteralDelimiterCount);
    }

    private static AkburaPairSessionDto? MapSessionToProtocol(
        AkburaPairSession? session,
        SourceText text,
        IAkburaPositionConverter positions)
    {
        if (session == null)
        {
            return null;
        }

        return new AkburaPairSessionDto
        {
            Kind = session.Kind.ToString(),
            OpeningRange = positions.ToRange(
                text,
                session.OpeningSpan),
            ClosingRange = positions.ToRange(
                text,
                session.ClosingSpan),
            OpeningText = session.OpeningText,
            ClosingText = session.ClosingText,
            RequiredDelimiterLength = session.RequiredDelimiterLength,
            OuterLiteralDelimiterCount =
                session.OuterLiteralDelimiterCount,
        };
    }
}
