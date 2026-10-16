using Akbura.Markup;
using Avalonia.Controls;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private Dictionary<long, ConditionalNameScope>? _conditionalNameScopes;

    /// <summary>
    /// Attaches a lexical scope once per pending revision, including retained scopes on new roots.
    /// </summary>
    public void ReconcileNameScope(int localId, Control target, INameScope scope)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scope);
        const string slot = "$conditional-name-scope";
        if (_pendingRevision is { } pending &&
            pending.Properties.TryGetValue(new RenderSlotKey(GetNodeId(localId), slot), out var applied))
        {
            if (ReferenceEquals(applied.DesiredValue, scope) && ReferenceEquals(pending.Nodes[localId].Instance, target))
            {
                return;
            }
            throw new InvalidOperationException("A render root cannot attach different lexical scopes in one revision.");
        }
        ReconcileConditionalAvaloniaValue(localId, slot, target, NameScope.NameScopeProperty, "lexical-branch", scope);
    }

    /// <summary>
    /// Gets the native name scope owned by the selected lexical branch.
    /// </summary>
    public INameScope GetConditionalNameScope(int regionId, int branchId, INameScope? parent)
    {
        return GetConditionalNameScopeCore(GetConditionalNameScopeIdentity(regionId, branchId), parent);
    }

    /// <summary>
    /// Reuses a completed lexical scope only while its registrations and parent remain unchanged.
    /// </summary>
    public INameScope GetConditionalNameScope(int regionId, int branchId, INameScope? parent,
        ReadOnlySpan<(string Name, object? Element)> names)
    {
        return GetConditionalNameScopeCore(GetConditionalNameScopeIdentity(regionId, branchId), parent, names, true);
    }

    private long GetConditionalNameScopeIdentity(int regionId, int branchId)
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

        return key;
    }

    /// <summary>
    /// Gets the storage-local base name scope.
    /// </summary>
    public INameScope GetLocalNameScope(INameScope? parent)
    {
        return GetConditionalNameScopeCore(0, parent);
    }

    /// <summary>
    /// Reuses the storage-local scope when the desired registrations and parent are unchanged.
    /// </summary>
    public INameScope GetLocalNameScope(INameScope? parent, ReadOnlySpan<(string Name, object? Element)> names)
    {
        return GetConditionalNameScopeCore(0, parent, names, true);
    }

    /// <summary>
    /// Supplies the current lexical scope to a retained local factory.
    /// </summary>
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

    /// <summary>
    /// Finds the current lexical scope of an actual enclosing render owner.
    /// </summary>
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

    private INameScope GetConditionalNameScopeCore(long key, INameScope? parent,
        ReadOnlySpan<(string Name, object? Element)> names = default, bool compareNames = false)
    {
        var scopes = _conditionalNameScopes ??= [];
        scopes.TryGetValue(key, out var previous);
        var pending = _pendingRevision;
        if (previous != null && (compareNames
            ? previous.Matches(parent, names)
            : pending == null || pending.IsActivation || ReferenceEquals(previous.Revision, pending)))
        {
            if (pending != null)
            {
                (pending.UsedConditionalNameScopes ??= []).Add(previous);
            }
            return previous.Scope!;
        }

        // A source transaction gets a separate immutable lookup object. Old
        // bindings keep their old scope while replacement bindings are prepared.
        var scope = new ConditionalNameScope(pending, parent, compareNames ? names.ToArray() : null);
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
                (pending.IsActivation || ReferenceEquals(entry.Value.Revision, pending) ||
                    pending.UsedConditionalNameScopes?.Contains(entry.Value) == true);
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

    private sealed class ConditionalNameScope(PendingRevision? revision, INameScope? parent,
        (string Name, object? Element)[]? names)
    {
        public NameScope? Scope { get; private set; } = new();

        public PendingRevision? Revision { get; set; } = revision;

        private INameScope? _parent = parent;
        private (string Name, object? Element)[]? _names = names;

        public bool Matches(INameScope? desiredParent, ReadOnlySpan<(string Name, object? Element)> desiredNames)
        {
            if (!ReferenceEquals(_parent, desiredParent) || _names == null || _names.Length != desiredNames.Length)
            {
                return false;
            }
            for (var index = 0; index < desiredNames.Length; index++)
            {
                if (!string.Equals(_names[index].Name, desiredNames[index].Name, StringComparison.Ordinal) ||
                    !ReferenceEquals(_names[index].Element, desiredNames[index].Element))
                {
                    return false;
                }
            }
            return true;
        }

        public void Release()
        {
            Scope = null;
            Revision = null;
            _parent = null;
            _names = null;
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
        public HashSet<ConditionalNameScope>? UsedConditionalNameScopes { get; set; }
    }
}
