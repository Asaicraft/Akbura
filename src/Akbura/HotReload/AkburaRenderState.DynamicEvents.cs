using Avalonia.Interactivity;
using System.Reflection;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    /// <summary>Refreshes a branch-local event closure without recreating its control.</summary>
    public void RefreshClrEventOperation(int localId, string slot, string identity,
        object target, Type declaringType, string eventName, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        if (!declaringType.IsInstanceOfType(target))
        {
            throw new ArgumentException("The event declaring type must match its target.", nameof(declaringType));
        }

        var eventInfo = declaringType.GetEvent(eventName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new ArgumentException("The event was not found.", nameof(eventName));
        RefreshEventOperation(localId, slot, identity, target, new ClrEventOperation(target, eventInfo, handler));
    }

    /// <summary>Refreshes the current branch's routed-event closure and preserves foreign handlers.</summary>
    public void RefreshRoutedEventOperation(int localId, string slot, string identity,
        Interactive target, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        RefreshEventOperation(localId, slot, identity, target, new RoutedEventOperation(target, routedEvent, handler));
    }

    private void RefreshEventOperation(int localId, string slot, string identity,
        object target, RenderOperationResource resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(target);
        var pending = _pendingRevision == null ? null : GetMutablePendingRevision();
        var node = GetNode(localId);
        if (!ReferenceEquals(node.Instance, target))
        {
            throw new ArgumentException("The event target must be the live render node.", nameof(target));
        }

        var previous = node.ReplaceDynamicOwnedOperation(slot, identity, resource);
        var previousResource = previous?.Resource;
        if (pending != null)
        {
            pending.TrackMutation(new RenderOperationMutation(previousResource, node.GetAppliedOperation(slot)));
        }

        try
        {
            previousResource?.Release();
            resource.Apply();
        }
        catch (Exception exception)
        {
            if (pending != null)
            {
                AbortPendingRevisionAfterFailure(pending, exception);
            }
            else
            {
                resource.Release();
                previousResource?.Apply();
                node.RestoreDynamicOwnedOperation(slot, previous);
            }

            throw;
        }
    }
}
