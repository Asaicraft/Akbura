using System.Collections.Immutable;

namespace Akbura.Workspaces.References;

public interface IAkburaDocumentHighlightService
{
    ImmutableArray<AkburaDocumentHighlight> GetHighlights(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default);
}
