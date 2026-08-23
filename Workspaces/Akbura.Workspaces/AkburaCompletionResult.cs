using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

/// <summary>
/// Contains completion items and the source span replaced on commit.
/// </summary>
public readonly struct AkburaCompletionResult
{
    private readonly ImmutableArray<AkburaCompletionItem> _items;

    public AkburaCompletionResult(
        TextSpan applicableSpan,
        ImmutableArray<AkburaCompletionItem> items)
        : this(applicableSpan, items, isIncomplete: false)
    {
    }

    public AkburaCompletionResult(
        TextSpan applicableSpan,
        ImmutableArray<AkburaCompletionItem> items,
        bool isIncomplete)
    {
        ApplicableSpan = applicableSpan;
        _items = items.IsDefault
            ? ImmutableArray<AkburaCompletionItem>.Empty
            : items;
        IsIncomplete = isIncomplete;
    }

    public TextSpan ApplicableSpan { get; }

    public ImmutableArray<AkburaCompletionItem> Items => _items.IsDefault
        ? ImmutableArray<AkburaCompletionItem>.Empty
        : _items;

    /// <summary>
    /// Gets whether another completion request should be made after the user
    /// types more text or a newer semantic snapshot becomes available.
    /// </summary>
    public bool IsIncomplete { get; }

    public bool IsEmpty => _items.IsDefaultOrEmpty;
}
