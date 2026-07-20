namespace Akbura;

public sealed class AkburaUseHooksFrameChangedException : AkburaException
{
    public AkburaUseHooksFrameChangedException(
        AkburaControl akburaControl,
        int expectedCount,
        int actualCount,
        int mismatchIndex = -1)
        : base(CreateMessage(akburaControl, expectedCount, actualCount, mismatchIndex))
    {
        AkburaControl = akburaControl;
        ExpectedCount = expectedCount;
        ActualCount = actualCount;
        MismatchIndex = mismatchIndex;
    }

    public AkburaControl AkburaControl { get; }

    public int ExpectedCount { get; }

    public int ActualCount { get; }

    public int MismatchIndex { get; }

    private static string CreateMessage(
        AkburaControl akburaControl,
        int expectedCount,
        int actualCount,
        int mismatchIndex)
    {
        ArgumentNullException.ThrowIfNull(akburaControl);

        return mismatchIndex < 0
            ? $"'{akburaControl.GetType().FullName}' invoked {actualCount} use hooks, " +
                $"but the previous render invoked {expectedCount}."
            : $"'{akburaControl.GetType().FullName}' changed the use hook at index {mismatchIndex}. " +
                "Use hooks must be invoked in the same order on every render.";
    }
}
