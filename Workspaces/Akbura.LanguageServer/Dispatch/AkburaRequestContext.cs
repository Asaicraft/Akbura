namespace Akbura.LanguageServer.Dispatch;

internal sealed class AkburaRequestContext
{
    public required string Method { get; init; }

    public required AkburaSolutionSnapshot Solution { get; init; }

    public required AkburaServerSnapshot ServerSnapshot { get; init; }

    public AkburaOpenDocument? OpenDocument { get; init; }

    public AkburaSyntacticDocument? SyntacticDocument { get; init; }

    public AkburaDocumentContext? SemanticDocument { get; init; }

    public required AkburaClientCapabilities ClientCapabilities { get; init; }

    public required AkburaPositionEncoding PositionEncoding { get; init; }

    public required AkburaLanguageServerServices Services { get; init; }
}