namespace Akbura.LanguageServer.State;

internal sealed class AkburaServerState
{
    private AkburaServerSnapshot _current;

    public AkburaServerState(AkburaServerSnapshot initial)
    {
        _current = initial ??
            throw new ArgumentNullException(nameof(initial));
    }

    public AkburaServerSnapshot Current =>
        Volatile.Read(ref _current);

    public void Publish(AkburaServerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var current = Current;
        if (snapshot.Sequence <= current.Sequence)
        {
            throw new InvalidOperationException(
                $"Server snapshot sequence {snapshot.Sequence} must be " +
                $"newer than {current.Sequence}.");
        }

        Volatile.Write(ref _current, snapshot);
    }
}