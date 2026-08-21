namespace Akbura.Workspaces;

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

    public static bool IsSupportedInsertionCharacter(
        char character)
    {
        return character != '\0' &&
            !char.IsWhiteSpace(character) &&
            !char.IsDigit(character);
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
