using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

/// <summary>
/// Describes one source document supplied during a project synchronization.
/// </summary>
public readonly struct AkburaDocumentInput
{
    public AkburaDocumentInput(Uri uri, SourceText text)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public Uri Uri { get; }

    public SourceText Text { get; }
}
