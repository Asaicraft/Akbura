namespace Akbura;

public sealed class AkburaUpdateLimitExceededException : AkburaException
{
    public AkburaUpdateLimitExceededException(
        AkburaControl akburaControl,
        int maxUpdatesPerBatch)
        : base(CreateMessage(akburaControl, maxUpdatesPerBatch))
    {
        AkburaControl = akburaControl;
        MaxUpdatesPerBatch = maxUpdatesPerBatch;
    }

    public AkburaControl AkburaControl { get; }

    public int MaxUpdatesPerBatch { get; }

    private static string CreateMessage(
        AkburaControl akburaControl,
        int maxUpdatesPerBatch)
    {
        ArgumentNullException.ThrowIfNull(akburaControl);

        return $"Component '{akburaControl.GetType().FullName}' requested more than " +
            $"{maxUpdatesPerBatch} consecutive Update() passes. " +
            "An Update() pass probably changes a state or parameter on every render.";
    }
}
