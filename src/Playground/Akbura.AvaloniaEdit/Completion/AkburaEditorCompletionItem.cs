using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace Akbura.AvaloniaEdit.Completion;

/// <summary>
/// Preserves the workspace item's source identity and separate display/filter/insertion fields.
/// </summary>
public sealed class AkburaEditorCompletionItem : ICompletionData
{
    private readonly AkburaEditorSession _session;

    internal AkburaEditorCompletionItem(AkburaEditorSession session, AkburaEditorSession.RequestSnapshot snapshot,
        AkburaSyntacticDocument syntax, AkburaDocumentContext context,
        AkburaCompletionItem? native, AkburaProjectedCompletionItem? projected, TextSpan span)
    {
        _session = session;
        Snapshot = snapshot;
        Syntax = syntax;
        Context = context;
        Native = native;
        Projected = projected;
        SourceSpan = span;
    }

    internal AkburaEditorSession.RequestSnapshot Snapshot
    {
        get;
    }

    internal AkburaSyntacticDocument Syntax
    {
        get;
    }

    internal AkburaDocumentContext Context
    {
        get;
    }

    internal AkburaCompletionItem? Native
    {
        get;
    }

    internal AkburaProjectedCompletionItem? Projected
    {
        get;
    }

    public TextSpan SourceSpan
    {
        get;
    }

    public bool IsProjected => Projected != null;

    public string ResolveKey => Projected?.ResolveKey ?? Native!.ResolveKey;

    public string DisplayText => Projected?.DisplayText ?? Native!.DisplayText;

    public string InsertText => Projected?.InsertText ?? Native!.InsertText;

    public string SortText => Projected?.SortText ?? Native!.SortText;

    public string Kind => Projected?.Kind.ToString() ?? Native!.Kind.ToString();

    public string Text => Projected?.FilterText ?? Native!.FilterText;

    public object Content => Native is { Suffix.Length: > 0 } item ? $"{DisplayText}  {item.Suffix}" : DisplayText;

    /// <summary>
    /// Gets the projected completion detail, when available.
    /// </summary>
    /// <remarks>
    /// Native lazy documentation is resolved separately by the session to avoid executing
    /// its factory in a drawing callback.
    /// </remarks>
    public object? Description => Projected?.Detail;

    /// <summary>
    /// Gets the description presented by AvaloniaEdit's built-in tooltip.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> because the session presents documentation beside
    /// the completion list through <see cref="CompletionDescriptionPopup"/>.
    /// </remarks>
    object? ICompletionData.Description => null;

    public IImage? Image => null;

    public double Priority => Native is { } item ? 100 - item.Priority : 50;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        _session.CommitFromPopup(this);
}
