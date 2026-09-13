using Akbura.Markup;
using Avalonia.Controls;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private Dictionary<long, ConditionalNameScope>? _conditionalNameScopes;

    /// <summary>Gets the native name scope owned by the selected lexical branch.</summary>
    public INameScope GetConditionalNameScope(int regionId, int branchId, INameScope? parent)
    {
        var regions = _pendingRevision?.ConditionalRegions ?? _conditionalRegions;
        ValidateLocalId(regionId, regions.Length);
        var region = regions[regionId];
        ValidateLocalId(branchId, region.BranchIdentities.Length);
        var key = region.BranchIdentities[branchId];
        if (region.ActiveBranchIdentity != key)
        {
            throw new InvalidOperationException("An inactive branch cannot create a name scope.");
        }

        return GetConditionalNameScopeCore(key, parent);
    }

    /// <summary>Gets the storage-local base name scope.</summary>
    public INameScope GetLocalNameScope(INameScope? parent)
    {
        return GetConditionalNameScopeCore(0, parent);
    }

    /// <summary>Supplies the current lexical scope to a retained local factory.</summary>
    public AkburaRetainedFactoryServiceProvider CreateRetainedFactoryServiceProvider(
        INameScope nativeScope, IServiceProvider fallback)
    {
        ArgumentNullException.ThrowIfNull(nativeScope);
        ArgumentNullException.ThrowIfNull(fallback);

        if (_conditionalNameScopes != null)
        {
            foreach (var entry in _conditionalNameScopes)
            {
                if (ReferenceEquals(entry.Value.Scope, nativeScope))
                {
                    return new AkburaRetainedFactoryServiceProvider(this, entry.Key, fallback);
                }
            }
        }

        throw new ArgumentException("The name scope is not owned by this render state.", nameof(nativeScope));
    }

    internal INameScope? GetRetainedFactoryNameScope(long identity)
    {
        return _conditionalNameScopes != null && _conditionalNameScopes.TryGetValue(identity, out var scope)
            ? scope.Scope
            : null;
    }

    /// <summary>Finds the current lexical scope of an actual enclosing render owner.</summary>
    public INameScope? GetNameScopeForNode(object ownerInstance)
    {
        ArgumentNullException.ThrowIfNull(ownerInstance);
        if (_conditionalNameScopes == null)
        {
            return null;
        }

        var nodes = _pendingRevision?.Nodes ?? _nodes;
        var regions = _pendingRevision?.ConditionalRegions ?? _conditionalRegions;
        foreach (var node in nodes)
        {
            if (!ReferenceEquals(node.Instance, ownerInstance))
            {
                continue;
            }

            var definition = node.Definition;
            if (definition.ConditionalRegionId >= 0)
            {
                var region = regions[definition.ConditionalRegionId];
                var key = region.BranchIdentities[definition.ConditionalBranchId];
                if (region.ActiveBranchIdentity == key && _conditionalNameScopes.TryGetValue(key, out var scope))
                {
                    return scope.Scope;
                }
            }
            return _conditionalNameScopes.TryGetValue(0, out var local) ? local.Scope : null;
        }
        return null;
    }

    private INameScope GetConditionalNameScopeCore(long key, INameScope? parent)
    {
        var scopes = _conditionalNameScopes ??= [];
        scopes.TryGetValue(key, out var previous);
        var pending = _pendingRevision;
        if (previous != null && (pending == null || pending.IsActivation || ReferenceEquals(previous.Revision, pending)))
        {
            return previous.Scope!;
        }

        // A source transaction gets a separate immutable lookup object. Old
        // bindings keep their old scope while replacement bindings are prepared.
        var scope = new ConditionalNameScope(pending);
        scopes[key] = scope;
        if (pending != null)
        {
            pending.TrackMutation(new ConditionalNameScopeMutation(scopes, key, previous, scope));
            if (previous != null)
            {
                (pending.ReplacedConditionalNameScopes ??= []).Add(previous);
            }
        }
        else
        {
            previous?.Release();
        }

        return scope.Scope!;
    }

    private void CompleteConditionalNameScopes(PendingRevision pending)
    {
        if (pending.ReplacedConditionalNameScopes != null)
        {
            foreach (var scope in pending.ReplacedConditionalNameScopes)
            {
                scope.Release();
            }
        }

        if (_conditionalNameScopes == null)
        {
            return;
        }

        List<long>? removed = null;
        foreach (var entry in _conditionalNameScopes)
        {
            var active = entry.Key == 0 &&
                (pending.IsActivation || ReferenceEquals(entry.Value.Revision, pending));
            entry.Value.Revision = null;
            foreach (var region in _conditionalRegions)
            {
                if (region.ActiveBranchIdentity == entry.Key &&
                    IsConditionalAncestorPathActive(_conditionalRegions,
                        region.Definition.ParentRegionId, region.Definition.ParentBranchId))
                {
                    active = true;
                    break;
                }
            }

            if (!active)
            {
                entry.Value.Release();
                (removed ??= []).Add(entry.Key);
            }
        }

        if (removed != null)
        {
            foreach (var key in removed)
            {
                _conditionalNameScopes.Remove(key);
            }
        }
    }

    private void ReleaseConditionalNameScopes()
    {
        if (_conditionalNameScopes != null)
        {
            foreach (var scope in _conditionalNameScopes.Values)
            {
                scope.Release();
            }
            _conditionalNameScopes.Clear();
        }
    }

    private sealed class ConditionalNameScope(PendingRevision? revision)
    {
        public NameScope? Scope { get; private set; } = new();

        public PendingRevision? Revision { get; set; } = revision;

        public void Release()
        {
            Scope = null;
            Revision = null;
        }
    }

    private sealed class ConditionalNameScopeMutation(Dictionary<long, ConditionalNameScope> scopes,
        long key, ConditionalNameScope? previous, ConditionalNameScope current) : RenderRevisionMutation
    {
        public override void Rollback()
        {
            current.Release();
            if (previous == null)
            {
                scopes.Remove(key);
            }
            else
            {
                scopes[key] = previous;
            }
        }
    }

    private sealed partial class PendingRevision
    {
        public List<ConditionalNameScope>? ReplacedConditionalNameScopes { get; set; }
    }
}
