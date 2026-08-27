namespace Akbura.LanguageServer.Protocol;

public sealed class AkburaTypingParams
{
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();

    public Position Position { get; set; } = new();

    public string Command { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public FormattingOptions Options { get; set; } = new();

    public AkburaPairSessionDto? Session { get; set; }
}

public sealed class AkburaTypingResponse
{
    public bool Handled { get; set; }

    public bool Stale { get; set; }

    public int Version { get; set; }

    public TextEdit[] Edits { get; set; } = [];

    public Position Position { get; set; } = new();

    public AkburaPairSessionDto? Session { get; set; }

    public bool TriggerCompletion { get; set; }

    public bool TriggerSignatureHelp { get; set; }
}

public sealed class AkburaPairSessionDto
{
    public string Kind { get; set; } = string.Empty;

    public Range OpeningRange { get; set; } = new();

    public Range ClosingRange { get; set; } = new();

    public string OpeningText { get; set; } = string.Empty;

    public string ClosingText { get; set; } = string.Empty;

    public int RequiredDelimiterLength { get; set; }

    public int OuterLiteralDelimiterCount { get; set; }
}
