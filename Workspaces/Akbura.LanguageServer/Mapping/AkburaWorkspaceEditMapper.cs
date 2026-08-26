namespace Akbura.LanguageServer.Mapping;

internal readonly record struct AkburaWorkspaceEditDocument(
    Uri Uri,
    SourceText Text,
    ImmutableArray<TextChange> Changes,
    int? Version);

internal static class AkburaWorkspaceEditMapper
{
    public static WorkspaceEdit Create(
        ImmutableArray<AkburaWorkspaceEditDocument> documents,
        bool supportsDocumentChanges,
        IAkburaPositionConverter positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (documents.IsDefault)
        {
            documents = [];
        }

        var ordered = documents
            .OrderBy(
                static document => document.Uri.AbsoluteUri,
                StringComparer.Ordinal)
            .ToArray();
        if (supportsDocumentChanges)
        {
            var documentChanges = new TextDocumentEdit[ordered.Length];
            for (var index = 0; index < ordered.Length; index++)
            {
                var document = ordered[index];
                documentChanges[index] = new TextDocumentEdit
                {
                    TextDocument =
                        new OptionalVersionedTextDocumentIdentifier
                        {
                            Uri = document.Uri.AbsoluteUri,
                            Version = document.Version,
                        },
                    Edits = AkburaProtocolMapper.ToTextEdits(
                        document.Text,
                        document.Changes,
                        positions),
                };
            }

            return new WorkspaceEdit
            {
                DocumentChanges = documentChanges,
            };
        }

        var changes = new Dictionary<string, TextEdit[]>(
            ordered.Length,
            StringComparer.Ordinal);
        foreach (var document in ordered)
        {
            changes[document.Uri.AbsoluteUri] =
                AkburaProtocolMapper.ToTextEdits(
                    document.Text,
                    document.Changes,
                    positions);
        }

        return new WorkspaceEdit
        {
            Changes = changes,
        };
    }
}