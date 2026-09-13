namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private ConditionalRegionState[] _conditionalRegions = [];
    private AkburaRenderConditionalDefinition[] _conditionalDefinitions = [];
    private Func<int, object>? _nodeFactory;
    private long _nextConditionalIdentity = 1;

    /// <summary>Gets whether source migration or conditional activation is pending.</summary>
    public bool HasPendingRevision => _pendingRevision != null;

    /// <summary>Gets whether the pending transaction changes the source declaration.</summary>
    public bool IsApplyingSourceRevision => _pendingRevision is { IsActivation: false };

    /// <summary>Gets the selected current-plan branch ordinal, or <c>-1</c>.</summary>
    public int GetConditionalBranch(int regionId)
    {
        var regions = _pendingRevision?.ConditionalRegions ?? _conditionalRegions;
        ValidateLocalId(regionId, regions.Length);
        var region = regions[regionId];
        return region.ActiveBranchIdentity < 0
            ? -1
            : Array.IndexOf(region.BranchIdentities, region.ActiveBranchIdentity);
    }

    /// <summary>
    /// Selects an alternative after its C# condition has been evaluated. Live nodes
    /// are prepared lazily; the applied branch remains unchanged until commit.
    /// </summary>
    /// <returns>Whether this region requires structural content reconciliation.</returns>
    public bool SelectConditionalBranch(int regionId, int branchId)
    {
        var definitions = _pendingRevision?.ConditionalDefinitions ?? _conditionalDefinitions;
        ValidateLocalId(regionId, definitions.Length);
        ref readonly var definition = ref definitions[regionId];
        if (branchId < -1 || branchId >= definition.Branches.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(branchId));
        }

        var regions = _pendingRevision?.ConditionalRegions ?? _conditionalRegions;
        var region = regions[regionId];
        if (!IsConditionalAncestorPathActive(regions, definition.ParentRegionId, definition.ParentBranchId))
        {
            throw new InvalidOperationException("An inactive conditional ancestor cannot render a child region.");
        }

        var selectedIdentity = branchId < 0 ? -1 : region.BranchIdentities[branchId];
        var changed = region.ActiveBranchIdentity != selectedIdentity;
        var nodes = _pendingRevision?.Nodes ?? _nodes;
        var branchNodes = branchId < 0 ? Array.Empty<int>() : region.BranchNodeIndices[branchId];
        var requiresCreation = false;
        for (var i = 0; i < branchNodes.Length && !requiresCreation; i++)
        {
            requiresCreation = nodes[branchNodes[i]].Instance == null;
        }

        if (!changed && !requiresCreation)
        {
            return false;
        }

        var pending = _pendingRevision ?? BeginConditionalActivation();
        if (pending.IsPreparedForCompletion)
        {
            throw new InvalidOperationException("A prepared render transaction cannot change activation.");
        }

        try
        {
            // Constructors run before removing the old owned branch. Property and
            // resource initialization subsequently run inside the lexical C# branch.
            for (var i = 0; i < branchNodes.Length; i++)
            {
                var localId = branchNodes[i];
                if (pending.Nodes[localId].Instance == null)
                {
                    CreateConditionalNode(pending, localId);
                }
            }

            pending.ConditionalRegions[regionId].ActiveBranchIdentity = selectedIdentity;
            if (changed)
            {
                for (var i = 0; i < pending.Nodes.Length; i++)
                {
                    var node = pending.Nodes[i];
                    if (node.Instance != null && !IsConditionalPathActive(pending, node.Definition))
                    {
                        DeactivateConditionalNode(pending, i);
                    }
                }
            }

            return true;
        }
        catch (Exception failure)
        {
            AbortPendingRevisionAfterFailure(pending, failure);
            throw;
        }
    }

    private static bool IsConditionalAncestorPathActive(ConditionalRegionState[] regions,
        int regionId, int branchId)
    {
        while (regionId >= 0)
        {
            var region = regions[regionId];
            if (region.ActiveBranchIdentity != region.BranchIdentities[branchId])
            {
                return false;
            }
            regionId = region.Definition.ParentRegionId;
            branchId = region.Definition.ParentBranchId;
        }

        return true;
    }

    private PendingRevision BeginConditionalActivation()
    {
        var pending = new PendingRevision(_revision!,
            _nodes.Select(static node => node.Definition).ToArray())
        {
            IsActivation = true,
            ConditionalDefinitions = _conditionalDefinitions,
            ConditionalRegions = _conditionalRegions.Select(static region => region.Clone()).ToArray(),
        };

        for (var i = 0; i < _nodes.Length; i++)
        {
            var old = _nodes[i];
            pending.Nodes[i] = new RenderNodeState(old.NodeId, old.Definition, old.Instance,
                old.GetAppliedOperations(), old.ActivationIdentity);
            pending.Nodes[i].PreserveAppliedOperations();
        }

        foreach (var pair in _collections)
        {
            pending.Collections.Add(pair.Key, pair.Value);
        }

        foreach (var pair in _properties)
        {
            pending.Properties.Add(pair.Key, pair.Value);
        }

        _pendingRevision = pending;
        return pending;
    }

    private static void PrepareConditionalCollectionSlot(PendingRevision pending, RenderSlotKey key)
    {
        if (pending.IsActivation && pending.ActivationCollectionsTouched.Add(key))
        {
            pending.Collections.Remove(key);
        }
    }

    private void CreateConditionalNode(PendingRevision pending, int localId)
    {
        var previous = pending.Nodes[localId];
        var instance = (pending.Factory ?? _nodeFactory)!(localId) ??
            throw new InvalidOperationException($"The render factory returned null for conditional node {localId}.");
        if (instance.GetType() != previous.Definition.Type ||
            pending.Nodes.Any(node => ReferenceEquals(node.Instance, instance)))
        {
            throw new InvalidOperationException("A conditional factory must create a new instance of its declared type.");
        }

        if (instance is System.ComponentModel.ISupportInitialize initializer)
        {
            pending.NewInitializers.Add(new PendingInitializer(initializer));
            initializer.BeginInit();
        }

        pending.Nodes[localId] = new RenderNodeState(previous.NodeId, previous.Definition,
            instance, activationIdentity: previous.ActivationIdentity);
        pending.IsNew[localId] = true;
        pending.ShouldApplyInitialValues[localId] = true;
    }

    private void DeactivateConditionalNode(PendingRevision pending, int localId)
    {
        var node = pending.Nodes[localId];
        pending.Nodes[localId] = new RenderNodeState(_nextNodeId++, node.Definition,
            null!, activationIdentity: node.ActivationIdentity);
        pending.IsNew[localId] = false;
        pending.ShouldApplyInitialValues[localId] = false;
        foreach (var key in pending.Collections.Keys.Where(key => key.OwnerNodeId == node.NodeId).ToArray())
        {
            pending.Collections.Remove(key);
        }

        foreach (var key in pending.Properties.Keys.Where(key => key.OwnerNodeId == node.NodeId).ToArray())
        {
            pending.Properties.Remove(key);
        }
    }

    private static bool IsConditionalPathActive(PendingRevision pending, AkburaRenderNodeDefinition node)
    {
        var regionId = node.ConditionalRegionId;
        var branchId = node.ConditionalBranchId;
        while (regionId >= 0)
        {
            var region = pending.ConditionalRegions[regionId];
            if (region.ActiveBranchIdentity != region.BranchIdentities[branchId])
            {
                return false;
            }

            regionId = region.Definition.ParentRegionId;
            branchId = region.Definition.ParentBranchId;
        }

        return true;
    }

    private string? GetConditionalActivationIdentity(PendingRevision pending, int localId)
    {
        var definition = pending.Definitions[localId];
        if (definition.ConditionalRegionId < 0)
        {
            return null;
        }

        var region = MatchConditionalRegion(pending, definition.ConditionalRegionId);
        return region.Identity + ":" + region.BranchIdentities[definition.ConditionalBranchId];
    }

    private ConditionalRegionState MatchConditionalRegion(PendingRevision pending, int regionId)
    {
        if (pending.ConditionalRegions[regionId] is { } matched)
        {
            return matched;
        }

        var definition = pending.ConditionalDefinitions[regionId];
        var ownerNodeId = pending.Nodes[definition.OwnerLocalId].NodeId;
        var parent = definition.ParentRegionId < 0
            ? null : MatchConditionalRegion(pending, definition.ParentRegionId);
        var parentIdentity = parent?.Identity ?? -1;
        var parentBranchIdentity = parent == null ? -1 : parent.BranchIdentities[definition.ParentBranchId];
        var candidates = _conditionalRegions.Where(old => old.OwnerNodeId == ownerNodeId &&
            old.ParentIdentity == parentIdentity && old.ParentBranchIdentity == parentBranchIdentity &&
            old.Definition.Slot == definition.Slot &&
            !pending.ConditionalRegions.Any(next => next?.Identity == old.Identity)).ToArray();
        var exact = candidates.Where(old => old.Definition.SyntaxIdentity == definition.SyntaxIdentity).ToArray();
        ConditionalRegionState? previous = null;
        if (exact.Length == 1 && pending.ConditionalDefinitions.Count(next =>
                next.OwnerLocalId == definition.OwnerLocalId && next.Slot == definition.Slot &&
                next.ParentRegionId == definition.ParentRegionId &&
                next.ParentBranchId == definition.ParentBranchId &&
                next.SyntaxIdentity == definition.SyntaxIdentity) == 1)
        {
            previous = exact[0];
        }
        else if (candidates.Length == 1 && pending.ConditionalDefinitions.Count(next =>
                     next.OwnerLocalId == definition.OwnerLocalId && next.Slot == definition.Slot &&
                     next.ParentRegionId == definition.ParentRegionId &&
                     next.ParentBranchId == definition.ParentBranchId &&
                     pending.ConditionalRegions[next.LocalId] == null) == 1)
        {
            previous = candidates[0];
        }

        var identities = new long[definition.Branches.Length];
        Array.Fill(identities, -1);
        if (previous != null)
        {
            MatchConditionalBranches(definition, previous, identities, useShape: false);
            MatchConditionalBranches(definition, previous, identities, useShape: true);
        }

        for (var i = 0; i < identities.Length; i++)
        {
            if (identities[i] < 0)
            {
                identities[i] = _nextConditionalIdentity++;
            }
        }

        var result = new ConditionalRegionState(definition, ownerNodeId,
            previous?.Identity ?? _nextConditionalIdentity++, parentIdentity, parentBranchIdentity,
            identities, CreateConditionalBranchNodeIndices(pending.Definitions, regionId, identities.Length),
            previous?.ActiveBranchIdentity ?? -1);
        pending.ConditionalRegions[regionId] = result;
        return result;
    }

    private static int[][] CreateConditionalBranchNodeIndices(AkburaRenderNodeDefinition[] definitions,
        int regionId, int branchCount)
    {
        var builders = new List<int>?[branchCount];
        for (var i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            if (definition.ConditionalRegionId == regionId)
            {
                (builders[definition.ConditionalBranchId] ??= []).Add(i);
            }
        }

        var result = new int[branchCount][];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = builders[i]?.ToArray() ?? [];
        }

        return result;
    }

    private static void MatchConditionalBranches(AkburaRenderConditionalDefinition definition,
        ConditionalRegionState previous, long[] identities, bool useShape)
    {
        for (var i = 0; i < identities.Length; i++)
        {
            if (identities[i] >= 0)
            {
                continue;
            }

            var branch = definition.Branches[i];
            var key = useShape ? branch.ShapeIdentity : branch.ConditionIdentity;
            var newCount = definition.Branches.Where((next, index) => identities[index] < 0 &&
                (useShape ? next.ShapeIdentity : next.ConditionIdentity) == key).Count();
            var oldMatches = previous.Definition.Branches.Select((old, index) => (old, index))
                .Where(pair => !identities.Contains(previous.BranchIdentities[pair.index]) &&
                    (useShape ? pair.old.ShapeIdentity : pair.old.ConditionIdentity) == key &&
                    (pair.old.ConditionIdentity.Length == 0) == (branch.ConditionIdentity.Length == 0))
                .ToArray();
            if (newCount == 1 && oldMatches.Length == 1)
            {
                identities[i] = previous.BranchIdentities[oldMatches[0].index];
            }
        }
    }

    private sealed class ConditionalRegionState(
        AkburaRenderConditionalDefinition definition, long ownerNodeId, long identity,
        long parentIdentity, long parentBranchIdentity, long[] branchIdentities, int[][] branchNodeIndices,
        long activeBranchIdentity)
    {
        public AkburaRenderConditionalDefinition Definition { get; } = definition;
        public long OwnerNodeId { get; } = ownerNodeId;
        public long Identity { get; } = identity;
        public long ParentIdentity { get; } = parentIdentity;
        public long ParentBranchIdentity { get; } = parentBranchIdentity;
        public long[] BranchIdentities { get; } = branchIdentities;
        public int[][] BranchNodeIndices { get; } = branchNodeIndices;
        public long ActiveBranchIdentity { get; set; } = activeBranchIdentity;

        public ConditionalRegionState Clone() => new(Definition, OwnerNodeId, Identity,
            ParentIdentity, ParentBranchIdentity, BranchIdentities, BranchNodeIndices, ActiveBranchIdentity);
    }

    private sealed partial class PendingRevision
    {
        public bool IsActivation { get; init; }
        public Func<int, object>? Factory { get; init; }
        public AkburaRenderConditionalDefinition[] ConditionalDefinitions { get; set; } = [];
        public ConditionalRegionState[] ConditionalRegions { get; set; } = [];
        public HashSet<RenderSlotKey> ActivationCollectionsTouched { get; } = [];
        public HashSet<RenderSlotKey> ActivationPropertiesTouched { get; } = [];
    }
}
