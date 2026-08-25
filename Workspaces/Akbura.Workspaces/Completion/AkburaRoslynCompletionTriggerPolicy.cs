using Microsoft.CodeAnalysis.Completion;

namespace Akbura.Workspaces.Completion;

internal static class AkburaRoslynCompletionTriggerPolicy
{
    public static AkburaRoslynCompletionPreflight Evaluate(
        bool isExplicit,
        bool isIncompleteSession,
        bool isSupportedInsertion,
        bool shouldTriggerCompletion)
    {
        if (isExplicit)
        {
            return AkburaRoslynCompletionPreflight.Explicit;
        }

        if (isIncompleteSession)
        {
            return AkburaRoslynCompletionPreflight
                .IncompleteSession;
        }

        if (!isSupportedInsertion)
        {
            return AkburaRoslynCompletionPreflight
                .UnsupportedInsertion;
        }

        return shouldTriggerCompletion
            ? AkburaRoslynCompletionPreflight.Triggered
            : AkburaRoslynCompletionPreflight.RoslynSuppressed;
    }

    public static CompletionTrigger CreateRoslynTrigger(
        bool isExplicit,
        bool isIncompleteSession,
        char triggerCharacter)
    {
        // Roslyn suppresses identifier-continuation insertion triggers.
        // An incomplete VS session still needs a refreshed full catalog,
        // which is filtered by Akbura's automatic item selector afterwards.
        return isExplicit || isIncompleteSession
            ? CompletionTrigger.Invoke
            : CompletionTrigger.CreateInsertionTrigger(
                triggerCharacter);
    }

    public static bool IsSupportedInsertionCharacter(
        char character)
    {
        return character != '\0' &&
            !char.IsWhiteSpace(character) &&
            !char.IsDigit(character) &&
            character != '{';
    }
}

internal enum AkburaRoslynCompletionPreflight
{
    Unavailable,
    Explicit,
    IncompleteSession,
    Triggered,
    UnsupportedInsertion,
    RoslynSuppressed,
}
