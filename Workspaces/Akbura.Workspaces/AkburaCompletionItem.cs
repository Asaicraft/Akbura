namespace Akbura.Workspaces;

/// <summary>
/// Represents one editor-independent Akbura completion item.
/// </summary>
public sealed class AkburaCompletionItem
{
    private readonly Func<string>? _descriptionFactory;
    private string? _lazyDescription;

    public AkburaCompletionItem(
        string displayText,
        string insertText,
        AkburaCompletionKind kind,
        string description,
        string? filterText = null,
        string? sortText = null)
        : this(
            displayText,
            insertText,
            kind,
            description,
            descriptionFactory: null,
            filterText,
            sortText,
            suffix: null,
            priority: 50,
            caretOffsetFromEnd: 0)
    {
    }

    internal AkburaCompletionItem(
        string displayText,
        string insertText,
        AkburaCompletionKind kind,
        string description,
        Func<string>? descriptionFactory,
        string? filterText = null,
        string? sortText = null,
        string? suffix = null,
        int priority = 50,
        int caretOffsetFromEnd = 0)
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
        _lazyDescription = descriptionFactory != null &&
            string.IsNullOrEmpty(description)
                ? null
                : description ?? string.Empty;
        _descriptionFactory = descriptionFactory;
        FilterText = filterText ?? displayText;
        Priority = priority;
        SortText = sortText ?? $"{priority:D2}_{displayText}";
        Suffix = suffix ?? string.Empty;
        CaretOffsetFromEnd = caretOffsetFromEnd;
    }

    public string DisplayText { get; }

    public string InsertText { get; }

    public string FilterText { get; }

    public string SortText { get; }

    public string Suffix { get; }

    public int Priority { get; }

    public int CaretOffsetFromEnd { get; }

    public AkburaCompletionKind Kind { get; }

    public string Description
    {
        get
        {
            var description = Volatile.Read(ref _lazyDescription);
            if (_descriptionFactory == null || description != null)
            {
                return description ?? string.Empty;
            }

            description = _descriptionFactory() ?? string.Empty;
            Interlocked.CompareExchange(
                ref _lazyDescription,
                description,
                null);
            return _lazyDescription ?? description;
        }
    }
}
