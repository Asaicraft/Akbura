namespace Akbura.Workspaces;

/// <summary>
/// Represents one editor-independent Akbura completion item.
/// </summary>
public sealed class AkburaCompletionItem
{
    public AkburaCompletionItem(
        string displayText,
        string insertText,
        AkburaCompletionKind kind,
        string description,
        string? filterText = null,
        string? sortText = null)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            throw new ArgumentException(
                "Completion display text cannot be empty.",
                nameof(displayText));
        }

        DisplayText = displayText;
        InsertText = insertText ??
            throw new ArgumentNullException(nameof(insertText));
        Kind = kind;
        Description = description ?? string.Empty;
        FilterText = filterText ?? displayText;
        SortText = sortText ?? displayText;
    }

    public string DisplayText { get; }

    public string InsertText { get; }

    public string FilterText { get; }

    public string SortText { get; }

    public AkburaCompletionKind Kind { get; }

    public string Description { get; }
}
