namespace Akbura.Workspaces.QuickInfo;

/// <summary>
/// Provides editor-independent Quick Info for native Akbura syntax.
/// </summary>
public interface IAkburaQuickInfoService
{
    AkburaQuickInfo? GetQuickInfo(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default);
}
