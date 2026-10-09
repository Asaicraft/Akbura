using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System.Collections.Immutable;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Represents one immutable classification result for an editor snapshot.
/// A syntactic result can be published before a project semantic model is
/// available and later be replaced by progressively more complete results
/// for the same request.
/// </summary>
internal class AkburaClassifiedBufferState
{
    public AkburaClassifiedBufferState(
        long requestVersion,
        ITextSnapshot snapshot,
        SourceText text,
        ImmutableArray<AkburaClassifiedSpan> classifications,
        ImmutableArray<AkburaDiagnosticSpan> diagnostics,
        AkburaClassificationStage stage)
    {
        RequestVersion = requestVersion;

        Snapshot = snapshot ??
            throw new ArgumentNullException(
                nameof(snapshot));

        Text = text ??
            throw new ArgumentNullException(
                nameof(text));

        Classifications = classifications;
        Diagnostics = diagnostics;
        Stage = stage;
    }

    public long RequestVersion { get; }

    public ITextSnapshot Snapshot { get; }

    public SourceText Text { get; }

    public ImmutableArray<AkburaClassifiedSpan> Classifications { get; }

    public ImmutableArray<AkburaDiagnosticSpan> Diagnostics { get; }

    public AkburaClassificationStage Stage { get; }

    public bool IncludesSemanticClassifications => Stage >= AkburaClassificationStage.Semantic;
}
