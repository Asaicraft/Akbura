#if DEBUG
using System.Diagnostics;
using Akbura.Pools;

namespace Akbura.AvaloniaEdit.Infrastructure;

/// <summary>
/// Debug-only, opt-in measurements for tests and the smoke host. No per-token logging.
/// </summary>
internal sealed class EditorPerformance
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _counts = [];
    private readonly Dictionary<string, List<double>> _durations = [];

    internal Func<string, CancellationToken, Task>? BeforeService
    {
        get; set;
    }

    internal Action<string, double?>? StageObserved
    {
        get; init;
    }

    internal void Count(string name, long amount = 1)
    {
        lock (_gate)
        {
            _counts[name] = _counts.GetValueOrDefault(name) + amount;
        }
    }

    internal Measurement Measure(string name)
    {
        if (IsServiceStage(name))
        {
            StageObserved?.Invoke(name, null);
        }

        return new(this, name);
    }

    private static bool IsServiceStage(string name) => name is
        "Analysis" or "Classification" or "Diagnostics" or "Syntax" or "CompletionProvider";

    internal void Record(string name, double milliseconds)
    {
        lock (_gate)
        {
            if (!_durations.TryGetValue(name, out var samples))
            {
                _durations[name] = samples = [];
            }

            if (samples.Count < 4096)
            {
                samples.Add(milliseconds);
            }
        }
    }

    internal object Snapshot()
    {
        lock (_gate)
        {
            var durations = new Dictionary<string, double[]>(_durations.Count);
            foreach (var pair in _durations)
            {
                using var samples = ImmutableArrayBuilder<double>.Rent(pair.Value.Count);
                foreach (var sample in pair.Value)
                {
                    samples.Add(sample);
                }
                durations.Add(pair.Key, samples.ToArray());
            }
            return new
            {
                Counts = new Dictionary<string, long>(_counts),
                Milliseconds = durations,
            };
        }
    }

    internal long CountOf(string name)
    {
        lock (_gate) return _counts.GetValueOrDefault(name);
    }

    internal void Reset()
    {
        lock (_gate) { _counts.Clear(); _durations.Clear(); }
    }

    internal readonly struct Measurement(EditorPerformance owner, string name) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();

        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            owner.Record(name, elapsed);
            if (IsServiceStage(name))
            {
                owner.StageObserved?.Invoke(name, elapsed);
            }
        }
    }
}
#endif
