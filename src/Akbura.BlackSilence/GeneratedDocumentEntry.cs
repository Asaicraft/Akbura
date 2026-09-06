using Microsoft.CodeAnalysis.Text;

namespace Akbura.BlackSilence;

/// <summary>
/// One completed immutable result. Only successful batches publish these entries.
/// </summary>
internal sealed class GeneratedDocumentEntry
{
    public GeneratedDocumentEntry(
        string identity,
        DocumentGenerationVersion version,
        string hintName,
        SourceText sourceText)
    {
        Identity = identity;
        Version = version;
        Source = new GeneratedSource(hintName, sourceText);
    }

    public string Identity { get; }
    public DocumentGenerationVersion Version { get; }
    public GeneratedSource Source { get; }
}
