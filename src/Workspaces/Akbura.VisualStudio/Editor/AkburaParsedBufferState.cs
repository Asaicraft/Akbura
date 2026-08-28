using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System.Collections.Immutable;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Represents one completely calculated semantic editor state.
/// </summary>
internal sealed class AkburaParsedBufferState :
    AkburaClassifiedBufferState
{
    public AkburaParsedBufferState(
        long requestVersion,
        ITextSnapshot snapshot,
        SourceText text,
        AkburaDocumentContext context,
        ImmutableArray<AkburaClassifiedSpan> classifications,
        ImmutableArray<AkburaDiagnosticSpan> diagnostics)
        : base(
            requestVersion,
            snapshot,
            text,
            classifications,
            diagnostics,
            includesSemanticClassifications: true)
    {
        Context = context ??
            throw new ArgumentNullException(
                nameof(context));
    }

    public AkburaDocumentContext Context { get; }

    public AkburaProjectSnapshot Project => Context.Project;

    public AkburaDocumentSnapshot Document => Context.Document;
}
