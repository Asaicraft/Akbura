namespace Akbura.Workspaces.AutomaticPairing;

public sealed record AkburaTypingCommand(
    AkburaTypingCommandKind Kind,
    int Position,
    string Text,
    AkburaTypingOptions Options,
    AkburaPairSession? Session);
