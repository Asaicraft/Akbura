using Avalonia;
using Avalonia.Threading;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Akbura.ComponentTree;

/// <summary>Generated, typed expression evaluation; not a runtime expression interpreter.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ExpressionBinding<T> : IDisposable
{
    private Func<ExpressionBindingContext, T>? _evaluate;
    private Action<T>? _write;
    private readonly ExpressionBindingContext _context;
    private bool _running;
    private bool _dirty;
    private bool _active;
    private bool _ownerActive;
    private bool _visuallySuspended;
    private bool _disposed;
    private IDisposable? _lifetime;
    private bool _hasValue;
    private T? _value;

    public ExpressionBinding() => _context = new ExpressionBindingContext(Invalidate);

    public void Update(Func<ExpressionBindingContext, T> evaluate, Action<T> write)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(write);
        Dispatcher.UIThread.VerifyAccess();
        _evaluate = evaluate;
        _write = write;
        _ownerActive = true;
        if (_visuallySuspended) return;
        _active = true;
        Invalidate();
    }

    public void Resume()
    {
        if (_disposed) return;
        _visuallySuspended = false;
        if (!_ownerActive || _active || _evaluate == null) return;
        _active = true;
        _hasValue = false;
        Invalidate();
    }

    public void Suspend()
    {
        _visuallySuspended = true;
        SuspendEvaluation();
    }

    private void SuspendEvaluation()
    {
        _active = false;
        _dirty = false;
        _hasValue = false;
        _value = default;
        _context.Clear();
    }

    internal void SetLifetime(IDisposable lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (_lifetime != null) throw new InvalidOperationException("The binding already has a lifetime owner.");
        _lifetime = lifetime;
    }

    internal void SuspendOwnership()
    {
        _ownerActive = false;
        SuspendEvaluation();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var lifetime = _lifetime;
        _lifetime = null;
        try
        {
            try { lifetime?.Dispose(); }
            finally { SuspendOwnership(); }
        }
        finally
        {
            _evaluate = null;
            _write = null;
            _value = default;
            _context.Stop();
        }
    }

    private void Invalidate()
    {
        if (!_active || _disposed) return;
        _dirty = true;
        if (_running) return;
        Dispatcher.UIThread.VerifyAccess();
        _running = true;
        try
        {
            var turns = 0;
            while (_dirty && _active && !_disposed)
            {
                if (++turns > 64)
                    throw new InvalidOperationException("An expression binding did not stabilize after 64 synchronous updates.");
                _dirty = false;
                _context.Begin();
                T value;
                try { value = _evaluate!(_context); }
                catch (Exception evaluationError)
                {
                    try { _context.End(success: false); }
                    catch (Exception cleanupError) { throw new AggregateException(evaluationError, cleanupError); }
                    throw;
                }
                _context.End(success: true);
                if (!_active || _disposed) break;
                if (!_hasValue || !EqualityComparer<T>.Default.Equals(_value!, value))
                {
                    _write!(value);
                    if (!_active || _disposed) break;
                    _value = value;
                    _hasValue = true;
                }
            }
        }
        finally { _running = false; }
    }
}

/// <summary>
/// Observes receivers at the actual generated read sites. Getters, indexers,
/// branches and methods are evaluated only by the original C# expression.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ExpressionBindingContext
{
    private readonly Dictionary<ObservationKey, Observation> _observations = new();
    private Action? _invalidate;
    private long _epoch;
    private bool _enabled;

    internal ExpressionBindingContext(Action invalidate) => _invalidate = invalidate;

    public T Property<T>(T receiver, int identity, string property)
    {
        if (receiver is INotifyPropertyChanged source && Observe(source, identity) is { } observation)
        {
            try
            {
                PropertyChangedEventHandler handler = (_, change) =>
                {
                    if (string.IsNullOrEmpty(change.PropertyName) || change.PropertyName == property)
                        observation.Changed();
                };
                observation.Subscription = new DelegateSubscription(() => source.PropertyChanged -= handler);
                source.PropertyChanged += handler;
            }
            catch { Cancel(source, identity, observation); throw; }
        }
        return receiver;
    }

    public T Avalonia<T>(T receiver, int identity, AvaloniaProperty property)
    {
        if (receiver is AvaloniaObject source && Observe(source, identity) is { } observation)
        {
            try { observation.Subscription = StateBindings.ObserveAvaloniaProperty(source, property, observation.Changed); }
            catch { Cancel(source, identity, observation); throw; }
        }
        return receiver;
    }

    public T Collection<T>(T receiver, int identity)
    {
        identity = int.MinValue;
        if (receiver is INotifyCollectionChanged source && Observe(source, identity) is { } observation)
        {
            try
            {
                NotifyCollectionChangedEventHandler handler = (_, _) => observation.Changed();
                observation.Subscription = new DelegateSubscription(() => source.CollectionChanged -= handler);
                source.CollectionChanged += handler;
            }
            catch { Cancel(source, identity, observation); throw; }
        }
        return receiver;
    }

    public T State<T>(State<T> state, int identity)
    {
        identity = int.MinValue + 1;
        if (Observe(state, identity) is { } observation)
        {
            try { observation.Subscription = state.Subscribe(_ => observation.Changed()); }
            catch { Cancel(state, identity, observation); throw; }
        }
        return state.Value;
    }

    internal void Begin()
    {
        _enabled = true;
        _epoch++;
    }

    internal void End(bool success)
    {
        List<ObservationKey>? remove = null;
        foreach (var (key, observation) in _observations)
            if (success ? observation.Seen != _epoch : observation.Created == _epoch)
                (remove ??= []).Add(key);
        if (remove == null) return;
        List<Exception>? failures = null;
        foreach (var key in remove)
        {
            var observation = _observations[key];
            _observations.Remove(key);
            try { observation.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures != null) throw new AggregateException(failures);
    }

    internal void Clear()
    {
        _enabled = false;
        var observations = _observations.Values.ToArray();
        _observations.Clear();
        List<Exception>? failures = null;
        foreach (var observation in observations)
        {
            try { observation.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures != null) throw new AggregateException(failures);
    }

    internal void Stop() => _invalidate = null;

    private Observation? Observe(object source, int identity)
    {
        if (!_enabled || _invalidate == null) return null;
        var key = new ObservationKey(source, identity);
        if (_observations.TryGetValue(key, out var existing))
        {
            existing.Seen = _epoch;
            return null;
        }
        var observation = new Observation(this, _epoch);
        _observations.Add(key, observation);
        return observation;
    }

    private void Cancel(object source, int identity, Observation observation)
    {
        _observations.Remove(new ObservationKey(source, identity));
        observation.Dispose();
    }

    private sealed class Observation : IDisposable
    {
        private readonly WeakReference<ExpressionBindingContext> _context;
        private int _active = 1;
        public Observation(ExpressionBindingContext context, long epoch)
        { _context = new(context); Seen = Created = epoch; }
        public long Seen { get; set; }
        public long Created { get; }
        public IDisposable? Subscription { get; set; }
        public void Changed()
        {
            if (Volatile.Read(ref _active) == 0) return;
            if (Dispatcher.UIThread.CheckAccess()) Deliver();
            else
            {
                var weak = new WeakReference<Observation>(this);
                Dispatcher.UIThread.Post(() => { if (weak.TryGetTarget(out var observation)) observation.Deliver(); });
            }
        }
        private void Deliver()
        {
            if (Volatile.Read(ref _active) == 0) return;
            if (_context.TryGetTarget(out var context)) context._invalidate?.Invoke();
            else Dispose();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _active, 0) == 0) return;
            var subscription = Subscription;
            Subscription = null;
            subscription?.Dispose();
        }
    }

    private readonly struct ObservationKey(object source, int identity) : IEquatable<ObservationKey>
    {
        private object Source { get; } = source;
        private int Identity { get; } = identity;
        public bool Equals(ObservationKey other) => Identity == other.Identity && ReferenceEquals(Source, other.Source);
        public override bool Equals(object? other) => other is ObservationKey key && Equals(key);
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Source), Identity);
    }

    private sealed class DelegateSubscription(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
