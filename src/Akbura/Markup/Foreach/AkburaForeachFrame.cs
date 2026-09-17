using System.ComponentModel;
using Avalonia.Controls;

namespace Akbura.HotReload;

public enum LoopFlow : byte
{
    Next,
    Continue,
    Break,
}

[Flags]
public enum AkburaForeachDependencies : ushort
{
    None = 0,
    ItemLocal = None,
    UsesSourceIndex = 1 << 0,
    ReadsSourceWideData = 1 << 1,
    ReadsComponentEnvironment = 1 << 2,
    ReadsNotifyingItemProperties = 1 << 3,
    MayContinue = 1 << 4,
    MayBreak = 1 << 5,
    HasCrossIterationState = 1 << 6,
    HasOpaqueEffectsOrReads = 1 << 7,
    KeysReadComponentEnvironment = 1 << 8,
}

internal interface IAkburaForeachRegion : IDisposable
{
    void Commit();
    void Abort();
    void Suspend();
    void Resume();
}

/// <summary>Retains declaration-local renderer ownership for one source occurrence.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaForeachFrame<TItem, TChild> : IDisposable
{
    private readonly Dictionary<int, AkburaRenderState> _states = [];
    private readonly Dictionary<int, RootKey> _stateKeys = [];
    private readonly Dictionary<int, object> _nodes = [];
    private readonly Dictionary<int, IDisposable> _owned = [];
    private readonly Dictionary<int, IAkburaForeachRegion> _regions = [];
    private FrameUpdate? _update;
    private TItem _item;
    private int _index;
    private TChild[] _children = [];
    private bool _disposed;
    private NameScope? _nameScope;

    internal AkburaForeachFrame(long occurrenceToken, TItem item, int index)
    {
        OccurrenceToken = occurrenceToken;
        _item = item;
        _index = index;
    }

    public long OccurrenceToken { get; }
    public TItem Item => _update != null ? _update.Item : _item;
    public int Index => _update?.Index ?? _index;
    public int SourceIndex => Index;
    public bool IsSourceRevision => _update?.TemplateChanged == true;
    public IReadOnlyList<TChild> Children => _update?.Children ?? (IReadOnlyList<TChild>)_children;
    public AkburaRenderState RenderState => GetRenderState(0);
    public bool NameScopeChanged => _update is { NameScope: { } scope } && !ReferenceEquals(scope, _nameScope);

    /// <summary>Shares one native lexical scope across all root declarations in this occurrence.</summary>
    public INameScope GetNameScope(INameScope? parent)
    {
        var update = GetUpdate();
        if (update.NameScope == null)
        {
            update.NameScope = new NameScope();
            // A different lookup object requires existing ElementName bindings
            // to be initialized against the same candidate transaction.
            foreach (var state in _states.Values)
            {
                state.Invalidate();
            }
        }

        return update.NameScope;
    }

    /// <summary>Gets an independent tree only when this root declaration is reached.</summary>
    public AkburaRenderState GetRenderState(int declarationSlot) => GetRenderStateCore(declarationSlot, null);

    public AkburaRenderState GetRenderState<TKey>(int declarationSlot, TKey key,
        IEqualityComparer<TKey>? comparer = null)
    {
        if (key is null)
        {
            throw new InvalidOperationException("A foreach root identity cannot be null.");
        }

        return GetRenderStateCore(declarationSlot, new TypedRootKey<TKey>(key, comparer ?? EqualityComparer<TKey>.Default));
    }

    private AkburaRenderState GetRenderStateCore(int declarationSlot, RootKey? key)
    {
        var update = GetUpdate();
        ValidateSlot(declarationSlot);
        if (update.States.TryGetValue(declarationSlot, out var visited))
        {
            update.StateKeys.TryGetValue(declarationSlot, out var visitedKey);
            if (!SameRootKey(visitedKey, key))
            {
                throw new InvalidOperationException("One foreach root declaration has conflicting identities.");
            }

            return visited;
        }

        _stateKeys.TryGetValue(declarationSlot, out var previousKey);
        var state = SameRootKey(previousKey, key) && _states.TryGetValue(declarationSlot, out var retained)
            ? retained : new AkburaRenderState();
        update.States.Add(declarationSlot, state);
        if (key != null)
        {
            update.StateKeys.Add(declarationSlot, key);
        }

        if (update.TemplateChanged || update.NameScope != null)
        {
            state.Invalidate();
        }

        return state;
    }

    /// <summary>Retains a simple generated node by declaration slot, never source index.</summary>
    public TNode GetOrCreate<TNode>(int declarationSlot, Func<TNode> factory) where TNode : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ValidateSlot(declarationSlot);
        var update = GetUpdate();
        if (update.Nodes.TryGetValue(declarationSlot, out var pending))
        {
            return (TNode)pending;
        }

        if (!_nodes.TryGetValue(declarationSlot, out var existing) || existing.GetType() != typeof(TNode))
        {
            existing = factory() ?? throw new InvalidOperationException("A foreach node factory returned null.");
        }

        update.Nodes.Add(declarationSlot, existing);
        return (TNode)existing;
    }

    public void Emit(TChild child) => GetUpdate().Children.Add(child);

    /// <summary>Registers an explicitly owned resource; source items are never disposed.</summary>
    public void Own(int declarationSlot, IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ValidateSlot(declarationSlot);
        GetUpdate().Owned.Add(declarationSlot, resource);
    }

    public AkburaForeachRegion<TSource, TOutput> GetForeachRegion<TSource, TOutput>(
        int declarationSlot, Action invalidate)
    {
        ValidateSlot(declarationSlot);
        ArgumentNullException.ThrowIfNull(invalidate);
        var update = GetUpdate();
        if (update.Regions.TryGetValue(declarationSlot, out var pending))
        {
            return (AkburaForeachRegion<TSource, TOutput>)pending;
        }

        if (!_regions.TryGetValue(declarationSlot, out var existing) ||
            existing is not AkburaForeachRegion<TSource, TOutput>)
        {
            existing = new AkburaForeachRegion<TSource, TOutput>(invalidate);
        }

        update.Regions.Add(declarationSlot, existing);
        return (AkburaForeachRegion<TSource, TOutput>)existing;
    }

    internal void Begin(TItem item, int index, bool templateChanged)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_update != null)
        {
            throw new InvalidOperationException("A foreach frame update is already pending.");
        }

        _update = new FrameUpdate(item, index, templateChanged);
    }

    internal void UpdateContext(TItem item, int index)
    {
        Begin(item, index, templateChanged: false);
        _update!.ContextOnly = true;
        _update.Children.AddRange(_children);
    }

    internal void Prepare()
    {
        var update = GetUpdate();
        foreach (var state in update.States.Values)
        {
            if (state.HasPendingRevision)
            {
                state.PrepareRevisionCompletion();
            }
        }

        if (update.NameScope is { IsCompleted: false } scope)
        {
            scope.Complete();
        }
    }

    internal void Commit()
    {
        if (_update is not { } update)
        {
            return;
        }

        _item = update.Item;
        _index = update.Index;
        if (update.ContextOnly)
        {
            _update = null;
            return;
        }

        _children = [.. update.Children];
        _update = null;
        List<Exception>? failures = null;
        foreach (var state in update.States.Values)
        {
            if (state.HasPendingRevision)
            {
                TryCleanup(state.CompleteRevision, ref failures);
            }
        }

        foreach (var pair in _states)
        {
            if (!update.States.TryGetValue(pair.Key, out var state) || !ReferenceEquals(pair.Value, state))
            {
                TryCleanup(pair.Value.Dispose, ref failures);
            }
        }

        _states.Clear();
        foreach (var pair in update.States)
        {
            _states.Add(pair.Key, pair.Value);
        }
        _stateKeys.Clear();
        foreach (var pair in update.StateKeys)
        {
            _stateKeys.Add(pair.Key, pair.Value);
        }
        _nameScope = update.NameScope;

        ReplaceOwned(_owned, update.Owned, ref failures);
        foreach (var region in update.Regions.Values)
        {
            TryCleanup(region.Commit, ref failures);
        }

        ReplaceOwned(_regions, update.Regions, ref failures);
        _nodes.Clear();
        foreach (var pair in update.Nodes)
        {
            _nodes.Add(pair.Key, pair.Value);
        }

        ThrowCleanupFailures(failures);
    }

    internal void Abort()
    {
        if (_update is not { } update)
        {
            return;
        }

        _update = null;
        List<Exception>? failures = null;
        foreach (var state in update.States.Values)
        {
            TryCleanup(state.AbortRevision, ref failures);
        }

        foreach (var pair in update.States)
        {
            if (!_states.TryGetValue(pair.Key, out var previous) || !ReferenceEquals(previous, pair.Value))
            {
                TryCleanup(pair.Value.Dispose, ref failures);
            }
        }

        ReleaseNewOwned(_owned, update.Owned, ref failures);
        foreach (var region in update.Regions.Values)
        {
            TryCleanup(region.Abort, ref failures);
        }

        ReleaseNewOwned(_regions, update.Regions, ref failures);
        ThrowCleanupFailures(failures);
    }

    internal void Suspend()
    {
        foreach (var state in _states.Values)
        {
            state.SuspendForeachRegions();
        }

        foreach (var region in _regions.Values)
        {
            region.Suspend();
        }
    }

    internal void Resume()
    {
        foreach (var state in _states.Values)
        {
            state.ResumeForeachRegions();
        }

        foreach (var region in _regions.Values)
        {
            region.Resume();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;
        TryCleanup(Abort, ref failures);
        foreach (var state in _states.Values)
        {
            TryCleanup(state.Dispose, ref failures);
        }

        foreach (var resource in _owned.Values)
        {
            TryCleanup(resource.Dispose, ref failures);
        }

        foreach (var region in _regions.Values)
        {
            TryCleanup(region.Dispose, ref failures);
        }

        _states.Clear();
        _stateKeys.Clear();
        _nodes.Clear();
        _owned.Clear();
        _regions.Clear();
        _children = [];
        _item = default!;
        _nameScope = null;
        ThrowCleanupFailures(failures);
    }

    private FrameUpdate GetUpdate() => _update ??
        throw new InvalidOperationException("This declaration is outside an active foreach item evaluation.");

    private static void ValidateSlot(int slot) => ArgumentOutOfRangeException.ThrowIfNegative(slot);

    private static bool SameRootKey(RootKey? first, RootKey? second) =>
        first == null ? second == null : first.EqualsKey(second);

    private abstract class RootKey
    {
        public abstract bool EqualsKey(RootKey? other);
    }

    private sealed class TypedRootKey<TKey>(TKey value, IEqualityComparer<TKey> comparer) : RootKey
    {
        private TKey Value => value;
        private IEqualityComparer<TKey> Comparer => comparer;
        public override bool EqualsKey(RootKey? other) => other is TypedRootKey<TKey> typed &&
            ReferenceEquals(comparer, typed.Comparer) && comparer.Equals(value, typed.Value);
    }

    private static void ReplaceOwned<TResource>(Dictionary<int, TResource> previous,
        Dictionary<int, TResource> desired, ref List<Exception>? failures) where TResource : IDisposable
    {
        foreach (var pair in previous)
        {
            if (!desired.TryGetValue(pair.Key, out var resource) || !ReferenceEquals(resource, pair.Value))
            {
                TryCleanup(pair.Value.Dispose, ref failures);
            }
        }

        previous.Clear();
        foreach (var pair in desired)
        {
            previous.Add(pair.Key, pair.Value);
        }
    }

    private static void ReleaseNewOwned<TResource>(Dictionary<int, TResource> previous,
        Dictionary<int, TResource> desired, ref List<Exception>? failures) where TResource : IDisposable
    {
        foreach (var pair in desired)
        {
            if (!previous.TryGetValue(pair.Key, out var old) || !ReferenceEquals(old, pair.Value))
            {
                TryCleanup(pair.Value.Dispose, ref failures);
            }
        }
    }

    private static void TryCleanup(Action action, ref List<Exception>? failures)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }

    private static void ThrowCleanupFailures(List<Exception>? failures)
    {
        if (failures != null)
        {
            throw new AggregateException("Foreach frame ownership could not be fully released.", failures);
        }
    }

    private sealed class FrameUpdate(TItem item, int index, bool templateChanged)
    {
        public TItem Item { get; } = item;
        public int Index { get; } = index;
        public bool TemplateChanged { get; } = templateChanged;
        public bool ContextOnly { get; set; }
        public List<TChild> Children { get; } = [];
        public Dictionary<int, AkburaRenderState> States { get; } = [];
        public Dictionary<int, RootKey> StateKeys { get; } = [];
        public NameScope? NameScope { get; set; }
        public Dictionary<int, object> Nodes { get; } = [];
        public Dictionary<int, IDisposable> Owned { get; } = [];
        public Dictionary<int, IAkburaForeachRegion> Regions { get; } = [];
    }
}
