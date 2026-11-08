namespace Akbura.AvaloniaEdit.Completion;

/// <summary>
/// Applies the existing native completion kinds' commit characters at the editor input boundary.
/// </summary>
/// <remarks>
/// Mirrors the Visual Studio adapter's native item policy. Token punctuation such as
/// utility hyphens and module-name dots remains ordinary input. Canonical edits still
/// come from the workspace, and projected C# retains its existing punctuation handling.
/// </remarks>
internal static class CompletionCommitPolicy
{
    internal static bool ShouldCommit(AkburaEditorCompletionItem item, char character)
    {
        if (char.IsLetterOrDigit(character) || character == '_')
        {
            return false;
        }

        if (item.IsProjected)
        {
            return true;
        }

        if (character is '\t' or '\n' or '\r')
        {
            return true;
        }

        if (character == '(' && item.Native!.Kind == AkburaCompletionKind.Keyword &&
            item.DisplayText != "$else" && item.Syntax.GetCompletionContext(item.Snapshot.Position).Kind is
                AkburaCompletionContextKind.MarkupStatement or AkburaCompletionContextKind.MarkupConditionalContinuation)
        {
            return true;
        }

        var start = item.SourceSpan.Start;
        if (start > 0 && item.Snapshot.Text[start - 1] is '"' or '\'')
        {
            return item.InsertText.EndsWith(".", StringComparison.Ordinal)
                ? character == '.'
                : character is '"' or '\'';
        }

        return item.Native!.Kind switch
        {
            AkburaCompletionKind.Component or AkburaCompletionKind.PropertyElement => character is '>' or ' ',
            AkburaCompletionKind.ClosingTag => character == '>',
            AkburaCompletionKind.MarkupExtension or AkburaCompletionKind.TailwindUtility or
                AkburaCompletionKind.AkcssStyle or AkburaCompletionKind.Keyword or AkburaCompletionKind.Hook => character == ' ',
            AkburaCompletionKind.AkcssModule => character == ';',
            AkburaCompletionKind.AkcssValue or AkburaCompletionKind.AkcssColor => character is ';' or ' ',
            _ => character is '=' or ' ',
        };
    }
}
