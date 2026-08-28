using Akbura.Pools;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

internal static class AkburaRoslynCompletionItemSelector
{
    public const int AutomaticItemLimit = 256;

    public static AkburaRoslynCompletionSelection Select(
        CompletionList list,
        SourceText text,
        int position,
        bool isExplicit,
        CancellationToken cancellationToken)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var rawItems = list.ItemsList;
        var rawItemCount = rawItems.Count;
        if (isExplicit)
        {
            using var selected =
                ImmutableArrayBuilder<CompletionItem>.Rent(
                    rawItemCount);
            for (var index = 0;
                 index < rawItemCount;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                selected.Add(rawItems[index]);
            }

            return new AkburaRoslynCompletionSelection(
                selected.ToImmutable(),
                prefix: string.Empty,
                rawItemCount,
                isIncomplete: false);
        }

        var prefix = GetPrefix(
            list.Span,
            text,
            position);
        var candidates =
            ArrayBuilder<RankedCompletionItem>.GetInstance(
                Math.Min(
                    rawItemCount,
                    AutomaticItemLimit));
        try
        {
            for (var index = 0;
                 index < rawItemCount;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = rawItems[index];
                var matchKind = GetMatchKind(
                    GetFilterText(item),
                    prefix);
                if (matchKind == CompletionMatchKind.None)
                {
                    continue;
                }

                candidates.Add(new RankedCompletionItem(
                    item,
                    matchKind,
                    item.Rules.MatchPriority,
                    index));
            }

            cancellationToken.ThrowIfCancellationRequested();
            candidates.Sort(RankedCompletionItemComparer.Instance);

            var selectedCount = Math.Min(
                candidates.Count,
                AutomaticItemLimit);
            using var selected =
                ImmutableArrayBuilder<CompletionItem>.Rent(
                    selectedCount);
            for (var index = 0;
                 index < selectedCount;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                selected.Add(candidates[index].Item);
            }

            return new AkburaRoslynCompletionSelection(
                selected.ToImmutable(),
                prefix,
                rawItemCount,
                candidates.Count > selectedCount);
        }
        finally
        {
            candidates.Free();
        }
    }

    internal static CompletionMatchKind GetMatchKind(
        string candidate,
        string prefix)
    {
        if (string.Equals(
                candidate,
                prefix,
                StringComparison.Ordinal))
        {
            return CompletionMatchKind.Exact;
        }

        if (candidate.StartsWith(
                prefix,
                StringComparison.Ordinal))
        {
            return CompletionMatchKind.Prefix;
        }

        if (candidate.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return CompletionMatchKind.PrefixIgnoreCase;
        }

        if (IsCamelCaseMatch(candidate, prefix))
        {
            return CompletionMatchKind.CamelCase;
        }

        return IsSubsequenceMatch(candidate, prefix)
            ? CompletionMatchKind.Subsequence
            : CompletionMatchKind.None;
    }

    private static string GetPrefix(
        TextSpan completionSpan,
        SourceText text,
        int position)
    {
        var start = Math.Max(
            0,
            Math.Min(
                completionSpan.Start,
                text.Length));
        var end = Math.Max(
            start,
            Math.Min(
                position,
                text.Length));
        return text.ToString(
            TextSpan.FromBounds(start, end));
    }

    private static string GetFilterText(
        CompletionItem item)
    {
        return string.IsNullOrEmpty(item.FilterText)
            ? item.DisplayText
            : item.FilterText;
    }

    private static bool IsCamelCaseMatch(
        string candidate,
        string prefix)
    {
        var prefixIndex = 0;
        for (var index = 0;
             index < candidate.Length &&
             prefixIndex < prefix.Length;
             index++)
        {
            if (!IsWordStart(candidate, index) ||
                !EqualsIgnoreCase(
                    candidate[index],
                    prefix[prefixIndex]))
            {
                continue;
            }

            prefixIndex++;
        }

        return prefixIndex == prefix.Length;
    }

    private static bool IsWordStart(
        string value,
        int index)
    {
        if (index == 0)
        {
            return true;
        }

        var current = value[index];
        var previous = value[index - 1];
        return !char.IsLetterOrDigit(previous) ||
            char.IsUpper(current) &&
            (!char.IsUpper(previous) ||
             index + 1 < value.Length &&
             char.IsLower(value[index + 1]));
    }

    private static bool IsSubsequenceMatch(
        string candidate,
        string prefix)
    {
        var prefixIndex = 0;
        for (var index = 0;
             index < candidate.Length &&
             prefixIndex < prefix.Length;
             index++)
        {
            if (EqualsIgnoreCase(
                    candidate[index],
                    prefix[prefixIndex]))
            {
                prefixIndex++;
            }
        }

        return prefixIndex == prefix.Length;
    }

    private static bool EqualsIgnoreCase(
        char left,
        char right)
    {
        return char.ToUpperInvariant(left) ==
            char.ToUpperInvariant(right);
    }

    private readonly record struct RankedCompletionItem(
        CompletionItem Item,
        CompletionMatchKind MatchKind,
        int MatchPriority,
        int OriginalIndex);

    private sealed class RankedCompletionItemComparer :
        IComparer<RankedCompletionItem>
    {
        public static RankedCompletionItemComparer Instance { get; } =
            new();

        public int Compare(
            RankedCompletionItem left,
            RankedCompletionItem right)
        {
            var match = left.MatchKind.CompareTo(
                right.MatchKind);
            if (match != 0)
            {
                return match;
            }

            var priority = right.MatchPriority.CompareTo(
                left.MatchPriority);
            return priority != 0
                ? priority
                : left.OriginalIndex.CompareTo(
                    right.OriginalIndex);
        }
    }
}

internal readonly struct AkburaRoslynCompletionSelection
{
    public AkburaRoslynCompletionSelection(
        ImmutableArray<CompletionItem> items,
        string prefix,
        int rawItemCount,
        bool isIncomplete)
    {
        Items = items.IsDefault
            ? []
            : items;
        Prefix = prefix ?? string.Empty;
        RawItemCount = rawItemCount;
        IsIncomplete = isIncomplete;
    }

    public ImmutableArray<CompletionItem> Items { get; }

    public string Prefix { get; }

    public int RawItemCount { get; }

    public bool IsIncomplete { get; }
}

internal enum CompletionMatchKind
{
    Exact,
    Prefix,
    PrefixIgnoreCase,
    CamelCase,
    Subsequence,
    None,
}
