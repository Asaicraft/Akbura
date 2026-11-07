namespace Akbura.AvaloniaEdit.Completion;

internal static class CompletionEdits
{
    // Validate the entire set before touching the editable buffer. All offsets share one UTF-16 base.
    public static ImmutableArray<TextChange> Validate(AkburaCompletionChange change, int length)
    {
        // An insertion at a replacement's start is valid (for example a leading import).
        // Sort it first here so reverse application replaces the old text before inserting the prefix.
        var edits = change.Changes.OrderBy(edit => edit.Span.Start).ThenBy(edit => edit.Span.Length).ToImmutableArray();
        var end = -1;
        var previousStart = -1;
        long newLength = length;

        foreach (var edit in edits)
        {
            if (edit.Span.Start > length || edit.Span.Length > length - edit.Span.Start ||
                edit.Span.Start < end || edit.Span.Start == previousStart && edit.Span.Length == 0)
            {
                throw new InvalidOperationException("Completion contains invalid or overlapping source edits.");
            }

            end = edit.Span.End;
            previousStart = edit.Span.Start;
            newLength += (edit.NewText?.Length ?? 0) - edit.Span.Length;
        }

        return change.NewPosition < 0 || change.NewPosition > newLength || newLength > int.MaxValue
            ? throw new InvalidOperationException("Completion returned an invalid caret position.")
            : edits;
    }
}
