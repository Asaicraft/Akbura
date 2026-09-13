using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>
/// Builds the current structural render plan for a generated component.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaRenderPlanBuilder
{
    private readonly List<AkburaRenderNodeDefinition> _definitions = [];
    private readonly List<AkburaRenderConditionalDefinition> _conditionals = [];
    private bool _isCompleted;

    /// <summary>
    /// Gets the number of definitions added to the plan.
    /// </summary>
    public int Count => _definitions.Count;

    /// <summary>Adds a dense conditional definition independently of visual node definitions.</summary>
    public void AddConditional(AkburaRenderConditionalDefinition definition)
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("The render plan has already been completed.");
        }

        if (definition.LocalId != _conditionals.Count || definition.Branches == null)
        {
            throw new ArgumentException("Conditional region identifiers must be dense.", nameof(definition));
        }

        _conditionals.Add(definition);
    }

    internal AkburaRenderConditionalDefinition[] CompleteConditionals()
    {
        foreach (var conditional in _conditionals)
        {
            if ((uint)conditional.OwnerLocalId >= (uint)_definitions.Count ||
                (conditional.ParentRegionId >= 0 &&
                 (uint)conditional.ParentBranchId >=
                 (uint)_conditionals[conditional.ParentRegionId].Branches.Length))
            {
                throw new ArgumentException("A conditional region references an unknown owner or ancestor branch.");
            }

            for (var i = 0; i < conditional.Branches.Length; i++)
            {
                var branch = conditional.Branches[i];
                if (branch.ConditionIdentity == null || string.IsNullOrWhiteSpace(branch.ShapeIdentity) ||
                    branch.ConditionIdentity.Length == 0 && i != conditional.Branches.Length - 1)
                {
                    throw new ArgumentException("A conditional region contains an invalid branch definition.");
                }
            }

            var owner = _definitions[conditional.OwnerLocalId];
            if (!MatchesConditionalAncestorPath(owner.ConditionalRegionId, owner.ConditionalBranchId,
                    conditional.ParentRegionId, conditional.ParentBranchId) ||
                conditional.ParentRegionId >= 0 && !IsDescendantOrSelf(conditional.OwnerLocalId,
                    _conditionals[conditional.ParentRegionId].OwnerLocalId))
            {
                throw new ArgumentException("A conditional region owner must belong to its declared ancestor path.");
            }
        }

        foreach (var definition in _definitions)
        {
            if (definition.ConditionalRegionId >= 0 &&
                ((uint)definition.ConditionalRegionId >= (uint)_conditionals.Count ||
                 (uint)definition.ConditionalBranchId >=
                 (uint)_conditionals[definition.ConditionalRegionId].Branches.Length))
            {
                throw new ArgumentException("A render node references an unknown conditional branch.");
            }

            if (definition.ParentId < 0)
            {
                if (definition.ConditionalRegionId >= 0)
                {
                    throw new ArgumentException("The render root cannot belong to a conditional region.");
                }
                continue;
            }

            var parent = _definitions[definition.ParentId];
            if (!MatchesConditionalAncestorPath(parent.ConditionalRegionId, parent.ConditionalBranchId,
                    definition.ConditionalRegionId, definition.ConditionalBranchId))
            {
                throw new ArgumentException("A render node must belong to its real parent's conditional path.");
            }

            if (definition.ConditionalRegionId >= 0)
            {
                var region = _conditionals[definition.ConditionalRegionId];
                if (!IsDescendantOrSelf(definition.ParentId, region.OwnerLocalId) ||
                    definition.ParentId == region.OwnerLocalId && definition.Slot != region.Slot)
                {
                    throw new ArgumentException("A conditional render node must belong to its region's content destination.");
                }
            }
        }

        return [.. _conditionals];
    }

    private bool MatchesConditionalAncestorPath(int ancestorRegionId, int ancestorBranchId,
        int regionId, int branchId)
    {
        if (ancestorRegionId < 0)
        {
            return true;
        }

        while (regionId >= 0)
        {
            if (regionId == ancestorRegionId)
            {
                return branchId == ancestorBranchId;
            }
            var region = _conditionals[regionId];
            regionId = region.ParentRegionId;
            branchId = region.ParentBranchId;
        }

        return false;
    }

    private bool IsDescendantOrSelf(int localId, int ancestorId)
    {
        while (localId >= 0)
        {
            if (localId == ancestorId)
            {
                return true;
            }
            localId = _definitions[localId].ParentId;
        }

        return false;
    }

    /// <summary>
    /// Adds a node definition in dense local identifier order.
    /// </summary>
    /// <param name="definition">The node definition to add.</param>
    public void Add(AkburaRenderNodeDefinition definition)
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException(
                "The render plan has already been completed.");
        }

        var expectedLocalId = _definitions.Count;
        if (definition.LocalId != expectedLocalId)
        {
            throw new ArgumentException(
                $"Render node local identifier {definition.LocalId} is invalid at " +
                $"position {expectedLocalId}; expected {expectedLocalId}.",
                nameof(definition));
        }

        if (definition.LocalId == 0)
        {
            if (definition.ParentId != -1)
            {
                throw new ArgumentException(
                    "Render node 0 must be the root and use parent identifier -1.",
                    nameof(definition));
            }
        }
        else if (definition.ParentId < 0 ||
            definition.ParentId >= definition.LocalId)
        {
            throw new ArgumentException(
                $"Render node {definition.LocalId} must reference a parent that " +
                "appears earlier in the plan.",
                nameof(definition));
        }

        ValidateDefinition(definition);
        ValidateExplicitKey(definition);
        _definitions.Add(definition);
    }

    internal AkburaRenderNodeDefinition[] Complete()
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException(
                "The render plan has already been completed.");
        }

        _isCompleted = true;
        if (_definitions.Count == 0)
        {
            throw new InvalidOperationException(
                "A render plan must contain one root node.");
        }

        return [.. _definitions];
    }

    private static void ValidateDefinition(
        AkburaRenderNodeDefinition definition)
    {
        if (definition.Slot == null ||
            definition.Type == null ||
            definition.SyntaxIdentity == null)
        {
            throw new ArgumentException(
                "A default render node definition cannot be added to a plan.",
                nameof(definition));
        }
    }

    private void ValidateExplicitKey(
        AkburaRenderNodeDefinition definition)
    {
        if (definition.ExplicitKey == null)
        {
            return;
        }

        for (var index = 0; index < _definitions.Count; index++)
        {
            var existing = _definitions[index];
            if (existing.ParentId != definition.ParentId ||
                existing.ConditionalRegionId != definition.ConditionalRegionId ||
                existing.ConditionalBranchId != definition.ConditionalBranchId ||
                !string.Equals(
                    existing.Slot,
                    definition.Slot,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.ExplicitKey,
                    definition.ExplicitKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            throw new ArgumentException(
                $"Explicit render key '{definition.ExplicitKey}' is ambiguous " +
                $"between positions {existing.LocalId} and {definition.LocalId} " +
                $"in slot '{definition.Slot}'.",
                nameof(definition));
        }
    }
}
