namespace Akbura.Workspaces.Completion;

internal static class AkburaMarkupStatementCompletionFacts
{
    internal static bool IsStatement(AkburaCompletionItem item) =>
        item.Kind == AkburaCompletionKind.Keyword &&
        item.DisplayText is "$if" or "$foreach" or "$else if" or "$else";

    internal static bool IncludesOpeningParenthesis(AkburaCompletionItem item) =>
        IsStatement(item) && item.InsertText.IndexOf('(') >= 0;

    internal static bool IncludesCommitCharacter(AkburaCompletionItem item, char character) =>
        IsStatement(item) && (character == ' ' || character == '(' && IncludesOpeningParenthesis(item));

    internal static string GetInsertTextBeforeExistingParenthesis(AkburaCompletionItem item, bool hasWhitespace)
    {
        var parenthesis = item.InsertText.IndexOf('(');
        var prefix = parenthesis >= 0 ? item.InsertText.Substring(0, parenthesis) : item.InsertText;
        return prefix.TrimEnd() + (hasWhitespace ? string.Empty : " ");
    }
}
