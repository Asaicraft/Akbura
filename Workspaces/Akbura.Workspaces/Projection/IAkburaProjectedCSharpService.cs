using Akbura.Workspaces.Completion;
using Akbura.Workspaces.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Projection;

/// <summary>
/// Provides editor-independent Roslyn language services for C# fragments
/// embedded in Akbura and AKCSS documents.
/// </summary>
public interface IAkburaProjectedCSharpService
{
    Task<AkburaProjectedCompletionResult?> GetCompletionsAsync(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        AkburaProjectedCompletionTrigger trigger,
        CancellationToken cancellationToken = default);

    Task<AkburaProjectedCompletionResolution?> ResolveCompletionAsync(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        string resolveKey,
        CancellationToken cancellationToken = default);

    Task<AkburaQuickInfo?> GetQuickInfoAsync(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes why projected C# completion was requested.
/// </summary>
public readonly record struct AkburaProjectedCompletionTrigger(
    bool IsExplicit,
    bool IsIncomplete,
    char Character);

/// <summary>
/// Contains projected C# completion items mapped back to Akbura source.
/// </summary>
public readonly struct AkburaProjectedCompletionResult
{
    private readonly ImmutableArray<AkburaProjectedCompletionItem> _items;

    public AkburaProjectedCompletionResult(
        ImmutableArray<AkburaProjectedCompletionItem> items,
        bool isIncomplete)
    {
        _items = items.IsDefault
            ? ImmutableArray<AkburaProjectedCompletionItem>.Empty
            : items;
        IsIncomplete = isIncomplete;
    }

    public ImmutableArray<AkburaProjectedCompletionItem> Items =>
        _items.IsDefault
            ? ImmutableArray<AkburaProjectedCompletionItem>.Empty
            : _items;

    public bool IsIncomplete { get; }
}

/// <summary>
/// Represents one Roslyn completion item whose source span is expressed in
/// the host Akbura document.
/// </summary>
public sealed class AkburaProjectedCompletionItem
{
    internal AkburaProjectedCompletionItem(
        string displayText,
        string insertText,
        string filterText,
        string sortText,
        string? detail,
        TextSpan sourceSpan,
        string resolveKey,
        AkburaProjectedCompletionKind kind)
    {
        DisplayText = displayText;
        InsertText = insertText;
        FilterText = filterText;
        SortText = sortText;
        Detail = detail;
        SourceSpan = sourceSpan;
        ResolveKey = resolveKey;
        Kind = kind;
    }

    public string DisplayText { get; }

    public string InsertText { get; }

    public string FilterText { get; }

    public string SortText { get; }

    public string? Detail { get; }

    public TextSpan SourceSpan { get; }

    public string ResolveKey { get; }

    public AkburaProjectedCompletionKind Kind { get; }
}

/// <summary>
/// Contains lazily computed documentation and the canonical Roslyn commit
/// edit mapped to the host Akbura document.
/// </summary>
public sealed class AkburaProjectedCompletionResolution
{
    internal AkburaProjectedCompletionResolution(
        string? detail,
        string documentation,
        AkburaCompletionChange change)
    {
        Detail = detail;
        Documentation = documentation;
        Change = change;
    }

    public string? Detail { get; }

    public string Documentation { get; }

    public AkburaCompletionChange Change { get; }
}

public enum AkburaProjectedCompletionKind
{
    Text,
    Method,
    Constructor,
    Field,
    Variable,
    Class,
    Interface,
    Module,
    Property,
    Enum,
    Keyword,
    EnumMember,
    Constant,
    Struct,
    Event,
    Operator,
    TypeParameter,
}