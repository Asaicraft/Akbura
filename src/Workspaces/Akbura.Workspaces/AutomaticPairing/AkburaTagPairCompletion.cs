using Akbura.Workspaces.Documents;

namespace Akbura.Workspaces.AutomaticPairing;

public readonly record struct AkburaTagPairCompletion(
    string InsertionText,
    int CaretOffset);

public static class AkburaTagPairCompletionFactory
{
    public static bool TryCreate(
        AkburaSyntacticDocument document,
        int position,
        AkburaTypingOptions options,
        out AkburaTagPairCompletion completion,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var closingTag = document.GetAutoClosingTagText(
            position,
            cancellationToken);
        if (closingTag == null)
        {
            completion = default;
            return false;
        }

        var line = document.Text.Lines.GetLineFromPosition(position - 1);
        var indentationLevel = document.GetDesiredIndentationLevel(
            line.LineNumber,
            cancellationToken);
        var outerIndentation = CreateIndentation(
            options,
            indentationLevel);
        var innerIndentation = CreateIndentation(
            options,
            indentationLevel + 1);
        var newLine = string.IsNullOrEmpty(options.NewLine)
            ? Environment.NewLine
            : options.NewLine;
        var insertionText = newLine + innerIndentation +
            newLine + outerIndentation + closingTag;

        completion = new AkburaTagPairCompletion(
            insertionText,
            newLine.Length + innerIndentation.Length);
        return true;
    }

    private static string CreateIndentation(
        AkburaTypingOptions options,
        int indentationLevel)
    {
        var width = Math.Max(0, indentationLevel) *
            Math.Max(0, options.IndentSize);
        if (width == 0)
        {
            return string.Empty;
        }

        if (options.InsertSpaces)
        {
            return new string(' ', width);
        }

        var tabSize = Math.Max(1, options.TabSize);
        return new string('\t', width / tabSize) +
            new string(' ', width % tabSize);
    }
}
