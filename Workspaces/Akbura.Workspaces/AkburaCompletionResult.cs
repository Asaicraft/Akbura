using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

/// <summary>
/// Contains completion items and the source span replaced on commit.
/// </summary>
public readonly struct AkburaCompletionResult
{
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
        Items = items.IsDefault
            ? ImmutableArray<AkburaCompletionItem>.Empty
            : items;
        IsIncomplete = isIncomplete;
    }

    public TextSpan ApplicableSpan { get; }

    public ImmutableArray<AkburaCompletionItem> Items { get; }

    /// <summary>
    /// Gets whether another completion request should be made after the user
    /// types more text or a newer semantic snapshot becomes available.
    /// </summary>
    public bool IsIncomplete { get; }

    public bool IsEmpty => Items.IsDefaultOrEmpty;
}
