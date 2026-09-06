using Microsoft.CodeAnalysis.CSharp;
using System.Threading;

namespace Akbura.BlackSilence;

/// <summary>
/// A best-effort cache owned by one incremental compilation-provider value.
/// Losing the cache, concurrent branches and cancellation cannot change output.
/// </summary>
internal sealed class BlackSilenceProjectState
{
    private readonly object _gate = new();
    private BlackSilenceProjectSnapshot? _snapshot;
    private long _nextVersion;

    public BlackSilenceProjectState(CSharpCompilation compilation)
    {
        CSharpCompilation = compilation;
    }

    public CSharpCompilation CSharpCompilation { get; }

    public BlackSilenceProjectSnapshot? TryGetSnapshot(GeneratorProjectOptions options)
    {
        lock (_gate)
        {
            return _snapshot?.Options == options ? _snapshot : null;
        }
    }

    public long GetNextVersion() => Interlocked.Increment(ref _nextVersion);

    public void Publish(BlackSilenceProjectSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_snapshot == null || snapshot.Version > _snapshot.Version)
            {
                _snapshot = snapshot;
            }
        }
    }
}
