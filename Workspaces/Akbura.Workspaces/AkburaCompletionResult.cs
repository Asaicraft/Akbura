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
    {
        ApplicableSpan = applicableSpan;
        Items = items.IsDefault
            ? ImmutableArray<AkburaCompletionItem>.Empty
            : items;
    }

    public TextSpan ApplicableSpan { get; }

    public ImmutableArray<AkburaCompletionItem> Items { get; }

    public bool IsEmpty => Items.IsDefaultOrEmpty;
}
