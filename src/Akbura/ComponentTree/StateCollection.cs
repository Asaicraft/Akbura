using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.ComponentTree;

/// <summary>
/// Stores component states, hook aliases and cached snapshots by stable declaration identity.
/// </summary>
public sealed class StateCollection
{
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private Dictionary<string, State> _hookStates = new(StringComparer.Ordinal);
    private Dictionary<string, State>? _stagedHookStates;
    private ImmutableArray<State> _snapshot;
    private ImmutableArray<State> _previousSnapshot;
    private ImmutableArray<State> _hotReloadSnapshot;

    internal bool HasPendingReconciliation { get; private set; }

    internal ImmutableArray<State> PreviousSnapshotForHotReload => _hotReloadSnapshot;

    /// <summary>
    /// Gets an existing state or creates it on first access.
    /// </summary>
    /// <param name="key">
    /// The stable identity of the state declaration.
    /// </param>
    /// <param name="factory">
    /// Creates the state when its key is not registered.
    /// </param>
    public State<T> GetOrCreate<T>(string key, Func<State<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_states.TryGetValue(key, out var existing))
        {
            return existing as State<T> ?? throw new InvalidOperationException(
                $"State '{key}' has value type '{existing.ValueType}', not '{typeof(T)}'.");
        }

        var state = factory() ?? throw new InvalidOperationException(
            $"State factory for '{key}' returned null.");
        _states.Add(key, state);
        return state;
    }

    /// <summary>
    /// Gets the cached ordered state snapshot, creating it on first access.
    /// </summary>
    public ImmutableArray<State> GetSnapshot(Func<ImmutableArray<State>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (_snapshot.IsDefault)
        {
            var snapshot = factory();
            if (snapshot.IsDefault)
            {
                throw new InvalidOperationException("The state snapshot factory returned a default array.");
            }

            _snapshot = snapshot;
        }

        return _snapshot;
    }

    /// <summary>
    /// Begins staging hook aliases without changing their committed bindings.
    /// </summary>
    public void BeginFrame()
    {
        if (_stagedHookStates != null)
        {
            throw new InvalidOperationException("A state frame is already active.");
        }

        _previousSnapshot = _snapshot;
        _stagedHookStates = new Dictionary<string, State>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the current hook alias, or null if it has not been bound.
    /// </summary>
    public State<T>? GetBoundOrNull<T>(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        State? state;
        if (_stagedHookStates?.TryGetValue(key, out state) != true &&
            !_hookStates.TryGetValue(key, out state))
        {
            return null;
        }

        return state as State<T> ?? throw new InvalidOperationException(
            $"Hook state '{key}' has value type '{state!.ValueType}', not '{typeof(T)}'.");
    }

    /// <summary>
    /// Gets a prepared hook alias.
    /// </summary>
    public State<T> GetRequiredHook<T>(string key) => GetBoundOrNull<T>(key) ??
        throw new InvalidOperationException("The composed hook state is not prepared for this frame.");

    /// <summary>
    /// Stages an alias owned by the active hook frame.
    /// </summary>
    public void StageHook<T>(string key, State<T> state)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(state);
        var staged = _stagedHookStates ?? throw new InvalidOperationException("No state frame is active.");
        var previous = GetBoundOrNull<T>(key);
        staged.Add(key, state);
        if (!ReferenceEquals(previous, state))
        {
            _snapshot = default;
        }
    }

    /// <summary>
    /// Commits the active frame's aliases and snapshot.
    /// </summary>
    public void CommitFrame()
    {
        var staged = _stagedHookStates ?? throw new InvalidOperationException("No state frame is active.");
        if (_hookStates.Keys.Any(key => !staged.ContainsKey(key)))
        {
            _snapshot = default;
        }

        _hookStates = staged;
        _stagedHookStates = null;
        _previousSnapshot = default;
    }

    /// <summary>
    /// Discards staged aliases and restores the previous cached snapshot.
    /// </summary>
    public void AbortFrame()
    {
        if (_stagedHookStates == null)
        {
            return;
        }

        _stagedHookStates = null;
        _snapshot = _previousSnapshot;
        _previousSnapshot = default;
    }

    /// <summary>
    /// Invalidates the snapshot while retaining it until Hot Reload reconciliation succeeds.
    /// </summary>
    public void InvalidateSnapshotForHotReload()
    {
        if (!HasPendingReconciliation)
        {
            _hotReloadSnapshot = _snapshot;
            HasPendingReconciliation = true;
        }

        _snapshot = default;
    }

    /// <summary>
    /// Clears hook bindings for Hot Reload while retaining ordinary states.
    /// </summary>
    public void ResetHookBindingsForHotReload()
    {
        if (_stagedHookStates != null)
        {
            throw new InvalidOperationException("Hook bindings cannot be reset during an active state frame.");
        }

        InvalidateSnapshotForHotReload();
        _hookStates.Clear();
        _previousSnapshot = default;
    }

    internal void CompleteHotReloadReconciliation()
    {
        _hotReloadSnapshot = default;
        HasPendingReconciliation = false;
    }

    internal void PruneOrdinaryStates(ImmutableArray<State> current, AkburaControl owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var retained = new HashSet<State>(current, ReferenceEqualityComparer.Instance);
        foreach (var entry in _states)
        {
            if (!retained.Contains(entry.Value))
            {
                if (entry.Value.IsOwnedBy(owner))
                {
                    entry.Value.Detach(owner);
                }
                else
                {
                    entry.Value.SuspendResources();
                }
                _states.Remove(entry.Key);
            }
        }
    }

    internal void Remove(State state)
    {
        foreach (var entry in _states)
        {
            if (ReferenceEquals(entry.Value, state))
            {
                _states.Remove(entry.Key);
            }
        }
    }
}
