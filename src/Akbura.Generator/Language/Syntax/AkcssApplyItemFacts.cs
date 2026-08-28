using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Syntax;

internal static class AkcssApplyItemFacts
{
    public static ImmutableArray<AkcssApplyItem> GetItems(
        SourceText text,
        AkcssApplyDirectiveSyntax apply)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (apply == null)
        {
            throw new ArgumentNullException(nameof(apply));
        }

        GetItemsBounds(text, apply, out var start, out var end);
        using var items = ImmutableArrayBuilder<AkcssApplyItem>.Rent();
        var current = start;
        while (current < end)
        {
            while (current < end && char.IsWhiteSpace(text[current]))
            {
                current++;
            }

            if (current >= end || IsTerminator(text[current]))
            {
                break;
            }

            var itemStart = current;
            while (current < end &&
                   !char.IsWhiteSpace(text[current]) &&
                   !IsTerminator(text[current]))
            {
                current++;
            }

            if (itemStart < current)
            {
                var span = TextSpan.FromBounds(itemStart, current);
                items.Add(new AkcssApplyItem(span, text.ToString(span)));
            }
        }

        return items.ToImmutable();
    }

    private static bool IsTerminator(char character)
    {
        return character is ';' or '}';
    }

    public static bool TryGetReferenceItem(
        SourceText text,
        AkcssApplyDirectiveSyntax apply,
        int position,
        out AkcssApplyItem item)
    {
        foreach (var candidate in GetItems(text, apply))
        {
            if (candidate.Span.Contains(position))
            {
                item = candidate;
                return true;
            }
        }

        item = default;
        return false;
    }

    public static bool TryGetCompletionItem(
        SourceText text,
        AkcssApplyDirectiveSyntax apply,
        int position,
        out AkcssApplyItem item)
    {
        foreach (var candidate in GetItems(text, apply))
        {
            if (candidate.Span.Contains(position) ||
                position == candidate.Span.End)
            {
                item = candidate;
                return true;
            }
        }

        GetItemsBounds(text, apply, out var start, out var end);
        if (position >= start && position <= end)
        {
            item = new AkcssApplyItem(new TextSpan(position, 0), string.Empty);
            return true;
        }

        item = default;
        return false;
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
            end = Math.Max(start, Math.Min(end, itemsSpan.End));
        }

        end = Math.Max(start, end);
    }
}
