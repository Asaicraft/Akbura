namespace Akbura.Mcp;

/// <summary>
/// Counts registered Akbura and AKCSS inputs and lists the Akbura document paths.
/// </summary>
internal sealed record ProjectDocumentSummary(int AkburaCount, int AkcssCount, string[] AkburaPaths);
