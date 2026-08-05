using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System.Collections.Immutable;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Represents one completely calculated editor state.
///
/// The instance is immutable and is published atomically.
/// Editor services either observe the previous complete state
/// or the next complete state, never a partially updated state.
/// </summary>
internal sealed class AkburaParsedBufferState
{
    public AkburaParsedBufferState(
        long requestVersion,
        ITextSnapshot snapshot,
        SourceText text,
        AkburaDocumentSnapshot document,
        ImmutableArray<AkburaClassifiedSpan> classifications)
    {
        RequestVersion = requestVersion;

        Snapshot = snapshot ??
            throw new ArgumentNullException(nameof(snapshot));

        Text = text ??
            throw new ArgumentNullException(nameof(text));

        Document = document ??
            throw new ArgumentNullException(nameof(document));

        Classifications = classifications;
    }

    public long RequestVersion { get; }

    public ITextSnapshot Snapshot { get; }

    public SourceText Text { get; }

    public AkburaDocumentSnapshot Document { get; }

    public ImmutableArray<AkburaClassifiedSpan>
        Classifications
    { get; }
}