using Avalonia.Controls;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private Dictionary<RenderSlotKey, ForeachOwnerEntry>? _foreachRegions;
    private ForeachOwnerFrame? _foreachFrame;

    /// <summary>Starts visitation of variable-width contributions in one owner frame.</summary>
    public void BeginForeachFrame()
    {
        if (_foreachFrame != null)
        {
            throw new InvalidOperationException("A foreach owner frame is already pending.");
        }

        _foreachFrame = new ForeachOwnerFrame();
    }

    /// <summary>Gets a region by retained owner identity and a stable declaration slot.</summary>
    public AkburaForeachRegion<TItem, TChild> GetForeachRegion<TItem, TChild>(
        int ownerLocalId, string slot, Action invalidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(invalidate);
        var owner = GetNode(ownerLocalId);
        if (owner?.Instance == null)
        {
            throw new InvalidOperationException("An inactive render node cannot own a foreach contribution.");
        }

        var key = new RenderSlotKey(owner.NodeId, slot);
        var frame = _foreachFrame ??= new ForeachOwnerFrame();
        if (frame.Entries.TryGetValue(key, out var visited))
        {
            return visited.Region is AkburaForeachRegion<TItem, TChild> pending ? pending :
                throw new InvalidOperationException("One foreach declaration has conflicting item or child types.");
        }

        ForeachOwnerEntry entry;
        if (_foreachRegions != null && _foreachRegions.TryGetValue(key, out var existing) &&
            existing.Region is AkburaForeachRegion<TItem, TChild>)
        {
            entry = existing;
        }
        else
        {
            entry = new ForeachOwnerEntry(this, owner.Instance as Control,
                new AkburaForeachRegion<TItem, TChild>(invalidate));
        }

        frame.Entries.Add(key, entry);
        var region = (AkburaForeachRegion<TItem, TChild>)entry.Region;
        if (IsApplyingSourceRevision)
        {
            region.Invalidate();
        }

        return region;
    }

    /// <summary>Acknowledges regions after every shared owner collection has reconciled successfully.</summary>
    public void CompleteForeachFrame()
    {
        if (_foreachFrame is not { } frame)
        {
            return;
        }

        if (_pendingRevision != null)
        {
            throw new InvalidOperationException("A foreach frame cannot commit before its render revision.");
        }

        var previous = _foreachRegions;
        _foreachRegions = frame.Entries;
        _foreachFrame = null;
        List<Exception>? failures = null;
        foreach (var entry in frame.Entries.Values)
        {
            RunForeachCleanup(entry.Region.Commit, ref failures);
            if (entry.Detached)
            {
                RunForeachCleanup(entry.Region.Suspend, ref failures);
            }
        }

        if (previous != null)
        {
            foreach (var pair in previous)
            {
                if (!frame.Entries.TryGetValue(pair.Key, out var retained) || !ReferenceEquals(retained, pair.Value))
                {
                    RunForeachCleanup(pair.Value.Dispose, ref failures);
                }
            }
        }

        ThrowForeachCleanupFailures(failures);
    }

    /// <summary>Discards candidates without acknowledging collection packets or releasing applied regions.</summary>
    public void AbortForeachFrame()
    {
        if (_foreachFrame is not { } frame)
        {
            return;
        }

        _foreachFrame = null;
        List<Exception>? failures = null;
        foreach (var pair in frame.Entries)
        {
            RunForeachCleanup(pair.Value.Region.Abort, ref failures);
            if (_foreachRegions == null || !_foreachRegions.TryGetValue(pair.Key, out var applied) ||
                !ReferenceEquals(applied, pair.Value))
            {
                RunForeachCleanup(pair.Value.Dispose, ref failures);
            }
        }

        ThrowForeachCleanupFailures(failures);
    }

    public void AbortForeachFrame(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        try
        {
            AbortForeachFrame();
        }
        catch (Exception rollback)
        {
            throw new AggregateException("The owner update and its foreach rollback failed.", failure, rollback);
        }
    }

    /// <summary>Suspends source leases without removing retained occurrence nodes.</summary>
    public void SuspendForeachRegions()
    {
        AbortForeachFrame();
        if (_foreachRegions != null)
        {
            foreach (var entry in _foreachRegions.Values)
            {
                entry.Region.Suspend();
            }
        }
    }

    public void ResumeForeachRegions()
    {
        if (_foreachRegions != null)
        {
            foreach (var entry in _foreachRegions.Values)
            {
                entry.Region.Resume();
            }
        }
    }

    private void ReleaseForeachRegions(ref List<Exception>? failures)
    {
        RunForeachCleanup(AbortForeachFrame, ref failures);
        var regions = _foreachRegions;
        _foreachRegions = null;
        if (regions != null)
        {
            foreach (var entry in regions.Values)
            {
                RunForeachCleanup(entry.Dispose, ref failures);
            }
        }
    }

    private void CompleteForeachRevision()
    {
        // A new source may remove the last loop and therefore stop emitting
        // BeginForeachFrame altogether. Its old contributions are still owned.
        if (_foreachFrame == null && _foreachRegions is { Count: > 0 })
        {
            _foreachFrame = new ForeachOwnerFrame();
        }

        CompleteForeachFrame();
    }

    private void AbortRevisionOwnership(PendingRevision pending)
    {
        if (_foreachFrame == null)
        {
            pending.Abort(throwOnFailure: true);
            return;
        }

        List<Exception>? failures = null;
        RunForeachCleanup(() => pending.Abort(throwOnFailure: true), ref failures);
        RunForeachCleanup(AbortForeachFrame, ref failures);
        ThrowForeachCleanupFailures(failures);
    }

    private static void RunForeachCleanup(Action cleanup, ref List<Exception>? failures)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }

    private static void ThrowForeachCleanupFailures(List<Exception>? failures)
    {
        if (failures != null)
        {
            throw new AggregateException("Foreach owner contributions could not be fully released.", failures);
        }
    }

    private sealed class ForeachOwnerFrame
    {
        public Dictionary<RenderSlotKey, ForeachOwnerEntry> Entries { get; } = [];
    }

    private sealed class ForeachOwnerEntry : IDisposable
    {
        private readonly AkburaRenderState _ownerState;
        private Control? _ownerControl;

        public ForeachOwnerEntry(AkburaRenderState ownerState, Control? ownerControl, IAkburaForeachRegion region)
        {
            _ownerState = ownerState;
            _ownerControl = ownerControl;
            Region = region;
            if (ownerControl != null)
            {
                ownerControl.AttachedToVisualTree += OnAttached;
                ownerControl.DetachedFromVisualTree += OnDetached;
            }
        }

        public IAkburaForeachRegion Region { get; }
        public bool Detached { get; private set; }

        private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
        {
            Detached = false;
            Region.Resume();
        }

        private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
        {
            Detached = true;
            // Native move/reconciliation can detach and reattach the same owner.
            // Never destroy its prepared work in the middle of that transaction.
            if (_ownerState._foreachFrame == null && _ownerState._pendingRevision == null)
            {
                Region.Suspend();
            }
        }

        public void Dispose()
        {
            if (_ownerControl is { } control)
            {
                _ownerControl = null;
                control.AttachedToVisualTree -= OnAttached;
                control.DetachedFromVisualTree -= OnDetached;
            }

            Region.Dispose();
        }
    }
}
