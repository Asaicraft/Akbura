using Avalonia;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    /// <summary>Updates active conditional content while retaining its original CLR baseline.</summary>
    public void ReconcileConditionalClrValue(int ownerLocalId, string slot, object target,
        Type? declaringType, string propertyName, string identity, object? desiredValue)
    {
        if (_pendingRevision != null)
        {
            ReconcileClrValueCore(ownerLocalId, slot, target, declaringType,
                propertyName, identity, desiredValue, compareDesiredValue: true);
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var state = GetAppliedConditionalProperty(ownerLocalId, slot, target);
        if (state is not ClrRenderPropertyState)
        {
            throw new InvalidOperationException("The conditional content slot is not a CLR property.");
        }

        state.UpdateAppliedValue(desiredValue);
    }

    /// <summary>Updates active conditional content without starting a source or activation transaction.</summary>
    public void ReconcileConditionalAvaloniaValue(int ownerLocalId, string slot, AvaloniaObject target,
        AvaloniaProperty property, string identity, object? desiredValue)
    {
        if (_pendingRevision != null)
        {
            ReconcileAvaloniaValueCore(ownerLocalId, slot, target, property,
                identity, desiredValue, compareDesiredValue: true);
            return;
        }

        ArgumentNullException.ThrowIfNull(property);
        var state = GetAppliedConditionalProperty(ownerLocalId, slot, target);
        if (state is not AvaloniaRenderPropertyState)
        {
            throw new InvalidOperationException("The conditional content slot is not an Avalonia property.");
        }

        state.UpdateAppliedValue(desiredValue);
    }

    private RenderPropertyState GetAppliedConditionalProperty(int ownerLocalId, string slot, object target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        var node = GetNode(ownerLocalId);
        if (!ReferenceEquals(node.Instance, target))
        {
            throw new ArgumentException("The content target must be its live render owner.", nameof(target));
        }

        if (!_properties.TryGetValue(new RenderSlotKey(node.NodeId, slot), out var state) || !ReferenceEquals(state.Target, target))
        {
            throw new InvalidOperationException("The conditional content property has not been initialized.");
        }

        return state;
    }
}
