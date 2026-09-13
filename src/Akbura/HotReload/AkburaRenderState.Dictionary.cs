using System.Collections;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    /// <summary>Reconciles only entries owned by one generated dictionary content slot.</summary>
    public void ReconcileDictionary<TKey, TValue>(
        int ownerLocalId,
        string slot,
        IDictionary<TKey, TValue> dictionary,
        ReadOnlySpan<KeyValuePair<TKey, TValue>> desiredItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(dictionary);
        var pending = _pendingRevision == null ? null : GetMutablePendingRevision();
        var key = new RenderSlotKey(GetNode(ownerLocalId).NodeId, slot);
        if (pending != null)
        {
            PrepareConditionalCollectionSlot(pending, key);
        }

        var states = pending?.Collections ?? _collections;
        if (pending != null && states.ContainsKey(key))
        {
            throw new InvalidOperationException($"Dictionary slot '{slot}' was reconciled twice in one revision.");
        }

        _collections.TryGetValue(key, out var previous);
        if (previous != null && previous is not RenderDictionaryState<TKey, TValue>)
        {
            throw new InvalidOperationException($"Dictionary slot '{slot}' changed its key or value type.");
        }

        var state = previous as RenderDictionaryState<TKey, TValue> ??
            new RenderDictionaryState<TKey, TValue>(key.OwnerNodeId, slot, dictionary);
        try
        {
            if (pending != null)
            {
                pending.TrackMutation(state.CreateMutation());
            }

            state.Reconciler.Reconcile(dictionary, desiredItems);
            states[key] = new RenderDictionaryState<TKey, TValue>(
                key.OwnerNodeId, slot, dictionary, state.Reconciler, desiredItems.ToArray());
        }
        catch (Exception exception)
        {
            if (pending != null)
            {
                AbortPendingRevisionAfterFailure(pending, exception);
            }

            throw;
        }
    }

    /// <summary>Reconciles an untyped dictionary without taking ownership of foreign entries.</summary>
    public void ReconcileDictionary(
        int ownerLocalId,
        string slot,
        IDictionary dictionary,
        ReadOnlySpan<DictionaryEntry> desiredItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(dictionary);
        var pending = _pendingRevision == null ? null : GetMutablePendingRevision();
        var key = new RenderSlotKey(GetNode(ownerLocalId).NodeId, slot);
        if (pending != null)
        {
            PrepareConditionalCollectionSlot(pending, key);
        }

        var states = pending?.Collections ?? _collections;
        if (pending != null && states.ContainsKey(key))
        {
            throw new InvalidOperationException($"Dictionary slot '{slot}' was reconciled twice in one revision.");
        }

        _collections.TryGetValue(key, out var previous);
        if (previous != null && previous is not UntypedRenderDictionaryState)
        {
            throw new InvalidOperationException($"Dictionary slot '{slot}' changed its contract.");
        }

        var state = previous as UntypedRenderDictionaryState ??
            new UntypedRenderDictionaryState(key.OwnerNodeId, slot, dictionary);
        try
        {
            if (pending != null)
            {
                pending.TrackMutation(state.CreateMutation());
            }

            state.Reconciler.Reconcile(dictionary, desiredItems);
            states[key] = new UntypedRenderDictionaryState(
                key.OwnerNodeId, slot, dictionary, state.Reconciler, desiredItems.ToArray());
        }
        catch (Exception exception)
        {
            if (pending != null)
            {
                AbortPendingRevisionAfterFailure(pending, exception);
            }

            throw;
        }
    }

    private sealed class RenderDictionaryState<TKey, TValue> : RenderCollectionState
    {
        public RenderDictionaryState(long owner, string slot, IDictionary<TKey, TValue> dictionary,
            AkburaRenderDictionaryReconciler<TKey, TValue>? reconciler = null,
            KeyValuePair<TKey, TValue>[]? items = null) : base(owner, slot, -1)
        {
            Dictionary = dictionary;
            Reconciler = reconciler ?? new();
            Items = items ?? [];
        }

        public IDictionary<TKey, TValue> Dictionary { get; }
        public AkburaRenderDictionaryReconciler<TKey, TValue> Reconciler { get; }
        public KeyValuePair<TKey, TValue>[] Items { get; }
        public override int ItemCount => Items.Length;

        public override RenderRevisionMutation CreateMutation() =>
            new DictionaryMutation(() => Reconciler.Reconcile(Dictionary, Items));

        public override RenderCollectionState RemoveOwnedItemsAndCreateEmptyState()
        {
            Reconciler.Clear();
            return new RenderDictionaryState<TKey, TValue>(OwnerNodeId, Slot, Dictionary, Reconciler);
        }
    }

    private sealed class UntypedRenderDictionaryState : RenderCollectionState
    {
        public UntypedRenderDictionaryState(long owner, string slot, IDictionary dictionary,
            AkburaRenderDictionaryReconciler? reconciler = null, DictionaryEntry[]? items = null)
            : base(owner, slot, -1)
        {
            Dictionary = dictionary;
            Reconciler = reconciler ?? new();
            Items = items ?? [];
        }

        public IDictionary Dictionary { get; }
        public AkburaRenderDictionaryReconciler Reconciler { get; }
        public DictionaryEntry[] Items { get; }
        public override int ItemCount => Items.Length;

        public override RenderRevisionMutation CreateMutation() =>
            new DictionaryMutation(() => Reconciler.Reconcile(Dictionary, Items));

        public override RenderCollectionState RemoveOwnedItemsAndCreateEmptyState()
        {
            Reconciler.Clear();
            return new UntypedRenderDictionaryState(OwnerNodeId, Slot, Dictionary, Reconciler);
        }
    }

    private sealed class DictionaryMutation(Action restore) : RenderRevisionMutation
    {
        public override void Rollback() => restore();
    }
}
