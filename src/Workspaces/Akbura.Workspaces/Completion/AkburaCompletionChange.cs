using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

/// <summary>
/// Describes the editor-independent text changes produced when a completion
/// item is committed.
/// </summary>
public sealed class AkburaCompletionChange
{
    public AkburaCompletionChange(
        ImmutableArray<TextChange> changes,
        int newPosition,
        bool triggerNextCompletion)
    {
        Changes = changes.IsDefault
            ? ImmutableArray<TextChange>.Empty
            : changes;
        NewPosition = newPosition;
        TriggerNextCompletion = triggerNextCompletion;
    }

    public ImmutableArray<TextChange> Changes { get; }

    public int NewPosition { get; }

    public bool TriggerNextCompletion { get; }
}