using System;
using System.Threading;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Completion;

/// <summary>
/// Validates the replacement range of a markup-extension type completion
/// against the current text, after an editor has translated its original span.
/// </summary>
public static class AkburaMarkupExtensionCompletionFacts
{
    /// <summary>
    /// Narrows a translated range to the extension name. Never includes the
    /// surrounding ${ / }, generic-argument braces, or extension arguments.
    /// The returned range is always contained in <paramref name="translatedSpan"/>.
    /// </summary>
    public static bool TryGetNameReplacementSpan(
        SourceText text,
        TextSpan translatedSpan,
        out TextSpan replacementSpan)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        replacementSpan = default;
        if ((uint)translatedSpan.Start > (uint)text.Length ||
            (uint)translatedSpan.End > (uint)text.Length)
        {
            return false;
        }

        // A session can start at the empty position after ${. Whitespace and
        // an automatically inserted } can both arrive after that snapshot.
        var start = translatedSpan.Start;
        while (start < translatedSpan.End && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var openBrace = start - 1;
        while (openBrace >= 0 && char.IsWhiteSpace(text[openBrace]))
        {
            openBrace--;
        }

        if (openBrace < 1 || text[openBrace] != '{' || text[openBrace - 1] != '$')
        {
            return false;
        }

        var end = start;
        while (end < translatedSpan.End && IsNameCharacter(text[end]))
        {
            end++;
        }

        // Empty ${|} and an incomplete ${| are valid insertion points.
        // Do not reinterpret an argument/string as an extension-name edit.
        if (end == start && start < text.Length &&
            !char.IsWhiteSpace(text[start]) && text[start] is not ('}' or '/' or '>'))
        {
            return false;
        }

        replacementSpan = TextSpan.FromBounds(start, end);
        return true;
    }

    /// <summary>
    /// Finds the current type-name context belonging to a specific ${ opener.
    /// The opener is tracked by the host while asynchronous parsing is pending.
    /// Text inside strings, arguments, or a different extension is not a match.
    /// </summary>
    public static bool TryGetTypeNameSpan(
        AkburaSyntacticDocument document,
        int position,
        int openingBracePosition,
        out TextSpan nameSpan,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        nameSpan = default;
        var text = document.Text;
        if ((uint)position > (uint)text.Length ||
            openingBracePosition < 1 || openingBracePosition >= text.Length ||
            position <= openingBracePosition ||
            text[openingBracePosition] != '{' || text[openingBracePosition - 1] != '$')
        {
            return false;
        }

        var context = document.GetCompletionContext(position, cancellationToken);
        if (context.Kind != AkburaCompletionContextKind.MarkupExtensionType ||
            context.ApplicableSpan.Start <= openingBracePosition ||
            context.ApplicableSpan.End != position)
        {
            return false;
        }

        // GetCompletionContext can find another, nested ${. Do not act on it
        // using a request that belonged to an earlier opener.
        for (var index = openingBracePosition + 1; index < context.ApplicableSpan.Start; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return false;
            }
        }

        nameSpan = context.ApplicableSpan;
        return true;
    }

    /// <summary>
    /// An old attribute session starts before ${. A type-name session starts
    /// at the name itself. Do not restart a correctly anchored session merely
    /// because its inclusive tracking span also picked up an auto-inserted }.
    /// </summary>
    public static bool IsTypeNameSession(TextSpan sessionSpan, TextSpan nameSpan)
    {
        return sessionSpan.Start == nameSpan.Start && sessionSpan.End >= nameSpan.End;
    }

    private static bool IsNameCharacter(char character)
    {
        // Same name characters as the syntactic markup-completion context.
        return char.IsLetterOrDigit(character) ||
            character is '_' or '-' or '.' or ':';
    }
}
