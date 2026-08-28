namespace Akbura.LanguageServer.State;

internal sealed record AkburaOpenDocument(
    Uri Uri,
    string LanguageId,
    int Version,
    SourceText Text,
    AkburaSyntacticDocument SyntacticDocument,
    AkburaProjectId? ProjectId,
    AkburaDocumentId? DocumentId,
    SourceText? ProjectText);