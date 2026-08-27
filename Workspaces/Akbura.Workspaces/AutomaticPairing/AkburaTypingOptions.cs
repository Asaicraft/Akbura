namespace Akbura.Workspaces.AutomaticPairing;

public sealed record AkburaTypingOptions(
    int TabSize,
    int IndentSize,
    bool InsertSpaces,
    string NewLine)
{
    public bool AutoClosingTags { get; init; } = true;

    public bool RawStringCompletion { get; init; } = true;
}