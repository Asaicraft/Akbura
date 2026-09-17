using Avalonia.Threading;
using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>A non-visual contribution to its owner's shared collection ledger.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed partial class AkburaForeachRegion<TItem, TChild> : IAkburaForeachRegion
{
    private Action? _invalidate;
    private readonly Action _scheduledInvalidation;
    private SourceAccess? _source;
    private SourceLease? _lease;
    private KeyContract? _keys;
    private object? _templateRevision;
    private object? _environmentRevision;
    private AkburaForeachDependencies _dependencies;
    private bool _sourceSnapshotComplete = true;
    private List<Occurrence> _records = [];
    private TChild[] _children = [];
    private RegionUpdate? _update;
    private long _nextOccurrence;
    private long _nextEpoch;
    private long _childrenVersion;
    private bool _hasRendered;
    private bool _invalidated;
    private bool _suspended;
    private bool _resumeDirty;
    private bool _disposed;
    private bool _invalidationScheduled;

    public AkburaForeachRegion(Action invalidate)
    {
        ArgumentNullException.ThrowIfNull(invalidate);
        _invalidate = invalidate;
        _scheduledInvalidation = InvalidateOwner;
    }

    public IReadOnlyList<TChild> Children => _update?.Children ?? _children;
    public bool ChildrenChanged => _update?.ChildrenChanged == true;
    public long ChildrenVersion => ChildrenChanged ? unchecked(_childrenVersion + 1) : _childrenVersion;
    public bool HasPendingUpdate => _update != null;
    public long SourceEpoch => _update?.Lease?.Epoch ?? _lease?.Epoch ?? 0;

    public IReadOnlyList<TChild> Render(
        IEnumerable<TItem>? source, object templateRevision, AkburaForeachDependencies dependencies,
        Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate, object? environmentRevision = null) =>
        RenderCore(GetSource(source, Identity), templateRevision, dependencies, evaluate, null, environmentRevision);

    public IReadOnlyList<TChild> Render<TKey>(
        IEnumerable<TItem>? source, object templateRevision, AkburaForeachDependencies dependencies,
        Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate, Func<TItem, TKey> keySelector, object keyRevision,
        IEqualityComparer<TKey>? comparer = null, object? environmentRevision = null)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyRevision);
        return RenderCore(GetSource(source, Identity), templateRevision, dependencies, evaluate,
            GetKeyContract(keySelector, keyRevision, comparer), environmentRevision);
    }

    public IReadOnlyList<TChild> Render<TSource>(
        IEnumerable<TSource>? source, Func<TSource, TItem> convert, object templateRevision,
        AkburaForeachDependencies dependencies, Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate,
        object? environmentRevision = null)
    {
        ArgumentNullException.ThrowIfNull(convert);
        return RenderCore(GetSource(source, convert), templateRevision, dependencies, evaluate, null, environmentRevision);
    }

    public IReadOnlyList<TChild> Render<TSource, TKey>(
        IEnumerable<TSource>? source, Func<TSource, TItem> convert, object templateRevision,
        AkburaForeachDependencies dependencies, Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate,
        Func<TItem, TKey> keySelector, object keyRevision, IEqualityComparer<TKey>? comparer = null,
        object? environmentRevision = null)
    {
        ArgumentNullException.ThrowIfNull(convert);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyRevision);
        return RenderCore(GetSource(source, convert), templateRevision, dependencies, evaluate,
            GetKeyContract(keySelector, keyRevision, comparer), environmentRevision);
    }

    public IReadOnlyList<TChild> RenderNonGeneric(
        System.Collections.IEnumerable? source, Func<object?, TItem> convert, object templateRevision,
        AkburaForeachDependencies dependencies, Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate,
        object? environmentRevision = null)
    {
        ArgumentNullException.ThrowIfNull(convert);
        return RenderCore(GetNonGenericSource(source, convert), templateRevision, dependencies,
            evaluate, null, environmentRevision);
    }

    public IReadOnlyList<TChild> RenderNonGeneric<TKey>(
        System.Collections.IEnumerable? source, Func<object?, TItem> convert, object templateRevision,
        AkburaForeachDependencies dependencies, Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate,
        Func<TItem, TKey> keySelector, object keyRevision, IEqualityComparer<TKey>? comparer = null,
        object? environmentRevision = null)
    {
        ArgumentNullException.ThrowIfNull(convert);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyRevision);
        return RenderCore(GetNonGenericSource(source, convert), templateRevision, dependencies,
            evaluate, GetKeyContract(keySelector, keyRevision, comparer), environmentRevision);
    }

    /// <summary>Invalidates generated instructions independently of source collection events.</summary>
    public void Invalidate()
    {
        _invalidated = true;
    }

    private static TItem Identity(TItem value) => value;

    private IReadOnlyList<TChild> RenderCore(
        SourceAccess source,
        object templateRevision,
        AkburaForeachDependencies dependencies,
        Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate,
        KeyContract? keys,
        object? environmentRevision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(templateRevision);
        ArgumentNullException.ThrowIfNull(evaluate);
        if (_update != null)
        {
            throw new InvalidOperationException("A foreach contribution must be committed or aborted before rendering again.");
        }

        if (_suspended)
        {
            return _children;
        }

        var sourceChanged = !ReferenceEquals(source, _source);
        var keyChanged = keys == null ? _keys != null : !keys.HasSameIdentity(_keys);
        var templateChanged = !_hasRendered || _invalidated || !Equals(templateRevision, _templateRevision);
        var dependenciesChanged = _hasRendered && dependencies != _dependencies;

        var environmentChanged = (dependencies & (AkburaForeachDependencies.ReadsComponentEnvironment |
            AkburaForeachDependencies.HasCrossIterationState | AkburaForeachDependencies.HasOpaqueEffectsOrReads)) != 0 &&
            (environmentRevision == null || !Equals(environmentRevision, _environmentRevision));

        var notifier = source.Identity as System.Collections.Specialized.INotifyCollectionChanged;
        var observed = notifier != null;
        var leaseChanged = observed && (_lease == null || sourceChanged);

        var keyEnvironmentChanged = keys != null &&
            (dependencies & AkburaForeachDependencies.KeysReadComponentEnvironment) != 0 &&
            (environmentRevision == null || !Equals(environmentRevision, _environmentRevision));

        var sourceDirty = sourceChanged || keyChanged || keyEnvironmentChanged || leaseChanged ||
            _lease?.NeedsSnapshot == true || _lease?.Packets.Count > 0;

        var itemDirty = HasDirtyItems &&
            (dependencies & AkburaForeachDependencies.ReadsNotifyingItemProperties) != 0;

        var needsUpdate = templateChanged || environmentChanged || dependenciesChanged ||
            sourceDirty || itemDirty || _resumeDirty;

        // Immutable source normally does not need another enumeration when its identity
        // has not changed. The exception is an earlier streaming render stopped by break.
        // In that case _records contain only the reached prefix.
        //
        // Snapshot incompleteness alone must not cause enumeration. If nothing affecting
        // execution changed, the normal no-op fast path must remain intact.
        var replayIncompleteImmutable = !observed &&
            source.IsImmutable &&
            _hasRendered &&
            !_sourceSnapshotComplete &&
            needsUpdate;

        var enumeratePlain = !observed &&
            (!source.IsImmutable || sourceChanged || keyChanged || !_hasRendered || replayIncompleteImmutable);

        if (!needsUpdate && !enumeratePlain)
        {
            return _children;
        }

        var lease = leaseChanged ? new SourceLease(this, notifier!, ++_nextEpoch) : observed ? _lease : null;

        var update = new RegionUpdate(source, keys, lease, lease?.Sequence ?? 0,
            templateRevision, environmentRevision, templateChanged, dependencies,
            _itemSequence, _sourceSnapshotComplete)
        {
            Records = _records.Select(static record => record.Clone()).ToList(),
        };

        _update = update;

        try
        {
            var rebuild = sourceChanged || keyChanged || keyEnvironmentChanged ||
                leaseChanged || lease?.NeedsSnapshot == true;

            if (observed && !rebuild && !ApplyPackets(update))
            {
                rebuild = true;
            }

            if (itemDirty)
            {
                for (var i = 0; i < update.Records.Count; i++)
                {
                    var record = update.Records[i];
                    if (!IsItemDirty(record.Item))
                    {
                        continue;
                    }

                    record.Dirty = true;

                    if (!rebuild && observed && keys != null)
                    {
                        var currentKey = keys.Select(record.Item);
                        if (!keys.Comparer.Equals(record.Key, currentKey))
                        {
                            update.Records[i] = NewRecord(record.Item, currentKey);
                        }
                    }
                }
            }

            var globalDirty = templateChanged || environmentChanged || dependenciesChanged || _resumeDirty ||
                (sourceDirty || itemDirty) &&
                (dependencies & (AkburaForeachDependencies.ReadsSourceWideData |
                    AkburaForeachDependencies.HasCrossIterationState |
                    AkburaForeachDependencies.HasOpaqueEffectsOrReads)) != 0;

            // Plain sources keep streaming semantics. This includes an immutable source
            // whose previous streaming pass stopped at break and now has to be replayed.
            if (!observed && enumeratePlain)
            {
                RenderStreaming(update, evaluate, globalDirty, keyChanged, dependencies);
            }
            else
            {
                if (rebuild)
                {
                    var before = lease?.Sequence ?? 0;
                    var values = source.Enumerate().ToList();

                    if (lease != null && lease.Sequence != before)
                    {
                        lease.NeedsSnapshot = true;
                        throw new InvalidOperationException(
                            "A foreach source changed during its snapshot enumeration.");
                    }

                    update.Sequence = before;
                    update.Records = ReconcileSnapshot(values, keys, keyChanged);

                    // Enumerate().ToList() reached the natural end of the source.
                    // The snapshot is complete even if body evaluation later stops at break.
                    update.SourceSnapshotComplete = true;
                }

                ValidateKeys(update.Records, keys);

                var stopped = false;

                for (var i = 0; i < update.Records.Count; i++)
                {
                    var record = update.Records[i];

                    if (stopped)
                    {
                        record.Frame = null;
                        record.Flow = null;
                        record.Dirty = true;
                        record.Index = i;
                        continue;
                    }

                    var indexDirty = record.Index != i &&
                        (dependencies & AkburaForeachDependencies.UsesSourceIndex) != 0;

                    Evaluate(update, record, i, evaluate, globalDirty || indexDirty);
                    stopped = record.Flow == LoopFlow.Break;
                }
            }

            update.Children = [.. update.Records.Where(static record => record.Frame != null)
            .SelectMany(static record => record.Frame!.Children)];
            update.ChildrenChanged = !SameChildren(_children, update.Children);

            foreach (var frame in update.Frames)
            {
                frame.Prepare();
            }

            return update.Children;
        }
        catch (Exception failure)
        {
            try
            {
                Abort();
            }
            catch (Exception rollback)
            {
                throw new AggregateException("A foreach update and its rollback failed.", failure, rollback);
            }

            throw;
        }
    }

    private void RenderStreaming(
        RegionUpdate update,
        Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate,
        bool dirty,
        bool keyChanged,
        AkburaForeachDependencies dependencies)
    {
        var desired = new List<Occurrence>();
        var oldKeys = !keyChanged && update.Keys != null ? IndexKeys(_records, update.Keys) : null;
        var seen = update.Keys != null ? new HashSet<object>(update.Keys.Comparer) : null;
        var snapshotComplete = true;

        foreach (var item in update.Source.Enumerate())
        {
            var key = update.Keys?.Select(item);

            if (key != null && !seen!.Add(key))
            {
                throw new InvalidOperationException("A foreach iteration key occurs more than once in this region.");
            }

            Occurrence record;

            if (oldKeys != null && oldKeys.TryGetValue(key!, out var keyed))
            {
                record = keyed.Clone();
            }
            else if (!keyChanged && update.Keys == null && desired.Count < _records.Count)
            {
                record = _records[desired.Count].Clone();
            }
            else
            {
                record = NewRecord(item, key);
            }

            record.Dirty |= !SameItem(record.Item, item) || IsItemDirty(item);
            record.Item = item;
            record.Key = key;
            desired.Add(record);

            var index = desired.Count - 1;
            var indexDirty = record.Index != index &&
                (dependencies & AkburaForeachDependencies.UsesSourceIndex) != 0;

            Evaluate(update, record, index, evaluate, dirty || indexDirty);

            if (record.Flow == LoopFlow.Break)
            {
                // Do not probe MoveNext again just to determine whether this happened
                // to be the last item. Reading beyond break would violate streaming semantics.
                snapshotComplete = false;
                break;
            }
        }

        update.Records = desired;
        update.SourceSnapshotComplete = snapshotComplete;
    }

    private List<Occurrence> ReconcileSnapshot(List<TItem> values, KeyContract? keys, bool keyChanged)
    {
        var desired = new List<Occurrence>(values.Count);
        var oldKeys = !keyChanged && keys != null ? IndexKeys(_records, keys) : null;
        for (var i = 0; i < values.Count; i++)
        {
            var item = values[i];
            var key = keys?.Select(item);
            Occurrence record;
            if (oldKeys != null && oldKeys.TryGetValue(key!, out var keyed))
            {
                record = keyed.Clone();
            }
            else if (!keyChanged && keys == null && i < _records.Count)
            {
                record = _records[i].Clone();
            }
            else
            {
                record = NewRecord(item, key);
            }

            record.Dirty |= !SameItem(record.Item, item) || IsItemDirty(item);
            record.Item = item;
            record.Key = key;
            desired.Add(record);
        }

        return desired;
    }

    private static Dictionary<object, Occurrence> IndexKeys(List<Occurrence> records, KeyContract keys)
    {
        var result = new Dictionary<object, Occurrence>(keys.Comparer);
        foreach (var record in records)
        {
            result.Add(record.Key!, record);
        }

        return result;
    }

    private static void ValidateKeys(List<Occurrence> records, KeyContract? keys)
    {
        if (keys == null)
        {
            return;
        }

        var seen = new HashSet<object>(keys.Comparer);
        foreach (var record in records)
        {
            if (!seen.Add(record.Key!))
            {
                throw new InvalidOperationException("A foreach iteration key occurs more than once in this region.");
            }
        }
    }

    private Occurrence NewRecord(TItem item, KeyContract? keys) => NewRecord(item, keys?.Select(item));

    private Occurrence NewRecord(TItem item, object? key) => new(++_nextOccurrence, item, key);

    private static bool SameItem(TItem first, TItem second) => typeof(TItem).IsValueType ?
        EqualityComparer<TItem>.Default.Equals(first, second) : ReferenceEquals(first, second);

    private static bool SameChildren(IReadOnlyList<TChild> first, IReadOnlyList<TChild> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var i = 0; i < first.Count; i++)
        {
            if (!SameChild(first[i], second[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameChild(TChild first, TChild second) => typeof(TChild).IsValueType ?
        EqualityComparer<TChild>.Default.Equals(first, second) : ReferenceEquals(first, second);

    private static void Evaluate(RegionUpdate update, Occurrence record, int index,
        Func<AkburaForeachFrame<TItem, TChild>, LoopFlow> evaluate, bool dirty)
    {
        if (record.Frame != null && record.Flow.HasValue && !record.Dirty && !dirty)
        {
            if (record.Index != index)
            {
                record.Frame.UpdateContext(record.Item, index);
                update.Frames.Add(record.Frame);
            }

            record.Index = index;
            return;
        }

        var frame = record.Frame ??= new(record.Token, record.Item, index);
        frame.Begin(record.Item, index, update.TemplateChanged);
        update.Frames.Add(frame);
        record.Flow = evaluate(frame);
        if (record.Flow is not (LoopFlow.Next or LoopFlow.Continue or LoopFlow.Break))
        {
            throw new InvalidOperationException("A foreach evaluator returned an invalid flow result.");
        }

        record.Index = index;
        record.Dirty = false;
    }

    /// <summary>
    /// Publishes prepared frames only after their shared target collection succeeded.
    /// </summary>
    public void Commit()
    {
        if (_update is not { } update)
        {
            return;
        }

        var previous = _records;
        var previousLease = _lease;

        _records = update.Records;
        _children = update.Children;
        if (update.ChildrenChanged)
        {
            _childrenVersion = unchecked(_childrenVersion + 1);
        }
        _source = update.Source;
        _keys = update.Keys;
        _lease = update.Lease;
        _templateRevision = update.TemplateRevision;
        _environmentRevision = update.EnvironmentRevision;
        _dependencies = update.Dependencies;
        _sourceSnapshotComplete = update.SourceSnapshotComplete;
        _hasRendered = true;
        _invalidated = false;
        _resumeDirty = false;
        _update = null;

        _lease?.Acknowledge(update.Sequence);
        CompleteItemPropertyLeases(update);

        if (!ReferenceEquals(previousLease, _lease))
        {
            previousLease?.Dispose();
        }

        List<Exception>? failures = null;

        foreach (var frame in update.Frames)
        {
            TryCleanup(frame.Commit, ref failures);
        }

        ReleaseRemovedFrames(previous, _records, ref failures);
        ThrowCleanupFailures(failures);
    }

    /// <summary>
    /// Leaves source packets unapplied for retry; it does not undo the user's source mutation.
    /// </summary>
    public void Abort()
    {
        if (_update is not { } update)
        {
            return;
        }

        _update = null;
        List<Exception>? failures = null;
        foreach (var frame in update.Frames)
        {
            TryCleanup(frame.Abort, ref failures);
        }

        var retained = new HashSet<AkburaForeachFrame<TItem, TChild>>(
            _records.Where(static record => record.Frame != null).Select(static record => record.Frame!));
        foreach (var frame in update.Frames)
        {
            if (!retained.Contains(frame))
            {
                TryCleanup(frame.Dispose, ref failures);
            }
        }
        if (!ReferenceEquals(update.Lease, _lease))
        {
            update.Lease?.Dispose();
        }

        ThrowCleanupFailures(failures);
    }

    public void Suspend()
    {
        if (_suspended || _disposed)
        {
            return;
        }

        Abort();
        _suspended = true;
        _lease?.Dispose();
        _lease = null;
        ReleaseItemPropertyLeases();
        foreach (var record in _records)
        {
            record.Frame?.Suspend();
        }
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        _resumeDirty = true;
        foreach (var record in _records)
        {
            record.Frame?.Resume();
        }

        ScheduleInvalidation();
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
        _lease?.Dispose();
        _lease = null;
        ReleaseItemPropertyLeases();
        ReleaseRemovedFrames(_records, [], ref failures);
        _records = [];
        _children = [];
        _childrenVersion = 0;
        _source = null;
        _keys = null;
        _templateRevision = null;
        _environmentRevision = null;
        _invalidate = null;
        ThrowCleanupFailures(failures);
    }

    private void ScheduleInvalidation()
    {
        if (_invalidationScheduled || _disposed || _suspended)
        {
            return;
        }

        _invalidationScheduled = true;
        Dispatcher.UIThread.Post(_scheduledInvalidation);
    }

    private void InvalidateOwner()
    {
        _invalidationScheduled = false;
        if (!_disposed && !_suspended)
        {
            _invalidate?.Invoke();
        }
    }

    private static void ReleaseRemovedFrames(
        List<Occurrence> previous,
        List<Occurrence> desired,
        ref List<Exception>? failures)
    {
        var retained = new HashSet<AkburaForeachFrame<TItem, TChild>>(
            desired.Where(static record => record.Frame != null).Select(static record => record.Frame!));
        foreach (var record in previous)
        {
            if (record.Frame != null && !retained.Contains(record.Frame))
            {
                TryCleanup(record.Frame.Dispose, ref failures);
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
            throw new AggregateException("Foreach ownership could not be fully released.", failures);
        }
    }

    private sealed class Occurrence(long token, TItem item, object? key)
    {
        public long Token { get; } = token;
        public TItem Item { get; set; } = item;
        public object? Key { get; set; } = key;
        public int Index { get; set; } = -1;
        public bool Dirty { get; set; } = true;
        public LoopFlow? Flow { get; set; }
        public AkburaForeachFrame<TItem, TChild>? Frame { get; set; }

        public Occurrence Clone() => new(Token, Item, Key)
        {
            Index = Index,
            Dirty = Dirty,
            Flow = Flow,
            Frame = Frame,
        };
    }

    private sealed class RegionUpdate(
        SourceAccess source,
        KeyContract? keys,
        SourceLease? lease,
        long sequence,
        object templateRevision,
        object? environmentRevision,
        bool templateChanged,
        AkburaForeachDependencies dependencies,
        long itemSequence,
        bool sourceSnapshotComplete)
    {
        public SourceAccess Source { get; } = source;
        public KeyContract? Keys { get; } = keys;
        public SourceLease? Lease { get; } = lease;
        public long Sequence { get; set; } = sequence;
        public object TemplateRevision { get; } = templateRevision;
        public object? EnvironmentRevision { get; } = environmentRevision;
        public bool TemplateChanged { get; } = templateChanged;
        public AkburaForeachDependencies Dependencies { get; } = dependencies;
        public long ItemSequence { get; } = itemSequence;
        public bool SourceSnapshotComplete { get; set; } = sourceSnapshotComplete;
        public bool ChildrenChanged { get; set; }
        public List<Occurrence> Records { get; set; } = [];
        public TChild[] Children { get; set; } = [];
        public List<AkburaForeachFrame<TItem, TChild>> Frames { get; } = [];
    }
}
