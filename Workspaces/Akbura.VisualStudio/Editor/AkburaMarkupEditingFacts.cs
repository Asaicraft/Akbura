using Microsoft.VisualStudio.Text;

namespace Akbura.VisualStudio.Editor;

internal static class AkburaMarkupEditingFacts
{
    public static bool IsCompletionNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) ||
            value is '_' or '-' or '.' or ':';
    }

    public static bool IsUsingDirectiveNamePosition(
        ITextSnapshot snapshot,
        int position,
        bool isAkcss)
    {
        if (position < 0 || position > snapshot.Length)
        {
            return false;
        }

        var lineStart = position;
        while (lineStart > 0 &&
               snapshot[lineStart - 1] is not '\r' and not '\n')
        {
            lineStart--;
        }

        var index = SkipWhitespace(snapshot, lineStart, position);
        if (isAkcss)
        {
            if (index >= position || snapshot[index] != '@')
            {
                return false;
            }

            index++;
        }
        else if (MatchesKeyword(snapshot, index, position, "global"))
        {
            index += "global".Length;
            if (index >= position || !char.IsWhiteSpace(snapshot[index]))
            {
                return false;
            }

            index = SkipWhitespace(snapshot, index, position);
        }

        if (!MatchesKeyword(snapshot, index, position, "using"))
        {
            return false;
        }

        index += "using".Length;
        if (index >= position || !char.IsWhiteSpace(snapshot[index]))
        {
            return false;
        }

        for (; index < position; index++)
        {
            if (snapshot[index] == ';')
            {
                return false;
            }
        }

        return true;
    }

    private static int SkipWhitespace(
        ITextSnapshot snapshot,
        int start,
        int end)
    {
        while (start < end && char.IsWhiteSpace(snapshot[start]))
        {
            start++;
        }

        return start;
    }

    private static bool MatchesKeyword(
        ITextSnapshot snapshot,
        int start,
        int end,
        string keyword)
    {
        if (start + keyword.Length > end)
        {
            return false;
        }

        for (var index = 0; index < keyword.Length; index++)
        {
            if (snapshot[start + index] != keyword[index])
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsPotentialCompletionPosition(
        ITextSnapshot snapshot,
        int position)
    {
        if (IsMarkupExtensionTypeCompletionPosition(
                snapshot,
                position))
        {
            return true;
        }

        if (position <= 0 || position > snapshot.Length)
        {
            return false;
        }

        var minimum = Math.Max(0, position - 4096);
        var tagStart = -1;
        for (var index = position - 1;
             index >= minimum;
             index--)
        {
            var character = snapshot[index];
            if (character == '>')
            {
                return false;
            }

            if (character == '<')
            {
                tagStart = index;
                break;
            }
        }

        if (tagStart < 0 ||
            (tagStart + 1 < position &&
             snapshot[tagStart + 1] is '!' or '?'))
        {
            return false;
        }

        var quote = '\0';
        var expressionDepth = 0;
        for (var index = tagStart + 1;
             index < position;
             index++)
        {
            var character = snapshot[index];
            if (quote != '\0')
            {
                if (character == quote &&
                    (index == tagStart + 1 ||
                     snapshot[index - 1] != '\\'))
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '{')
            {
                expressionDepth++;
            }
            else if (character == '}' &&
                     expressionDepth > 0)
            {
                expressionDepth--;
            }
        }

        if (quote != '\0' || expressionDepth != 0)
        {
            return false;
        }

        var tokenStart = position;
        while (tokenStart > tagStart + 1 &&
               IsCompletionNameCharacter(
                   snapshot[tokenStart - 1]))
        {
            tokenStart--;
        }

        var previous = tokenStart - 1;
        while (previous > tagStart &&
               char.IsWhiteSpace(snapshot[previous]))
        {
            previous--;
        }

        return previous <= tagStart ||
            snapshot[previous] != '=';
    }

    public static bool IsMarkupExtensionTypeCompletionPosition(
        ITextSnapshot snapshot,
        int position)
    {
        if (position <= 0 || position > snapshot.Length)
        {
            return false;
        }

        var nameStart = position;
        while (nameStart > 0 &&
               IsCompletionNameCharacter(snapshot[nameStart - 1]))
        {
            nameStart--;
        }

        var openBrace = nameStart - 1;
        while (openBrace >= 0 &&
               char.IsWhiteSpace(snapshot[openBrace]))
        {
            openBrace--;
        }

        if (openBrace < 1 ||
            snapshot[openBrace] != '{' ||
            snapshot[openBrace - 1] != '$')
        {
            return false;
        }

        var tagStart = openBrace - 2;
        while (tagStart >= 0 &&
               snapshot[tagStart] is not ('<' or '>'))
        {
            tagStart--;
        }

        if (tagStart < 0 || snapshot[tagStart] != '<' ||
            (tagStart + 1 < snapshot.Length &&
             snapshot[tagStart + 1] is '/' or '!' or '?'))
        {
            return false;
        }

        var quote = '\0';
        for (var index = tagStart + 1;
             index < openBrace - 1;
             index++)
        {
            var character = snapshot[index];
            if (quote == '\0')
            {
                if (character is '\'' or '"')
                {
                    quote = character;
                }
            }
            else if (character == quote &&
                     (index == tagStart + 1 ||
                      snapshot[index - 1] != '\\'))
            {
                quote = '\0';
            }
        }

        return quote == '\0';
    }
}
