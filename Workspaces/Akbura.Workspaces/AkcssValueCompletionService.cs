using Akbura.Language;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

internal sealed class AkcssValueCompletionService
{
    public ImmutableArray<AkburaCompletionItem> GetItems(
        AkburaSemanticModel semanticModel,
        AkcssSyntacticCompletionContext context,
        CancellationToken cancellationToken)
    {
        if (!semanticModel.TryGetAkcssValueCompletionInfo(
                context.ContainingDeclarationSpan,
                context.PropertyName,
                out var info) ||
            info.ExpectedType is not { } expectedType)
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (semanticModel.IsAkcssColorPropertyType(expectedType))
        {
            AddNamedColors(
                semanticModel,
                info,
                context.Prefix,
                seen,
                items,
                cancellationToken);
        }

        AddExpectedTypeMembers(
            semanticModel,
            info,
            context.Prefix,
            seen,
            items,
            cancellationToken);

        if (semanticModel.IsAkcssColorPropertyType(expectedType))
        {
            AddColorSnippets(
                context.Prefix,
                seen,
                items);
        }

        if (semanticModel.IsAvaloniaThicknessType(expectedType))
        {
            AddThicknessItems(
                context.Prefix,
                seen,
                items);
        }
        else if (semanticModel.IsAvaloniaCornerRadiusType(expectedType))
        {
            AddCornerRadiusItems(
                context.Prefix,
                seen,
                items);
        }

        return items.ToImmutable();
    }

    private static void AddExpectedTypeMembers(
        AkburaSemanticModel semanticModel,
        AkcssValueCompletionInfo info,
        string prefix,
        HashSet<string> seen,
        ImmutableArrayBuilder<AkburaCompletionItem> items,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in semanticModel
                     .LookupAkcssExpectedValuesForCompletion(
                         info,
                         cancellationToken)
                     .OrderBy(static candidate => candidate.DisplayText,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesPrefix(candidate.DisplayText, prefix) ||
                !seen.Add(candidate.DisplayText))
            {
                continue;
            }

            var value = candidate;
            items.Add(new AkburaCompletionItem(
                value.DisplayText,
                value.InsertText,
                AkburaCompletionKind.AkcssValue,
                description: string.Empty,
                descriptionFactory: () =>
                    value.Symbol.ToDisplayString(),
                suffix: value.TypeDisplay,
                priority: 0));
        }
    }

    private static void AddNamedColors(
        AkburaSemanticModel semanticModel,
        AkcssValueCompletionInfo info,
        string prefix,
        HashSet<string> seen,
        ImmutableArrayBuilder<AkburaCompletionItem> items,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in semanticModel
                     .LookupAkcssNamedColorsForCompletion(
                         info.ContainingSymbol,
                         cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesPrefix(candidate.DisplayText, prefix) ||
                !seen.Add(candidate.DisplayText))
            {
                continue;
            }

            var color = candidate;
            items.Add(new AkburaCompletionItem(
                color.DisplayText,
                color.InsertText,
                AkburaCompletionKind.AkcssColor,
                description: string.Empty,
                descriptionFactory: () =>
                    color.Symbol.ToDisplayString(),
                suffix: "Color",
                priority: string.Equals(
                    color.DisplayText,
                    prefix,
                    StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : 10));
        }
    }

    private static void AddColorSnippets(
        string prefix,
        HashSet<string> seen,
        ImmutableArrayBuilder<AkburaCompletionItem> items)
    {
        AddSnippets(
            [
                "\"#000000\"",
                "\"#FF000000\"",
                "\"rgb(0, 0, 0)\"",
                "\"rgba(0, 0, 0, 0.5)\"",
                "\"hsl(0, 0%, 0%)\"",
            ],
            prefix,
            "AKCSS color literal.",
            20,
            seen,
            items);
    }

    private static void AddThicknessItems(
        string prefix,
        HashSet<string> seen,
        ImmutableArrayBuilder<AkburaCompletionItem> items)
    {
        AddSnippets(
            [
                "0",
                "(0, 0)",
                "(0, 0, 0, 0)",
                "(horizontal: 0, vertical: 0)",
                "(left: 0, top: 0, right: 0, bottom: 0)",
            ],
            prefix,
            "AKCSS Thickness value.",
            10,
            seen,
            items);

        if (prefix.Length == 0)
        {
            return;
        }

        foreach (var label in new[]
                 {
                     "horizontal:",
                     "vertical:",
                     "left:",
                     "top:",
                     "right:",
                     "bottom:",
                 })
        {
            if (!MatchesPrefix(label, prefix) ||
                !seen.Add(label))
            {
                continue;
            }

            items.Add(new AkburaCompletionItem(
                label,
                label,
                AkburaCompletionKind.AkcssValue,
                "Named Thickness component.",
                descriptionFactory: null,
                priority: 0));
        }
    }

    private static void AddCornerRadiusItems(
        string prefix,
        HashSet<string> seen,
        ImmutableArrayBuilder<AkburaCompletionItem> items)
    {
        AddSnippets(
            [
                "new CornerRadius(0)",
                "new CornerRadius(0, 0, 0, 0)",
            ],
            prefix,
            "C# CornerRadius expression.",
            10,
            seen,
            items);
    }

    private static void AddSnippets(
        IEnumerable<string> snippets,
        string prefix,
        string description,
        int priority,
        HashSet<string> seen,
        ImmutableArrayBuilder<AkburaCompletionItem> items)
    {
        foreach (var snippet in snippets)
        {
            if (!MatchesSnippetPrefix(snippet, prefix) ||
                !seen.Add(snippet))
            {
                continue;
            }

            items.Add(new AkburaCompletionItem(
                snippet,
                snippet,
                AkburaCompletionKind.AkcssValue,
                description,
                descriptionFactory: null,
                priority: priority));
        }
    }

    private static bool MatchesPrefix(
        string candidate,
        string prefix)
    {
        return prefix.Length == 0 ||
            candidate.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSnippetPrefix(
        string candidate,
        string prefix)
    {
        return prefix.Length == 0 ||
            candidate.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase) ||
            candidate.TrimStart('(', '"').StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase) ||
            candidate.IndexOf(
                prefix,
                StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
