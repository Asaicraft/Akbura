using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

internal sealed partial class AkburaCompletionService
{
    private static AkburaCompletionResult CreateMarkupStatementResult(
        AkburaSyntacticDocument document,
        AkburaSyntacticCompletionContext context)
    {
        var next = context.ApplicableSpan.End;
        while (next < document.Text.Length && char.IsWhiteSpace(document.Text[next]))
        {
            next++;
        }

        var hasParenthesis = next < document.Text.Length && document.Text[next] == '(';
        var hasWhitespace = context.ApplicableSpan.End < next;
        var items = ImmutableArray.CreateBuilder<AkburaCompletionItem>(3);
        Add("$if", "Selects a branch using a C# boolean condition.");
        Add("$foreach", "Renders an enumerable source into an indexed mutable child collection.");
        if (context.Kind == AkburaCompletionContextKind.MarkupConditionalContinuation)
        {
            Add("$else if", "Tests another condition after the preceding branches fail.");
            Add("$else", "Selects the final branch when all preceding conditions fail.");
        }

        return new AkburaCompletionResult(context.ApplicableSpan, items.ToImmutable());

        void Add(string name, string description)
        {
            if (!name.StartsWith(context.Prefix, StringComparison.Ordinal))
            {
                return;
            }

            var conditional = name != "$else";
            var insert = conditional && !hasParenthesis ? name + " ()" :
                name + (hasWhitespace ? string.Empty : " ");
            items.Add(new AkburaCompletionItem(name, insert, AkburaCompletionKind.Keyword,
                description, descriptionFactory: null,
                caretOffsetFromEnd: conditional && !hasParenthesis ? 1 : 0,
                triggerCompletionAfterInsert: conditional && !hasParenthesis));
        }
    }
}
