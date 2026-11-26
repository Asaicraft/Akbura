namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Discovers applicable utility definitions in immutable workspace snapshots.
/// </summary>
public interface IAkburaUtilityCatalogService
{
    AkburaUtilitySearchResult FindUtilities(
        AkburaDocumentContext context,
        AkburaUtilitySearchRequest request,
        CancellationToken cancellationToken = default);

    AkburaStandardUtilityLibrary GetStandardLibrary(
        AkburaProjectSnapshot project,
        CancellationToken cancellationToken = default);
}
