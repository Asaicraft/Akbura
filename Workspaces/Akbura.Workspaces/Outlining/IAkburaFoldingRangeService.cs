using System.Collections.Immutable;

namespace Akbura.Workspaces.Outlining;

public interface IAkburaFoldingRangeService
{
    ImmutableArray<AkburaOutliningRegion> GetFoldingRanges(
        AkburaDocumentContext context,
        CancellationToken cancellationToken = default);
}
