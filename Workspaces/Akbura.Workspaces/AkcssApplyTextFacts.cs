using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal static class AkcssApplyTextFacts
{
    public static bool TryGetItemSpan(
        SourceText text,
        AkcssApplyDirectiveSyntax apply,
        int position,
        out TextSpan itemSpan)
    {
        foreach (var span in GetItemSpans(text, apply))
        {
            if (span.Contains(position) ||
                position == span.End)
            {
                itemSpan = span;
                return true;
            }
        }

        return TryGetEmptyItemSpan(
            text,
            apply,
            position,
            out itemSpan);
    }

    public static ImmutableArray<TextSpan> GetItemSpans(
        SourceText text,
        AkcssApplyDirectiveSyntax apply)
    {
        GetItemsBounds(text, apply, out var start, out var end);
        using var spans = ImmutableArrayBuilder<TextSpan>.Rent();
        var current = start;
        while (current < end)
        {
            while (current < end &&
                   char.IsWhiteSpace(text[current]))
            {
                current++;
            }

            if (current >= end)
            {
                break;
            }

            var itemStart = current;
            while (current < end &&
                   !char.IsWhiteSpace(text[current]) &&
                   text[current] != ';')
            {
                current++;
            }

            if (itemStart < current)
            {
                spans.Add(TextSpan.FromBounds(itemStart, current));
            }
        }

        return spans.ToImmutable();
    }

    private static bool TryGetEmptyItemSpan(
        SourceText text,
        AkcssApplyDirectiveSyntax apply,
        int position,
        out TextSpan itemSpan)
    {
        GetItemsBounds(text, apply, out var start, out var end);
        if (position < start || position > end)
        {
            itemSpan = default;
            return false;
        }

        itemSpan = new TextSpan(position, 0);
        return true;
    }

    private static void GetItemsBounds(
        SourceText text,
        AkcssApplyDirectiveSyntax apply,
        out int start,
        out int end)
    {
        start = Math.Max(
            0,
            Math.Min(text.Length, apply.ApplyKeyword.Span.End));
        end = apply.Semicolon.IsMissing
            ? Math.Min(text.Length, apply.FullSpan.End)
            : Math.Min(text.Length, apply.Semicolon.Span.Start);

        if (apply.Green.GetSlot(2) != null)
        {
            var itemsSpan = apply.Items.FullSpan;
            start = Math.Max(start, itemsSpan.Start);
            end = Math.Max(
                start,
                Math.Min(end, itemsSpan.End));
        }

        end = Math.Max(start, end);
    }
}
