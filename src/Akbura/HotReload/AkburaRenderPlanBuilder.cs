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
    private bool _isCompleted;

    /// <summary>
    /// Gets the number of definitions added to the plan.
    /// </summary>
    public int Count => _definitions.Count;

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
