namespace Akbura.Workspaces.Outlining;

internal sealed class AkburaFoldingRangeService :
    IAkburaFoldingRangeService
{
    public System.Collections.Immutable.ImmutableArray<AkburaOutliningRegion>
        GetFoldingRanges(
            AkburaDocumentContext context,
            CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        cancellationToken.ThrowIfCancellationRequested();

        return AkburaSyntacticDocument.Create(
            context.Document,
            cancellationToken).OutliningRegions;
    }
}
