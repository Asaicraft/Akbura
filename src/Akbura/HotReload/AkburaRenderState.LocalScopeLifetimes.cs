namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private Dictionary<object, List<WeakReference<IDisposable>>>? _localScopeLifetimes;

    /// <summary>Retires a replaced template only after its parent's revision commits.</summary>
    public void DeferLocalRenderScopeDisposal(IDisposable resource, Func<bool> stillDetached)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(stillDetached);
        if (_pendingRevision is not { } pending)
        {
            if (stillDetached())
            {
                resource.Dispose();
            }

            return;
        }

        var disposals = pending.LocalScopeDisposals ??= [];
        foreach (var disposal in disposals)
        {
            if (ReferenceEquals(disposal.Resource, resource))
            {
                return;
            }
        }

        disposals.Add(new DeferredLocalScopeDisposal(resource, stillDetached));
    }

    private void CompleteLocalScopeLifetimes(PendingRevision pending)
    {
        List<Exception>? failures = null;
        try
        {
            ReleaseRemovedLocalScopeLifetimes();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (pending.LocalScopeDisposals is { } disposals)
        {
            foreach (var disposal in disposals)
            {
                try
                {
                    if (disposal.StillDetached())
                    {
                        disposal.Resource.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }

        ThrowLocalScopeCleanupFailures(failures);
    }

    private readonly record struct DeferredLocalScopeDisposal(IDisposable Resource, Func<bool> StillDetached);

    private sealed partial class PendingRevision
    {
        public List<DeferredLocalScopeDisposal>? LocalScopeDisposals { get; set; }
    }

    /// <summary>Links a built local scope to its live owning node, not to a mutable plan ordinal.</summary>
    public void RegisterLocalRenderScopeLifetime(object ownerInstance, IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(ownerInstance);
        ArgumentNullException.ThrowIfNull(lease);
        var nodes = _pendingRevision?.Nodes ?? _nodes;
        var ownerIsLive = false;
        foreach (var node in nodes)
        {
            if (node != null && ReferenceEquals(node.Instance, ownerInstance))
            {
                ownerIsLive = true;
                break;
            }
        }

        if (!ownerIsLive)
        {
            lease.Dispose();
            throw new ArgumentException("A local scope must belong to a live render node.", nameof(ownerInstance));
        }

        var lifetimes = _localScopeLifetimes ??= new(ReferenceEqualityComparer.Instance);
        if (!lifetimes.TryGetValue(ownerInstance, out var leases))
        {
            leases = [];
            lifetimes.Add(ownerInstance, leases);
        }

        for (var i = leases.Count - 1; i >= 0; i--)
        {
            if (!leases[i].TryGetTarget(out var existing))
            {
                leases.RemoveAt(i);
            }
            else if (ReferenceEquals(existing, lease))
            {
                return;
            }
        }

        var reference = new WeakReference<IDisposable>(lease);
        leases.Add(reference);
        _pendingRevision?.TrackMutation(new LocalScopeRegistrationMutation(this, ownerInstance, reference, lease));
    }

    private void ReleaseRemovedLocalScopeLifetimes()
    {
        if (_localScopeLifetimes is not { Count: > 0 } lifetimes)
        {
            return;
        }

        var liveInstances = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var node in _nodes)
        {
            if (node.Instance != null)
            {
                liveInstances.Add(node.Instance);
            }
        }

        List<Exception>? failures = null;
        foreach (var owner in lifetimes.Keys.ToArray())
        {
            if (!liveInstances.Contains(owner))
            {
                var leases = lifetimes[owner];
                lifetimes.Remove(owner);
                ReleaseLocalScopeLeases(leases, ref failures);
            }
        }

        ThrowLocalScopeCleanupFailures(failures);
    }

    private void ReleaseAllLocalScopeLifetimes(ref List<Exception>? failures)
    {
        var lifetimes = _localScopeLifetimes;
        _localScopeLifetimes = null;
        if (lifetimes == null)
        {
            return;
        }

        foreach (var leases in lifetimes.Values)
        {
            ReleaseLocalScopeLeases(leases, ref failures);
        }
    }

    private static void ReleaseLocalScopeLeases(List<WeakReference<IDisposable>> leases,
        ref List<Exception>? failures)
    {
        foreach (var reference in leases)
        {
            if (!reference.TryGetTarget(out var lease))
            {
                continue;
            }

            try
            {
                lease.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
    }

    private static void ThrowLocalScopeCleanupFailures(List<Exception>? failures)
    {
        if (failures != null)
        {
            throw new AggregateException("Removed local render scopes could not be fully released.", failures);
        }
    }

    private sealed class LocalScopeRegistrationMutation(AkburaRenderState state, object owner,
        WeakReference<IDisposable> reference, IDisposable lease) : RenderRevisionMutation
    {
        public override void Rollback()
        {
            if (state._localScopeLifetimes is { } lifetimes && lifetimes.TryGetValue(owner, out var leases))
            {
                leases.Remove(reference);
                if (leases.Count == 0)
                {
                    lifetimes.Remove(owner);
                }
            }

            lease.Dispose();
        }
    }
}
