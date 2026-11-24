using Akbura.AvaloniaEdit.Infrastructure;

namespace Akbura.AvaloniaEdit.UnitTests;

internal sealed class ControlledScheduler : IAkburaEditorScheduler
{
    private Hold? _next;
    public bool FailNext
    {
        get; set;
    }

    public Hold HoldNextResult(int skipOperations = 0)
    {
        var hold = new Hold(skipOperations);
        Interlocked.Exchange(ref _next, hold);
        return hold;
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var hold = Volatile.Read(ref _next);
        if (hold == null || Interlocked.Decrement(ref hold.RemainingOperations) >= 0 ||
            !ReferenceEquals(Interlocked.CompareExchange(ref _next, null, hold), hold))
        {
            hold = null;
        }
        if (FailNext)
        {
            FailNext = false;
            return Task.FromException<T>(new InvalidOperationException("Controlled service failure"));
        }
        return Task.Run(async () =>
        {
            // Deliberately ignore cancellation for a held result: identity checks must still protect the UI.
            var result = await operation(hold == null ? cancellationToken : CancellationToken.None);
            if (hold != null)
            {
                hold.Started.TrySetResult();
                await hold.Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            return result;
        });
    }

    internal sealed class Hold(int skipOperations)
    {
        internal int RemainingOperations = skipOperations;
        public TaskCompletionSource Started
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
