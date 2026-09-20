using System;
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
        if (end == start && start < text.Length && text[start] != '}')
        {
            return false;
        }

        replacementSpan = TextSpan.FromBounds(start, end);
        return true;
    }

    private static bool IsNameCharacter(char character)
    {
        // Same name characters as the syntactic markup-completion context.
        return char.IsLetterOrDigit(character) ||
            character is '_' or '-' or '.' or ':';
    }
}
