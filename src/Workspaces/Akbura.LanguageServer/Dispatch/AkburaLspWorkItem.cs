namespace Akbura.LanguageServer.Dispatch;

internal sealed class AkburaLspWorkItem
{
    public AkburaLspWorkItem(
        long sequence,
        AkburaLspHandlerDescriptor descriptor,
        object? parameters,
        CancellationToken cancellationToken)
    {
        Sequence = sequence;
        Descriptor = descriptor;
        Parameters = parameters;
        CancellationToken = cancellationToken;
        Completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public long Sequence { get; }

    public AkburaLspHandlerDescriptor Descriptor { get; }

    public object? Parameters { get; }

    public CancellationToken CancellationToken { get; }

    public TaskCompletionSource<object?> Completion { get; }
}