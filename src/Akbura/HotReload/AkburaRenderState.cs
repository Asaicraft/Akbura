using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Akbura.Akcss;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Akbura.HotReload;

/// <summary>
/// Retains live render nodes across generated component revisions.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaRenderState
{
    private const string AkcssOperationSlot = "$akcss";
    private const string AkcssOperationIdentity = "generated-akcss";

    private const string CollectionSynchronizerPrefix =
        "__SynchronizeContentLogicalChildren_";

    private static readonly ConcurrentDictionary<Type, MethodInfo[]>
        s_collectionSynchronizers = [];

    private RenderNodeState[] _nodes = [];
    private Dictionary<RenderSlotKey, RenderCollectionState> _collections = [];
    private Dictionary<RenderSlotKey, RenderPropertyState> _properties = [];
    private PendingRevision? _pendingRevision;
    private string? _revision;
    private long _nextNodeId = 1;

    /// <summary>
    /// Invalidates the applied revision while retaining its live node snapshot.
    /// </summary>
    public void Invalidate()
    {
        if (_pendingRevision != null)
        {
            throw new InvalidOperationException(
                "A render revision is already being applied.");
        }

        _revision = null;
    }

    /// <summary>
    /// Begins a structural revision when it differs from the applied revision.
    /// </summary>
    /// <param name="revision">The stable identity of the generated render plan.</param>
    /// <param name="describe">The fixed callback that describes the current plan.</param>
    /// <param name="factory">The fixed callback that creates a node by current local identifier.</param>
    /// <returns>
    /// <see langword="true"/> when a new revision was prepared; otherwise,
    /// <see langword="false"/> when the revision is already current.
    /// </returns>
    public bool BeginRevision(
        string revision,
        Action<AkburaRenderPlanBuilder> describe,
        Func<int, object> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(factory);

        if (_pendingRevision != null)
        {
            throw new InvalidOperationException(
                "A render revision is already being applied.");
        }

        if (string.Equals(
                _revision,
                revision,
                StringComparison.Ordinal))
        {
            return false;
        }

        var builder = new AkburaRenderPlanBuilder();
        describe(builder);
        var definitions = builder.Complete();
        var pendingRevision = new PendingRevision(
            revision,
            definitions);
        _pendingRevision = pendingRevision;

        try
        {
            MatchAndCreate(pendingRevision, factory);
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }

        return true;
    }

    /// <summary>
    /// Gets the stable runtime identifier associated with a current local identifier.
    /// </summary>
    /// <param name="localId">The dense current-plan identifier.</param>
    /// <returns>The stable runtime node identifier.</returns>
    public long GetNodeId(int localId)
    {
        return GetNode(localId).NodeId;
    }

    /// <summary>
    /// Gets a required live render node by its current local identifier.
    /// </summary>
    /// <typeparam name="T">The required node type.</typeparam>
    /// <param name="localId">The dense current-plan identifier.</param>
    /// <returns>The live node instance.</returns>
    public T GetRequired<T>(int localId)
        where T : class
    {
        var node = GetNode(localId);
        if (node.Instance is T instance)
        {
            return instance;
        }

        throw new InvalidOperationException(
            $"Render node {localId} contains " +
            $"'{node.Instance.GetType().FullName}', not '{typeof(T).FullName}'.");
    }

    /// <summary>
    /// Determines whether a node was created for the pending revision.
    /// </summary>
    /// <param name="localId">The dense current-plan identifier.</param>
    /// <returns><see langword="true"/> when the node is new.</returns>
    public bool IsNew(int localId)
    {
        var pendingRevision = GetPendingRevision();
        ValidateLocalId(localId, pendingRevision.Nodes.Length);
        return pendingRevision.IsNew[localId];
    }

    /// <summary>
    /// Determines whether generated initial values must be applied to a node.
    /// </summary>
    /// <param name="localId">The dense current-plan identifier.</param>
    /// <returns>
    /// <see langword="true"/> for a new node or a retained node whose own
    /// normalized syntax identity changed.
    /// </returns>
    public bool ShouldApplyInitialValues(int localId)
    {
        var pendingRevision = GetPendingRevision();
        ValidateLocalId(localId, pendingRevision.Nodes.Length);
        return pendingRevision.ShouldApplyInitialValues[localId];
    }

    /// <summary>
    /// Registers one generated operation for the pending node revision and
    /// determines whether its declaration changed.
    /// </summary>
    /// <param name="localId">The dense current-plan node identifier.</param>
    /// <param name="slot">The stable operation slot on the node.</param>
    /// <param name="identity">The normalized declaration identity.</param>
    /// <returns>
    /// <see langword="true"/> for a new or changed operation; otherwise,
    /// <see langword="false"/> for an unchanged operation.
    /// </returns>
    public bool ShouldApplyOperation(
        int localId,
        string slot,
        string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidateLocalId(localId, pendingRevision.Nodes.Length);
            return pendingRevision.Nodes[localId]
                .RegisterOperation(slot, identity);
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Registers a generated operation which owns a runtime resource and
    /// determines whether that resource must be replaced for this revision.
    /// </summary>
    public bool ShouldApplyOwnedOperation(
        int localId,
        string slot,
        string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidateLocalId(localId, pendingRevision.Nodes.Length);
            return pendingRevision.Nodes[localId]
                .RegisterOwnedOperation(
                    slot,
                    identity);
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Replaces the generated AKCSS cascade owned by one live render node.
    /// </summary>
    /// <remarks>
    /// The cascade is deliberately replaced for every structural revision so
    /// edits to referenced AKCSS definitions are observed even when the node's
    /// own syntax identity is unchanged.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public void ApplyAkcssStylesOperation(
        int localId,
        object target,
        ImmutableArray<AkcssStyleActivator> styles)
    {
        ArgumentNullException.ThrowIfNull(target);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidatePropertyOwner(
                pendingRevision,
                localId,
                target);

            var node = pendingRevision.Nodes[localId];
            node.RegisterOwnedOperationForReplacement(
                AkcssOperationSlot,
                AkcssOperationIdentity);
            var key = CreateSlotKey(
                pendingRevision,
                localId,
                AkcssOperationSlot);
            var previous = FindAppliedOperation(key)?.Resource as
                AkcssStylesOperation;
            ApplyOwnedOperation(
                pendingRevision,
                localId,
                AkcssOperationSlot,
                new AkcssStylesOperation(
                    target,
                    styles,
                    previous));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Applies an Avalonia binding previously accepted by
    /// <see cref="ShouldApplyOwnedOperation"/>.
    /// </summary>
    public void ApplyBindingOperation(
        int localId,
        string slot,
        AvaloniaObject target,
        AvaloniaProperty property,
        BindingBase binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(binding);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidatePropertyOwner(
                pendingRevision,
                localId,
                target);

            var key = CreateSlotKey(
                pendingRevision,
                localId,
                slot);
            ReleasePreviousProperty(
                pendingRevision,
                key);
            var previousBinding = FindAppliedOperation(key)?.Resource as
                AvaloniaPropertyOperation;
            ApplyOwnedOperation(
                pendingRevision,
                localId,
                slot,
                new AvaloniaBindingOperation(
                    target,
                    property,
                    binding,
                    previousBinding));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Attaches an observable Avalonia property binding previously accepted by
    /// <see cref="ShouldApplyOwnedOperation"/>.
    /// </summary>
    public void ApplyObservableBindingOperation(
        int localId,
        string slot,
        AvaloniaObject target,
        AvaloniaProperty property,
        IObservable<object?> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidatePropertyOwner(
                pendingRevision,
                localId,
                target);

            var key = CreateSlotKey(
                pendingRevision,
                localId,
                slot);
            ReleasePreviousProperty(
                pendingRevision,
                key);
            var previousBinding = FindAppliedOperation(key)?.Resource as
                AvaloniaPropertyOperation;
            ApplyOwnedOperation(
                pendingRevision,
                localId,
                slot,
                new AvaloniaObservableBindingOperation(
                    target,
                    property,
                    source,
                    previousBinding));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Attaches a CLR event handler previously accepted by
    /// <see cref="ShouldApplyOwnedOperation"/>.
    /// </summary>
    public void ApplyClrEventOperation(
        int localId,
        string slot,
        object target,
        Type declaringType,
        string eventName,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(handler);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidatePropertyOwner(
                pendingRevision,
                localId,
                target);
            if (!declaringType.IsInstanceOfType(target))
            {
                throw new ArgumentException(
                    $"Render event target must be assignable to " +
                    $"'{declaringType.FullName}'.",
                    nameof(target));
            }

            var eventInfo = declaringType.GetEvent(
                eventName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly) ??
                throw new ArgumentException(
                    $"CLR event '{declaringType.FullName}.{eventName}' was not found.",
                    nameof(eventName));

            ApplyOwnedOperation(
                pendingRevision,
                localId,
                slot,
                new ClrEventOperation(
                    target,
                    eventInfo,
                    handler));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Attaches an Avalonia routed-event handler previously accepted by
    /// <see cref="ShouldApplyOwnedOperation"/>.
    /// </summary>
    public void ApplyRoutedEventOperation(
        int localId,
        string slot,
        Interactive target,
        RoutedEvent routedEvent,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidatePropertyOwner(
                pendingRevision,
                localId,
                target);
            ApplyOwnedOperation(
                pendingRevision,
                localId,
                slot,
                new RoutedEventOperation(
                    target,
                    routedEvent,
                    handler));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Synchronizes one generated collection slot with its desired live nodes.
    /// </summary>
    /// <typeparam name="T">The collection item type.</typeparam>
    /// <param name="ownerLocalId">The current local identifier of the slot owner.</param>
    /// <param name="slot">The stable semantic collection slot.</param>
    /// <param name="collection">The target live collection.</param>
    /// <param name="desiredItems">The desired generated items in source order.</param>
    public void ReconcileCollection<T>(
        int ownerLocalId,
        string slot,
        IList<T> collection,
        IReadOnlyList<T> desiredItems)
    {
        ReconcileCollection(
            ownerLocalId,
            slot,
            component: null,
            collection,
            desiredItems);
    }

    /// <summary>
    /// Synchronizes generated collection content on an Akbura component using
    /// its statically known generic item type.
    /// </summary>
    /// <typeparam name="T">The collection item type.</typeparam>
    /// <param name="ownerLocalId">The current local identifier of the component.</param>
    /// <param name="slot">The stable semantic collection slot.</param>
    /// <param name="component">The component that owns the content parameter.</param>
    /// <param name="collection">The target live collection.</param>
    /// <param name="desiredItems">The desired generated items in source order.</param>
    public void ReconcileComponentCollection<T>(
        int ownerLocalId,
        string slot,
        AkburaControl component,
        IList<T> collection,
        IReadOnlyList<T> desiredItems)
    {
        ArgumentNullException.ThrowIfNull(component);

        ReconcileCollection(
            ownerLocalId,
            slot,
            component,
            collection,
            desiredItems);
    }

    private void ReconcileCollection<T>(
        int ownerLocalId,
        string slot,
        AkburaControl? component,
        IList<T> collection,
        IReadOnlyList<T> desiredItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(desiredItems);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidateLocalId(
                ownerLocalId,
                pendingRevision.Nodes.Length);

            if (component != null &&
                !ReferenceEquals(
                    pendingRevision.Nodes[ownerLocalId].Instance,
                    component))
            {
                throw new ArgumentException(
                    "The content component must be the render collection owner.",
                    nameof(component));
            }

            ValidateDesiredChildren(
                pendingRevision,
                ownerLocalId,
                slot,
                desiredItems);

            var ownerNodeId = pendingRevision.Nodes[ownerLocalId].NodeId;
            var key = new RenderSlotKey(ownerNodeId, slot);
            if (pendingRevision.Collections.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Render collection slot '{slot}' on node {ownerLocalId} " +
                    "was reconciled more than once in the same revision.");
            }

            IReadOnlyList<T> previousItems = Array.Empty<T>();
            var insertionAnchor = -1;
            if (_collections.TryGetValue(key, out var previousState))
            {
                if (previousState is not RenderCollectionState<T> typedState)
                {
                    throw new InvalidOperationException(
                        $"Render collection slot '{slot}' changed its item type.");
                }

                insertionAnchor = typedState.Anchor;
                if (ReferenceEquals(typedState.Collection, collection))
                {
                    previousItems = typedState.Items;
                }
                else
                {
                    pendingRevision.TrackMutation(
                        typedState.CreateMutation());
                    insertionAnchor = typedState
                        .RemoveOwnedItemsAndCreateEmptyState()
                        .Anchor;
                }
            }

            pendingRevision.TrackMutation(
                new RenderCollectionMutation<T>(
                    collection,
                    component));
            insertionAnchor = AkburaRenderCollectionReconciler.Reconcile(
                collection,
                previousItems,
                desiredItems,
                insertionAnchor);

            if (component != null)
            {
                SynchronizeComponentContentCollections(component);
            }

            pendingRevision.Collections.Add(
                key,
                new RenderCollectionState<T>(
                    ownerNodeId,
                    slot,
                    collection,
                    component,
                    [.. desiredItems],
                    insertionAnchor));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Synchronizes one generated collection slot when its static item type is
    /// unavailable to the generator.
    /// </summary>
    /// <param name="ownerLocalId">The current local identifier of the slot owner.</param>
    /// <param name="slot">The stable semantic collection slot.</param>
    /// <param name="target">The target collection implementing non-generic <see cref="System.Collections.IList"/>.</param>
    /// <param name="desiredItems">The desired generated items in source order.</param>
    public void ReconcileCollection(
        int ownerLocalId,
        string slot,
        object target,
        object[] desiredItems)
    {
        ReconcileCollection(
            ownerLocalId,
            slot,
            component: null,
            target,
            desiredItems);
    }

    /// <summary>
    /// Synchronizes generated collection content on an Akbura component and
    /// keeps its logical children consistent on both commit and rollback.
    /// </summary>
    /// <param name="ownerLocalId">The current local identifier of the component.</param>
    /// <param name="slot">The stable semantic collection slot.</param>
    /// <param name="component">The component that owns the content parameter.</param>
    /// <param name="target">The content collection implementing non-generic <see cref="System.Collections.IList"/>.</param>
    /// <param name="desiredItems">The desired generated items in source order.</param>
    public void ReconcileComponentCollection(
        int ownerLocalId,
        string slot,
        AkburaControl component,
        object target,
        object[] desiredItems)
    {
        ArgumentNullException.ThrowIfNull(component);

        ReconcileCollection(
            ownerLocalId,
            slot,
            component,
            target,
            desiredItems);
    }

    private void ReconcileCollection(
        int ownerLocalId,
        string slot,
        AkburaControl? component,
        object target,
        object[] desiredItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desiredItems);

        if (target is not System.Collections.IList collection)
        {
            throw new ArgumentException(
                $"Render collection target '{target.GetType().FullName}' does " +
                "not implement System.Collections.IList.",
                nameof(target));
        }

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidateLocalId(
                ownerLocalId,
                pendingRevision.Nodes.Length);

            if (component != null &&
                !ReferenceEquals(
                    pendingRevision.Nodes[ownerLocalId].Instance,
                    component))
            {
                throw new ArgumentException(
                    "The content component must be the render collection owner.",
                    nameof(component));
            }

            ValidateDesiredChildren(
                pendingRevision,
                ownerLocalId,
                slot,
                desiredItems);

            var ownerNodeId = pendingRevision.Nodes[ownerLocalId].NodeId;
            var key = new RenderSlotKey(ownerNodeId, slot);
            if (pendingRevision.Collections.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Render collection slot '{slot}' on node {ownerLocalId} " +
                    "was reconciled more than once in the same revision.");
            }

            IReadOnlyList<object> previousItems = Array.Empty<object>();
            var insertionAnchor = -1;
            if (_collections.TryGetValue(key, out var previousState))
            {
                if (previousState is not UntypedRenderCollectionState untypedState)
                {
                    throw new InvalidOperationException(
                        $"Render collection slot '{slot}' changed its item type.");
                }

                insertionAnchor = untypedState.Anchor;
                if (ReferenceEquals(untypedState.Target, target))
                {
                    previousItems = untypedState.Items;
                }
                else
                {
                    pendingRevision.TrackMutation(
                        untypedState.CreateMutation());
                    insertionAnchor = untypedState
                        .RemoveOwnedItemsAndCreateEmptyState()
                        .Anchor;
                }
            }

            pendingRevision.TrackMutation(
                new UntypedRenderCollectionMutation(
                    collection,
                    component));
            insertionAnchor = AkburaRenderCollectionReconciler.Reconcile(
                collection,
                previousItems,
                desiredItems,
                insertionAnchor);

            if (component != null)
            {
                SynchronizeComponentContentCollections(component);
            }

            pendingRevision.Collections.Add(
                key,
                new UntypedRenderCollectionState(
                    ownerNodeId,
                    slot,
                    target,
                    collection,
                    component,
                    [.. desiredItems],
                    insertionAnchor));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Reconciles one generated scalar child written through a CLR property.
    /// </summary>
    /// <param name="ownerLocalId">The current local identifier of the property owner.</param>
    /// <param name="slot">The stable semantic property slot.</param>
    /// <param name="target">The live property owner.</param>
    /// <param name="declaringType">The semantic CLR property receiver type.</param>
    /// <param name="propertyName">The CLR property name.</param>
    /// <param name="desiredValue">The desired generated child.</param>
    public void ReconcileClrProperty(
        int ownerLocalId,
        string slot,
        object target,
        Type? declaringType,
        string propertyName,
        object desiredValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(desiredValue);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidateDesiredPropertyChild(
                pendingRevision,
                ownerLocalId,
                slot,
                target,
                desiredValue);

            ReleasePreviousOwnedOperation(
                pendingRevision,
                CreateSlotKey(
                    pendingRevision,
                    ownerLocalId,
                    slot));

            var property = GetClrProperty(
                target.GetType(),
                declaringType,
                propertyName);
            ReconcileProperty(
                pendingRevision,
                ownerLocalId,
                slot,
                new ClrRenderPropertyState(
                    pendingRevision.Nodes[ownerLocalId].NodeId,
                    slot,
                    target,
                    property,
                    desiredValue));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Reconciles one generated scalar child written through an Avalonia property.
    /// </summary>
    /// <param name="ownerLocalId">The current local identifier of the property owner.</param>
    /// <param name="slot">The stable semantic property slot.</param>
    /// <param name="target">The live property owner.</param>
    /// <param name="property">The Avalonia property receiving the child.</param>
    /// <param name="desiredValue">The desired generated child.</param>
    public void ReconcileAvaloniaProperty(
        int ownerLocalId,
        string slot,
        AvaloniaObject target,
        AvaloniaProperty property,
        object desiredValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(desiredValue);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidateDesiredPropertyChild(
                pendingRevision,
                ownerLocalId,
                slot,
                target,
                desiredValue);

            ReleasePreviousOwnedOperation(
                pendingRevision,
                CreateSlotKey(
                    pendingRevision,
                    ownerLocalId,
                    slot));

            ReconcileProperty(
                pendingRevision,
                ownerLocalId,
                slot,
                new AvaloniaRenderPropertyState(
                    pendingRevision.Nodes[ownerLocalId].NodeId,
                    slot,
                    target,
                    property,
                    desiredValue));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Reconciles one generated constant value written through a CLR property.
    /// </summary>
    /// <param name="ownerLocalId">The current local identifier of the property owner.</param>
    /// <param name="slot">The stable semantic property slot.</param>
    /// <param name="target">The live property owner.</param>
    /// <param name="declaringType">The semantic CLR property receiver type.</param>
    /// <param name="propertyName">The CLR property name.</param>
    /// <param name="identity">The normalized declaration identity.</param>
    /// <param name="desiredValue">The generated value, including <see langword="null"/>.</param>
    public void ReconcileClrValue(
        int ownerLocalId,
        string slot,
        object target,
        Type? declaringType,
        string propertyName,
        string identity,
        object? desiredValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidatePropertyOwner(
                pendingRevision,
                ownerLocalId,
                target);

            ReleasePreviousOwnedOperation(
                pendingRevision,
                CreateSlotKey(
                    pendingRevision,
                    ownerLocalId,
                    slot));

            var property = GetClrProperty(
                target.GetType(),
                declaringType,
                propertyName);
            ReconcileProperty(
                pendingRevision,
                ownerLocalId,
                slot,
                new ClrRenderPropertyState(
                    pendingRevision.Nodes[ownerLocalId].NodeId,
                    slot,
                    target,
                    property,
                    desiredValue,
                    identity));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Reconciles one generated constant value written through an Avalonia property.
    /// </summary>
    /// <param name="ownerLocalId">The current local identifier of the property owner.</param>
    /// <param name="slot">The stable semantic property slot.</param>
    /// <param name="target">The live property owner.</param>
    /// <param name="property">The Avalonia property receiving the value.</param>
    /// <param name="identity">The normalized declaration identity.</param>
    /// <param name="desiredValue">The generated value, including <see langword="null"/>.</param>
    public void ReconcileAvaloniaValue(
        int ownerLocalId,
        string slot,
        AvaloniaObject target,
        AvaloniaProperty property,
        string identity,
        object? desiredValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        var pendingRevision = GetMutablePendingRevision();
        try
        {
            ValidatePropertyOwner(
                pendingRevision,
                ownerLocalId,
                target);

            ReleasePreviousOwnedOperation(
                pendingRevision,
                CreateSlotKey(
                    pendingRevision,
                    ownerLocalId,
                    slot));

            ReconcileProperty(
                pendingRevision,
                ownerLocalId,
                slot,
                new AvaloniaRenderPropertyState(
                    pendingRevision.Nodes[ownerLocalId].NodeId,
                    slot,
                    target,
                    property,
                    desiredValue,
                    identity));
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Applies completion-time cleanup for the pending revision without
    /// publishing it as the applied snapshot.
    /// </summary>
    public void PrepareRevisionCompletion()
    {
        var pendingRevision = GetPendingRevision();
        if (pendingRevision.IsPreparedForCompletion)
        {
            return;
        }

        try
        {
            pendingRevision.ValidateOwnedOperations();
            RemoveOmittedProperties(pendingRevision);
            RemoveOmittedCollections(pendingRevision);
            RemoveOmittedOperations(pendingRevision);
            pendingRevision.EndNewNodes(throwOnFailure: true);
            pendingRevision.IsPreparedForCompletion = true;
        }
        catch (Exception exception)
        {
            AbortPendingRevisionAfterFailure(
                pendingRevision,
                exception);
            throw;
        }
    }

    /// <summary>
    /// Completes the pending revision and makes it the applied snapshot.
    /// </summary>
    public void CompleteRevision()
    {
        PrepareRevisionCompletion();
        var pendingRevision = GetPendingRevision();

        _nodes = pendingRevision.Nodes;
        _collections = pendingRevision.Collections;
        _properties = pendingRevision.Properties;
        _revision = pendingRevision.Revision;
        _pendingRevision = null;
    }

    /// <summary>
    /// Aborts a pending revision without replacing the applied snapshot.
    /// </summary>
    public void AbortRevision()
    {
        var pendingRevision = _pendingRevision;
        if (pendingRevision == null)
        {
            return;
        }

        _pendingRevision = null;
        pendingRevision.Abort(throwOnFailure: true);
    }

    /// <summary>
    /// Aborts a pending revision while preserving the failure that caused the
    /// generated update to stop.
    /// </summary>
    /// <param name="failure">The failure raised by the generated update.</param>
    public void AbortRevision(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var pendingRevision = _pendingRevision;
        if (pendingRevision == null)
        {
            return;
        }

        AbortPendingRevisionAfterFailure(pendingRevision, failure);
    }

    private void ReconcileProperty(
        PendingRevision pendingRevision,
        int ownerLocalId,
        string slot,
        RenderPropertyState desiredState)
    {
        var key = new RenderSlotKey(
            pendingRevision.Nodes[ownerLocalId].NodeId,
            slot);
        if (pendingRevision.ContainsOperation(key))
        {
            throw new InvalidOperationException(
                $"Render slot '{slot}' on node {ownerLocalId} cannot contain " +
                "both a property value and an owned operation.");
        }

        if (pendingRevision.Properties.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Render property slot '{slot}' on node {ownerLocalId} " +
                "was reconciled more than once in the same revision.");
        }

        if (!_properties.TryGetValue(key, out var previousState))
        {
            pendingRevision.TrackMutation(desiredState.CreateMutation());
            desiredState.ApplyDesiredValue();
            pendingRevision.Properties.Add(key, desiredState);
            return;
        }

        if (previousState.Matches(desiredState))
        {
            if (previousState.HasSameDeclaration(desiredState))
            {
                pendingRevision.Properties.Add(key, previousState);
                return;
            }

            pendingRevision.TrackMutation(previousState.CreateMutation());
            previousState.ApplyValue(desiredState.DesiredValue);
            pendingRevision.Properties.Add(
                key,
                previousState.WithDeclaration(desiredState));
            return;
        }

        pendingRevision.TrackMutation(previousState.CreateMutation());
        previousState.RestoreBaseline();
        pendingRevision.TrackMutation(desiredState.CreateMutation());
        desiredState.ApplyDesiredValue();
        pendingRevision.Properties.Add(key, desiredState);
    }

    private static RenderSlotKey CreateSlotKey(
        PendingRevision pendingRevision,
        int localId,
        string slot)
    {
        return new RenderSlotKey(
            pendingRevision.Nodes[localId].NodeId,
            slot);
    }

    private static void ApplyOwnedOperation(
        PendingRevision pendingRevision,
        int localId,
        string slot,
        RenderOperationResource resource)
    {
        var node = pendingRevision.Nodes[localId];
        var previous = node.SetOwnedOperationResource(slot, resource);
        pendingRevision.TrackMutation(
            new RenderOperationMutation(
                previous,
                node.GetAppliedOperation(slot)));
        previous?.Release();
        resource.Apply();
    }

    private void ReleasePreviousProperty(
        PendingRevision pendingRevision,
        RenderSlotKey key)
    {
        if (!_properties.TryGetValue(key, out var previousState) ||
            !pendingRevision.SupersededProperties.Add(key))
        {
            return;
        }

        pendingRevision.TrackMutation(previousState.CreateMutation());
        previousState.RestoreBaseline();
    }

    private void ReleasePreviousOwnedOperation(
        PendingRevision pendingRevision,
        RenderSlotKey key)
    {
        if (!pendingRevision.ReleasedOperations.Add(key))
        {
            return;
        }

        var operation = FindAppliedOperation(key);
        var resource = operation?.Resource;
        if (resource == null || !resource.IsApplied)
        {
            return;
        }

        pendingRevision.TrackMutation(
            new RenderOperationMutation(
                resource,
                replacement: null));
        resource.Release();
    }

    private RenderOperationState? FindAppliedOperation(
        in RenderSlotKey key)
    {
        for (var index = 0; index < _nodes.Length; index++)
        {
            var node = _nodes[index];
            if (node.NodeId == key.OwnerNodeId &&
                node.TryGetAppliedOperation(
                    key.Slot,
                    out var operation))
            {
                return operation;
            }
        }

        return null;
    }

    private void AbortPendingRevisionAfterFailure(
        PendingRevision pendingRevision,
        Exception failure)
    {
        if (!ReferenceEquals(_pendingRevision, pendingRevision))
        {
            return;
        }

        _pendingRevision = null;
        try
        {
            pendingRevision.Abort(throwOnFailure: true);
        }
        catch (Exception rollbackFailure)
        {
            var failures = new List<Exception>
            {
                failure,
            };

            if (rollbackFailure is AggregateException aggregate)
            {
                failures.AddRange(
                    aggregate.Flatten().InnerExceptions);
            }
            else
            {
                failures.Add(rollbackFailure);
            }

            throw new AggregateException(
                "The render revision failed and its rollback did not " +
                "complete successfully.",
                failures);
        }
    }

    private void MatchAndCreate(
        PendingRevision pendingRevision,
        Func<int, object> factory)
    {
        var oldMatched = new bool[_nodes.Length];
        var assignedInstances = new HashSet<object>(
            ReferenceEqualityComparer.Instance);

        for (var parentId = -1;
            parentId < pendingRevision.Definitions.Length;
            parentId++)
        {
            var slots = GetSlots(
                pendingRevision.Definitions,
                parentId);

            for (var slotIndex = 0;
                slotIndex < slots.Count;
                slotIndex++)
            {
                MatchGroup(
                    pendingRevision,
                    parentId,
                    slots[slotIndex],
                    oldMatched,
                    assignedInstances,
                    factory);
            }
        }
    }

    private void MatchGroup(
        PendingRevision pendingRevision,
        int parentId,
        string slot,
        bool[] oldMatched,
        HashSet<object> assignedInstances,
        Func<int, object> factory)
    {
        var newNodeIds = GetNodeIds(
            pendingRevision.Definitions,
            parentId,
            slot);
        if (newNodeIds.Count == 0)
        {
            return;
        }

        long? parentNodeId = parentId < 0
            ? null
            : pendingRevision.Nodes[parentId].NodeId;
        var oldNodeIds = GetOldNodeIds(parentNodeId, slot);

        MatchExplicitKeys(
            pendingRevision,
            newNodeIds,
            oldNodeIds,
            oldMatched,
            assignedInstances);
        MatchExactSyntax(
            pendingRevision,
            newNodeIds,
            oldNodeIds,
            oldMatched,
            assignedInstances);
        MatchCompatibleTypes(
            pendingRevision,
            newNodeIds,
            oldNodeIds,
            oldMatched,
            assignedInstances);
        CreateUnmatchedNodes(
            pendingRevision,
            newNodeIds,
            assignedInstances,
            factory);
    }

    private void MatchExplicitKeys(
        PendingRevision pendingRevision,
        IReadOnlyList<int> newNodeIds,
        IReadOnlyList<int> oldNodeIds,
        bool[] oldMatched,
        HashSet<object> assignedInstances)
    {
        for (var newIndex = 0;
            newIndex < newNodeIds.Count;
            newIndex++)
        {
            var newLocalId = newNodeIds[newIndex];
            ref readonly var definition =
                ref pendingRevision.Definitions[newLocalId];
            if (definition.ExplicitKey == null)
            {
                continue;
            }

            for (var oldIndex = 0;
                oldIndex < oldNodeIds.Count;
                oldIndex++)
            {
                var oldLocalId = oldNodeIds[oldIndex];
                if (oldMatched[oldLocalId])
                {
                    continue;
                }

                var oldNode = _nodes[oldLocalId];
                if (oldNode.Definition.Type != definition.Type ||
                    !string.Equals(
                        oldNode.Definition.ExplicitKey,
                        definition.ExplicitKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                ReuseNode(
                    pendingRevision,
                    newLocalId,
                    oldLocalId,
                    oldMatched,
                    assignedInstances);
                break;
            }
        }
    }

    private void MatchExactSyntax(
        PendingRevision pendingRevision,
        IReadOnlyList<int> newNodeIds,
        IReadOnlyList<int> oldNodeIds,
        bool[] oldMatched,
        HashSet<object> assignedInstances)
    {
        for (var newIndex = 0;
            newIndex < newNodeIds.Count;
            newIndex++)
        {
            var newLocalId = newNodeIds[newIndex];
            ref readonly var definition =
                ref pendingRevision.Definitions[newLocalId];
            if (pendingRevision.Nodes[newLocalId] != null ||
                definition.ExplicitKey != null)
            {
                continue;
            }

            var bestOldLocalId = -1;
            var bestDistance = int.MaxValue;
            var bestPosition = int.MaxValue;

            for (var oldIndex = 0;
                oldIndex < oldNodeIds.Count;
                oldIndex++)
            {
                var oldLocalId = oldNodeIds[oldIndex];
                if (oldMatched[oldLocalId])
                {
                    continue;
                }

                var oldDefinition = _nodes[oldLocalId].Definition;
                if (oldDefinition.ExplicitKey != null ||
                    oldDefinition.Type != definition.Type ||
                    !string.Equals(
                        oldDefinition.SyntaxIdentity,
                        definition.SyntaxIdentity,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = Math.Abs(oldIndex - newIndex);
                if (distance < bestDistance ||
                    distance == bestDistance &&
                    oldIndex < bestPosition)
                {
                    bestOldLocalId = oldLocalId;
                    bestDistance = distance;
                    bestPosition = oldIndex;
                }
            }

            if (bestOldLocalId >= 0)
            {
                ReuseNode(
                    pendingRevision,
                    newLocalId,
                    bestOldLocalId,
                    oldMatched,
                    assignedInstances);
            }
        }
    }

    private void MatchCompatibleTypes(
        PendingRevision pendingRevision,
        IReadOnlyList<int> newNodeIds,
        IReadOnlyList<int> oldNodeIds,
        bool[] oldMatched,
        HashSet<object> assignedInstances)
    {
        for (var newIndex = 0;
            newIndex < newNodeIds.Count;
            newIndex++)
        {
            var newLocalId = newNodeIds[newIndex];
            ref readonly var definition =
                ref pendingRevision.Definitions[newLocalId];
            if (pendingRevision.Nodes[newLocalId] != null ||
                definition.ExplicitKey != null)
            {
                continue;
            }

            for (var oldIndex = 0;
                oldIndex < oldNodeIds.Count;
                oldIndex++)
            {
                var oldLocalId = oldNodeIds[oldIndex];
                if (oldMatched[oldLocalId])
                {
                    continue;
                }

                var oldDefinition = _nodes[oldLocalId].Definition;
                if (oldDefinition.ExplicitKey != null ||
                    oldDefinition.Type != definition.Type)
                {
                    continue;
                }

                ReuseNode(
                    pendingRevision,
                    newLocalId,
                    oldLocalId,
                    oldMatched,
                    assignedInstances);
                break;
            }
        }
    }

    private void ReuseNode(
        PendingRevision pendingRevision,
        int newLocalId,
        int oldLocalId,
        bool[] oldMatched,
        HashSet<object> assignedInstances)
    {
        var oldNode = _nodes[oldLocalId];
        if (!assignedInstances.Add(oldNode.Instance))
        {
            throw new InvalidOperationException(
                $"Render node instance at old position {oldLocalId} is ambiguous.");
        }

        ref readonly var definition =
            ref pendingRevision.Definitions[newLocalId];
        pendingRevision.Nodes[newLocalId] = new RenderNodeState(
            oldNode.NodeId,
            definition,
            oldNode.Instance,
            oldNode.GetAppliedOperations());
        pendingRevision.ShouldApplyInitialValues[newLocalId] =
            !string.Equals(
                oldNode.Definition.SyntaxIdentity,
                definition.SyntaxIdentity,
                StringComparison.Ordinal);
        oldMatched[oldLocalId] = true;
    }

    private void CreateUnmatchedNodes(
        PendingRevision pendingRevision,
        IReadOnlyList<int> newNodeIds,
        HashSet<object> assignedInstances,
        Func<int, object> factory)
    {
        for (var index = 0; index < newNodeIds.Count; index++)
        {
            var localId = newNodeIds[index];
            if (pendingRevision.Nodes[localId] != null)
            {
                continue;
            }

            ref readonly var definition =
                ref pendingRevision.Definitions[localId];
            var instance = factory(localId) ??
                throw new InvalidOperationException(
                    $"The render node factory returned null for node {localId}.");

            if (instance.GetType() != definition.Type)
            {
                throw new InvalidOperationException(
                    $"The render node factory created " +
                    $"'{instance.GetType().FullName}' for node {localId}; " +
                    $"expected exact type '{definition.Type.FullName}'.");
            }

            if (!assignedInstances.Add(instance))
            {
                throw new InvalidOperationException(
                    $"The render node factory reused an instance at position {localId}.");
            }

            var initializer = instance as ISupportInitialize;
            if (initializer != null)
            {
                pendingRevision.NewInitializers.Add(
                    new PendingInitializer(initializer));
            }

            initializer?.BeginInit();

            pendingRevision.Nodes[localId] = new RenderNodeState(
                _nextNodeId++,
                definition,
                instance);
            pendingRevision.IsNew[localId] = true;
            pendingRevision.ShouldApplyInitialValues[localId] = true;
        }
    }

    private static List<string> GetSlots(
        IReadOnlyList<AkburaRenderNodeDefinition> definitions,
        int parentId)
    {
        var slots = new List<string>();
        var uniqueSlots = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            if (definition.ParentId == parentId &&
                uniqueSlots.Add(definition.Slot))
            {
                slots.Add(definition.Slot);
            }
        }

        return slots;
    }

    private static List<int> GetNodeIds(
        IReadOnlyList<AkburaRenderNodeDefinition> definitions,
        int parentId,
        string slot)
    {
        var nodeIds = new List<int>();
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            if (definition.ParentId == parentId &&
                string.Equals(
                    definition.Slot,
                    slot,
                    StringComparison.Ordinal))
            {
                nodeIds.Add(definition.LocalId);
            }
        }

        return nodeIds;
    }

    private List<int> GetOldNodeIds(
        long? parentNodeId,
        string slot)
    {
        var nodeIds = new List<int>();
        for (var index = 0; index < _nodes.Length; index++)
        {
            var node = _nodes[index];
            long? oldParentNodeId = node.Definition.ParentId < 0
                ? null
                : _nodes[node.Definition.ParentId].NodeId;

            if (oldParentNodeId == parentNodeId &&
                string.Equals(
                    node.Definition.Slot,
                    slot,
                    StringComparison.Ordinal))
            {
                nodeIds.Add(index);
            }
        }

        return nodeIds;
    }

    private static void ValidateDesiredChildren<T>(
        PendingRevision pendingRevision,
        int ownerLocalId,
        string slot,
        IReadOnlyList<T> desiredItems)
    {
        ValidateLocalId(ownerLocalId, pendingRevision.Nodes.Length);

        var expectedChildren = new List<object>();
        for (var index = 0;
            index < pendingRevision.Nodes.Length;
            index++)
        {
            var node = pendingRevision.Nodes[index];
            if (node.Definition.ParentId == ownerLocalId &&
                string.Equals(
                    node.Definition.Slot,
                    slot,
                    StringComparison.Ordinal))
            {
                expectedChildren.Add(node.Instance);
            }
        }

        var expectedIndex = 0;
        for (var desiredIndex = 0;
            desiredIndex < desiredItems.Count;
            desiredIndex++)
        {
            var desiredItem = desiredItems[desiredIndex];
            var nodeLocalId = FindNodeLocalId(
                pendingRevision.Nodes,
                desiredItem);
            if (nodeLocalId < 0)
            {
                continue;
            }

            if (expectedIndex >= expectedChildren.Count ||
                !ReferenceEquals(
                    expectedChildren[expectedIndex],
                    desiredItem))
            {
                throw new ArgumentException(
                    $"Render collection node at position {desiredIndex} does " +
                    $"not match the current plan for slot '{slot}'.",
                    nameof(desiredItems));
            }

            expectedIndex++;
        }

        if (expectedIndex != expectedChildren.Count)
        {
            throw new ArgumentException(
                $"Render collection slot '{slot}' is missing one or more " +
                "generated child nodes.",
                nameof(desiredItems));
        }
    }

    private static void ValidateDesiredPropertyChild(
        PendingRevision pendingRevision,
        int ownerLocalId,
        string slot,
        object target,
        object desiredValue)
    {
        ValidatePropertyOwner(
            pendingRevision,
            ownerLocalId,
            target);

        object? expectedValue = null;
        var expectedCount = 0;
        for (var index = 0;
            index < pendingRevision.Nodes.Length;
            index++)
        {
            var node = pendingRevision.Nodes[index];
            if (node.Definition.ParentId == ownerLocalId &&
                string.Equals(
                    node.Definition.Slot,
                    slot,
                    StringComparison.Ordinal))
            {
                expectedValue = node.Instance;
                expectedCount++;
            }
        }

        if (expectedCount != 1 ||
            !ReferenceEquals(expectedValue, desiredValue))
        {
            throw new ArgumentException(
                $"Render property slot '{slot}' must contain exactly its one " +
                "current generated child.",
                nameof(desiredValue));
        }
    }

    private static void ValidatePropertyOwner(
        PendingRevision pendingRevision,
        int ownerLocalId,
        object target)
    {
        ValidateLocalId(ownerLocalId, pendingRevision.Nodes.Length);
        if (!ReferenceEquals(
                pendingRevision.Nodes[ownerLocalId].Instance,
                target))
        {
            throw new ArgumentException(
                "The render property target does not match its owner node.",
                nameof(target));
        }
    }

    private static int FindNodeLocalId(
        IReadOnlyList<RenderNodeState> nodes,
        object? instance)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            if (ReferenceEquals(nodes[index].Instance, instance))
            {
                return index;
            }
        }

        return -1;
    }

    private static PropertyInfo GetClrProperty(
        Type targetType,
        Type? declaringType,
        string propertyName)
    {
        if (declaringType != null &&
            !declaringType.IsAssignableFrom(targetType))
        {
            throw new ArgumentException(
                $"CLR render property receiver '{declaringType.FullName}' " +
                $"is not compatible with '{targetType.FullName}'.",
                nameof(declaringType));
        }

        const BindingFlags propertyFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        PropertyInfo? property = null;
        var currentType = declaringType ?? targetType;
        while (currentType != null)
        {
            property = currentType.GetProperty(
                propertyName,
                propertyFlags);
            if (property != null || declaringType != null)
            {
                break;
            }

            currentType = currentType.BaseType;
        }

        if (property == null ||
            !property.CanRead ||
            !property.CanWrite ||
            property.GetIndexParameters().Length != 0 ||
            property.GetMethod?.IsStatic != false ||
            property.SetMethod?.IsStatic != false)
        {
            throw new ArgumentException(
                $"CLR render property '" +
                $"{(declaringType ?? targetType).FullName}.{propertyName}' " +
                "must be a readable and writable instance property.",
                nameof(propertyName));
        }

        return property;
    }

    private static void SynchronizeComponentContentCollections(
        AkburaControl component)
    {
        var synchronizers = s_collectionSynchronizers.GetOrAdd(
            component.GetType(),
            static componentType => FindCollectionSynchronizers(componentType));

        for (var index = 0; index < synchronizers.Length; index++)
        {
            try
            {
                synchronizers[index].Invoke(component, parameters: null);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }
    }

    private static MethodInfo[] FindCollectionSynchronizers(Type componentType)
    {
        var synchronizers = new List<MethodInfo>();

        for (var currentType = componentType;
            currentType != null &&
            typeof(AkburaControl).IsAssignableFrom(currentType);
            currentType = currentType.BaseType)
        {
            var methods = currentType.GetMethods(
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            for (var index = 0; index < methods.Length; index++)
            {
                var method = methods[index];
                if (method.ReturnType == typeof(void) &&
                    !method.IsGenericMethod &&
                    method.GetParameters().Length == 0 &&
                    method.Name.StartsWith(
                        CollectionSynchronizerPrefix,
                        StringComparison.Ordinal))
                {
                    synchronizers.Add(method);
                }
            }
        }

        return [.. synchronizers];
    }

    private void RemoveOmittedProperties(
        PendingRevision pendingRevision)
    {
        foreach (var pair in _properties)
        {
            if (pendingRevision.Properties.ContainsKey(pair.Key) ||
                pendingRevision.ContainsOperation(pair.Key))
            {
                continue;
            }

            pendingRevision.TrackMutation(pair.Value.CreateMutation());
            pair.Value.RestoreBaseline();
        }
    }

    private void RemoveOmittedOperations(
        PendingRevision pendingRevision)
    {
        for (var nodeIndex = 0; nodeIndex < _nodes.Length; nodeIndex++)
        {
            var node = _nodes[nodeIndex];
            foreach (var pair in node.GetAppliedOperations())
            {
                var key = new RenderSlotKey(
                    node.NodeId,
                    pair.Key);
                if (pendingRevision.ContainsOperation(key) ||
                    pendingRevision.Properties.ContainsKey(key) ||
                    !pendingRevision.ReleasedOperations.Add(key))
                {
                    continue;
                }

                var resource = pair.Value.Resource;
                if (resource == null || !resource.IsApplied)
                {
                    continue;
                }

                pendingRevision.TrackMutation(
                    new RenderOperationMutation(
                        resource,
                        replacement: null));
                resource.Release();
            }
        }
    }

    private void RemoveOmittedCollections(
        PendingRevision pendingRevision)
    {
        var retainedNodeIds = new HashSet<long>();
        for (var index = 0;
            index < pendingRevision.Nodes.Length;
            index++)
        {
            retainedNodeIds.Add(pendingRevision.Nodes[index].NodeId);
        }

        foreach (var pair in _collections)
        {
            if (pendingRevision.Collections.ContainsKey(pair.Key))
            {
                continue;
            }

            if (pair.Value.ItemCount == 0)
            {
                if (retainedNodeIds.Contains(pair.Key.OwnerNodeId))
                {
                    pendingRevision.Collections.Add(pair.Key, pair.Value);
                }

                continue;
            }

            pendingRevision.TrackMutation(
                pair.Value.CreateMutation());
            var emptyState = pair.Value
                .RemoveOwnedItemsAndCreateEmptyState();
            if (retainedNodeIds.Contains(pair.Key.OwnerNodeId))
            {
                pendingRevision.Collections.Add(
                    pair.Key,
                    emptyState);
            }
        }
    }

    private RenderNodeState GetNode(int localId)
    {
        var nodes = _pendingRevision?.Nodes ?? _nodes;
        ValidateLocalId(localId, nodes.Length);
        return nodes[localId];
    }

    private PendingRevision GetPendingRevision()
    {
        return _pendingRevision ??
            throw new InvalidOperationException(
                "No render revision is being applied.");
    }

    private PendingRevision GetMutablePendingRevision()
    {
        var pendingRevision = GetPendingRevision();
        if (pendingRevision.IsPreparedForCompletion)
        {
            throw new InvalidOperationException(
                "Render revision completion has already been prepared.");
        }

        return pendingRevision;
    }

    private static void ValidateLocalId(
        int localId,
        int nodeCount)
    {
        if ((uint)localId >= (uint)nodeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localId),
                localId,
                $"Render node local identifier must be between 0 and " +
                $"{nodeCount - 1}.");
        }
    }

    private sealed class PendingRevision
    {
        public PendingRevision(
            string revision,
            AkburaRenderNodeDefinition[] definitions)
        {
            Revision = revision;
            Definitions = definitions;
            Nodes = new RenderNodeState[definitions.Length];
            IsNew = new bool[definitions.Length];
            ShouldApplyInitialValues = new bool[definitions.Length];
        }

        public string Revision { get; }

        public AkburaRenderNodeDefinition[] Definitions { get; }

        public RenderNodeState[] Nodes { get; }

        public bool[] IsNew { get; }

        public bool[] ShouldApplyInitialValues { get; }

        public bool IsPreparedForCompletion { get; set; }

        public Dictionary<RenderSlotKey, RenderCollectionState> Collections { get; } = [];

        public Dictionary<RenderSlotKey, RenderPropertyState> Properties { get; } = [];

        public HashSet<RenderSlotKey> SupersededProperties { get; } = [];

        public HashSet<RenderSlotKey> ReleasedOperations { get; } = [];

        public List<PendingInitializer> NewInitializers { get; } = [];

        private List<RenderRevisionMutation> Mutations { get; } = [];

        public void TrackMutation(RenderRevisionMutation mutation)
        {
            Mutations.Add(mutation);
        }

        public bool ContainsOperation(in RenderSlotKey key)
        {
            for (var index = 0; index < Nodes.Length; index++)
            {
                var node = Nodes[index];
                if (node != null &&
                    node.NodeId == key.OwnerNodeId)
                {
                    return node.ContainsOperation(key.Slot);
                }
            }

            return false;
        }

        public void ValidateOwnedOperations()
        {
            for (var index = 0; index < Nodes.Length; index++)
            {
                Nodes[index].ValidateOwnedOperations();
            }
        }

        public void Abort(bool throwOnFailure)
        {
            List<Exception>? exceptions = null;

            for (var index = Mutations.Count - 1;
                index >= 0;
                index--)
            {
                try
                {
                    Mutations[index].Rollback();
                }
                catch (Exception exception)
                {
                    if (throwOnFailure)
                    {
                        exceptions ??= [];
                        exceptions.Add(exception);
                    }
                }
            }

            try
            {
                EndNewNodes(throwOnFailure);
            }
            catch (Exception exception)
            {
                if (throwOnFailure)
                {
                    exceptions ??= [];
                    if (exception is AggregateException aggregate)
                    {
                        exceptions.AddRange(
                            aggregate.Flatten().InnerExceptions);
                    }
                    else
                    {
                        exceptions.Add(exception);
                    }
                }
            }

            if (exceptions != null)
            {
                if (exceptions.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
                }

                throw new AggregateException(
                    "One or more render revision resources could not be " +
                    "rolled back or finalized.",
                    exceptions);
            }
        }

        public void EndNewNodes(bool throwOnFailure)
        {
            List<Exception>? exceptions = null;

            for (var index = NewInitializers.Count - 1;
                index >= 0;
                index--)
            {
                var pendingInitializer = NewInitializers[index];
                if (pendingInitializer.IsCompleted)
                {
                    continue;
                }

                pendingInitializer.IsCompleted = true;
                try
                {
                    pendingInitializer.Initializer.EndInit();
                }
                catch (Exception exception)
                {
                    if (throwOnFailure)
                    {
                        exceptions ??= [];
                        exceptions.Add(exception);
                    }
                }
            }

            if (exceptions == null)
            {
                return;
            }

            if (exceptions.Count == 1)
            {
                ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
            }

            throw new AggregateException(
                "One or more render nodes could not complete initialization.",
                exceptions);
        }
    }

    private sealed class PendingInitializer
    {
        public PendingInitializer(ISupportInitialize initializer)
        {
            Initializer = initializer;
        }

        public ISupportInitialize Initializer { get; }

        public bool IsCompleted { get; set; }
    }

    private sealed class RenderNodeState
    {
        public RenderNodeState(
            long nodeId,
            AkburaRenderNodeDefinition definition,
            object instance,
            IReadOnlyDictionary<string, RenderOperationState>? previousOperations = null)
        {
            NodeId = nodeId;
            Definition = definition;
            Instance = instance;
            PreviousOperations = previousOperations ??
                EmptyOperations;
        }

        private static IReadOnlyDictionary<string, RenderOperationState>
            EmptyOperations { get; } =
                new Dictionary<string, RenderOperationState>(
                    StringComparer.Ordinal);

        public long NodeId { get; }

        public AkburaRenderNodeDefinition Definition { get; }

        public object Instance { get; }

        private IReadOnlyDictionary<string, RenderOperationState>
            PreviousOperations { get; }

        private Dictionary<string, RenderOperationState> Operations { get; } =
            new(StringComparer.Ordinal);

        public bool RegisterOperation(string slot, string identity)
        {
            EnsureOperationSlotAvailable(slot);

            if (PreviousOperations.TryGetValue(
                    slot,
                    out var previousOperation) &&
                previousOperation.HasIdentity(identity))
            {
                Operations.Add(slot, previousOperation);
                return false;
            }

            Operations.Add(
                slot,
                new RenderOperationState(
                    identity,
                    requiresResource: false,
                    previousResource: null));
            return true;
        }

        public bool RegisterOwnedOperation(
            string slot,
            string identity)
        {
            EnsureOperationSlotAvailable(slot);

            PreviousOperations.TryGetValue(
                slot,
                out var previousOperation);
            if (previousOperation != null &&
                previousOperation.RequiresResource &&
                previousOperation.HasIdentity(identity))
            {
                Operations.Add(slot, previousOperation);
                return false;
            }

            var operation = new RenderOperationState(
                identity,
                requiresResource: true,
                previousOperation?.Resource);
            Operations.Add(slot, operation);
            return true;
        }

        public void RegisterOwnedOperationForReplacement(
            string slot,
            string identity)
        {
            EnsureOperationSlotAvailable(slot);

            PreviousOperations.TryGetValue(
                slot,
                out var previousOperation);
            Operations.Add(
                slot,
                new RenderOperationState(
                    identity,
                    requiresResource: true,
                    previousOperation?.Resource));
        }

        public RenderOperationResource? SetOwnedOperationResource(
            string slot,
            RenderOperationResource resource)
        {
            if (!Operations.TryGetValue(slot, out var operation))
            {
                throw new InvalidOperationException(
                    $"Render operation slot '{slot}' was not registered for " +
                    $"node {Definition.LocalId}.");
            }

            return operation.SetResource(resource);
        }

        public bool ContainsOperation(string slot)
        {
            return Operations.ContainsKey(slot);
        }

        public bool TryGetAppliedOperation(
            string slot,
            out RenderOperationState operation)
        {
            return Operations.TryGetValue(slot, out operation!);
        }

        public RenderOperationState GetAppliedOperation(string slot)
        {
            if (!Operations.TryGetValue(slot, out var operation))
            {
                throw new InvalidOperationException(
                    $"Render operation slot '{slot}' was not registered for " +
                    $"node {Definition.LocalId}.");
            }

            return operation;
        }

        public IReadOnlyDictionary<string, RenderOperationState>
            GetAppliedOperations()
        {
            return Operations;
        }

        public void ValidateOwnedOperations()
        {
            foreach (var pair in Operations)
            {
                if (pair.Value.RequiresResource &&
                    pair.Value.Resource == null)
                {
                    throw new InvalidOperationException(
                        $"Owned render operation slot '{pair.Key}' on node " +
                        $"{Definition.LocalId} was registered without a " +
                        "runtime resource.");
                }
            }
        }

        private void EnsureOperationSlotAvailable(string slot)
        {
            if (Operations.ContainsKey(slot))
            {
                throw new InvalidOperationException(
                    $"Render operation slot '{slot}' was registered more than " +
                    $"once for node {Definition.LocalId}.");
            }
        }
    }

    private sealed class RenderOperationState
    {
        public RenderOperationState(
            string identity,
            bool requiresResource,
            RenderOperationResource? previousResource)
        {
            Identity = identity;
            RequiresResource = requiresResource;
            PreviousResource = previousResource;
        }

        public string Identity { get; }

        public bool RequiresResource { get; }

        public RenderOperationResource? Resource { get; private set; }

        private RenderOperationResource? PreviousResource { get; set; }

        public bool HasIdentity(string identity)
        {
            return string.Equals(
                Identity,
                identity,
                StringComparison.Ordinal);
        }

        public RenderOperationResource? SetResource(
            RenderOperationResource resource)
        {
            if (!RequiresResource)
            {
                throw new InvalidOperationException(
                    "The render operation slot does not own a runtime resource.");
            }

            if (Resource != null)
            {
                throw new InvalidOperationException(
                    "The render operation resource has already been applied.");
            }

            Resource = resource;
            var previous = PreviousResource;
            PreviousResource = null;
            return previous;
        }
    }

    private readonly struct RenderSlotKey : IEquatable<RenderSlotKey>
    {
        public RenderSlotKey(long ownerNodeId, string slot)
        {
            OwnerNodeId = ownerNodeId;
            Slot = slot;
        }

        public long OwnerNodeId { get; }

        public string Slot { get; }

        public bool Equals(RenderSlotKey other)
        {
            return OwnerNodeId == other.OwnerNodeId &&
                string.Equals(Slot, other.Slot, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is RenderSlotKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                OwnerNodeId,
                StringComparer.Ordinal.GetHashCode(Slot));
        }
    }

    private abstract class RenderCollectionState
    {
        protected RenderCollectionState(
            long ownerNodeId,
            string slot,
            int anchor)
        {
            OwnerNodeId = ownerNodeId;
            Slot = slot;
            Anchor = anchor;
        }

        public long OwnerNodeId { get; }

        public string Slot { get; }

        public int Anchor { get; }

        public abstract int ItemCount { get; }

        public abstract RenderRevisionMutation CreateMutation();

        public abstract RenderCollectionState
            RemoveOwnedItemsAndCreateEmptyState();
    }

    private sealed class RenderCollectionState<T> : RenderCollectionState
    {
        public RenderCollectionState(
            long ownerNodeId,
            string slot,
            IList<T> collection,
            AkburaControl? component,
            T[] items,
            int anchor)
            : base(ownerNodeId, slot, anchor)
        {
            Collection = collection;
            Component = component;
            Items = items;
        }

        public IList<T> Collection { get; }

        public AkburaControl? Component { get; }

        public T[] Items { get; }

        public override int ItemCount => Items.Length;

        public override RenderRevisionMutation CreateMutation()
        {
            return new RenderCollectionMutation<T>(
                Collection,
                Component);
        }

        public override RenderCollectionState
            RemoveOwnedItemsAndCreateEmptyState()
        {
            var anchor = AkburaRenderCollectionReconciler.Reconcile(
                Collection,
                Items,
                Array.Empty<T>(),
                Anchor);

            if (Component != null)
            {
                SynchronizeComponentContentCollections(Component);
            }

            return new RenderCollectionState<T>(
                OwnerNodeId,
                Slot,
                Collection,
                Component,
                Array.Empty<T>(),
                anchor);
        }
    }

    private sealed class UntypedRenderCollectionState : RenderCollectionState
    {
        public UntypedRenderCollectionState(
            long ownerNodeId,
            string slot,
            object target,
            System.Collections.IList collection,
            AkburaControl? component,
            object[] items,
            int anchor)
            : base(ownerNodeId, slot, anchor)
        {
            Target = target;
            Collection = collection;
            Component = component;
            Items = items;
        }

        public object Target { get; }

        public System.Collections.IList Collection { get; }

        public AkburaControl? Component { get; }

        public object[] Items { get; }

        public override int ItemCount => Items.Length;

        public override RenderRevisionMutation CreateMutation()
        {
            return new UntypedRenderCollectionMutation(
                Collection,
                Component);
        }

        public override RenderCollectionState
            RemoveOwnedItemsAndCreateEmptyState()
        {
            var anchor = AkburaRenderCollectionReconciler.Reconcile(
                Collection,
                Items,
                Array.Empty<object>(),
                Anchor);

            if (Component != null)
            {
                SynchronizeComponentContentCollections(Component);
            }

            return new UntypedRenderCollectionState(
                OwnerNodeId,
                Slot,
                Target,
                Collection,
                Component,
                Array.Empty<object>(),
                anchor);
        }
    }

    private abstract class RenderPropertyState
    {
        protected RenderPropertyState(
            long ownerNodeId,
            string slot,
            object target,
            object? desiredValue,
            string? declarationIdentity)
        {
            OwnerNodeId = ownerNodeId;
            Slot = slot;
            Target = target;
            DesiredValue = desiredValue;
            DeclarationIdentity = declarationIdentity;
        }

        public long OwnerNodeId { get; }

        public string Slot { get; }

        public object Target { get; }

        public object? DesiredValue { get; }

        public string? DeclarationIdentity { get; }

        public abstract bool Matches(RenderPropertyState other);

        public bool HasSameDeclaration(RenderPropertyState other)
        {
            if (DeclarationIdentity != null ||
                other.DeclarationIdentity != null)
            {
                return DeclarationIdentity != null &&
                    other.DeclarationIdentity != null &&
                    string.Equals(
                        DeclarationIdentity,
                        other.DeclarationIdentity,
                        StringComparison.Ordinal);
            }

            return ReferenceEquals(DesiredValue, other.DesiredValue);
        }

        public abstract void ApplyValue(object? value);

        public void ApplyDesiredValue()
        {
            ApplyValue(DesiredValue);
        }

        public abstract void RestoreBaseline();

        public abstract RenderRevisionMutation CreateMutation();

        public abstract RenderPropertyState WithDeclaration(
            RenderPropertyState declaration);
    }

    private sealed class ClrRenderPropertyState : RenderPropertyState
    {
        private readonly PropertyInfo _property;
        private readonly object? _baselineValue;

        public ClrRenderPropertyState(
            long ownerNodeId,
            string slot,
            object target,
            PropertyInfo property,
            object desiredValue)
            : this(
                ownerNodeId,
                slot,
                target,
                property,
                desiredValue,
                declarationIdentity: null,
                property.GetValue(target))
        {
        }

        public ClrRenderPropertyState(
            long ownerNodeId,
            string slot,
            object target,
            PropertyInfo property,
            object? desiredValue,
            string declarationIdentity)
            : this(
                ownerNodeId,
                slot,
                target,
                property,
                desiredValue,
                declarationIdentity,
                property.GetValue(target))
        {
        }

        private ClrRenderPropertyState(
            long ownerNodeId,
            string slot,
            object target,
            PropertyInfo property,
            object? desiredValue,
            string? declarationIdentity,
            object? baselineValue)
            : base(
                ownerNodeId,
                slot,
                target,
                desiredValue,
                declarationIdentity)
        {
            _property = property;
            _baselineValue = baselineValue;
        }

        public override bool Matches(RenderPropertyState other)
        {
            return other is ClrRenderPropertyState clrState &&
                ReferenceEquals(Target, clrState.Target) &&
                _property == clrState._property;
        }

        public override void ApplyValue(object? value)
        {
            _property.SetValue(Target, value);
        }

        public override void RestoreBaseline()
        {
            _property.SetValue(Target, _baselineValue);
        }

        public override RenderRevisionMutation CreateMutation()
        {
            return new ClrRenderPropertyMutation(
                Target,
                _property,
                _property.GetValue(Target));
        }

        public override RenderPropertyState WithDeclaration(
            RenderPropertyState declaration)
        {
            return new ClrRenderPropertyState(
                OwnerNodeId,
                Slot,
                Target,
                _property,
                declaration.DesiredValue,
                declaration.DeclarationIdentity,
                _baselineValue);
        }
    }

    private sealed class AvaloniaRenderPropertyState : RenderPropertyState
    {
        private readonly AvaloniaObject _target;
        private readonly AvaloniaProperty _property;
        private readonly bool _baselineWasSet;
        private readonly object? _baselineValue;

        public AvaloniaRenderPropertyState(
            long ownerNodeId,
            string slot,
            AvaloniaObject target,
            AvaloniaProperty property,
            object desiredValue)
            : this(
                ownerNodeId,
                slot,
                target,
                property,
                desiredValue,
                declarationIdentity: null,
                target.IsSet(property),
                target.GetValue(property))
        {
        }

        public AvaloniaRenderPropertyState(
            long ownerNodeId,
            string slot,
            AvaloniaObject target,
            AvaloniaProperty property,
            object? desiredValue,
            string declarationIdentity)
            : this(
                ownerNodeId,
                slot,
                target,
                property,
                desiredValue,
                declarationIdentity,
                target.IsSet(property),
                target.GetValue(property))
        {
        }

        private AvaloniaRenderPropertyState(
            long ownerNodeId,
            string slot,
            AvaloniaObject target,
            AvaloniaProperty property,
            object? desiredValue,
            string? declarationIdentity,
            bool baselineWasSet,
            object? baselineValue)
            : base(
                ownerNodeId,
                slot,
                target,
                desiredValue,
                declarationIdentity)
        {
            _target = target;
            _property = property;
            _baselineWasSet = baselineWasSet;
            _baselineValue = baselineValue;
        }

        public override bool Matches(RenderPropertyState other)
        {
            return other is AvaloniaRenderPropertyState avaloniaState &&
                ReferenceEquals(_target, avaloniaState._target) &&
                ReferenceEquals(_property, avaloniaState._property);
        }

        public override void ApplyValue(object? value)
        {
            _target.SetCurrentValue(_property, value);
        }

        public override void RestoreBaseline()
        {
            RestoreAvaloniaValue(
                _target,
                _property,
                _baselineWasSet,
                _baselineValue);
        }

        public override RenderRevisionMutation CreateMutation()
        {
            return new AvaloniaRenderPropertyMutation(
                _target,
                _property,
                _target.IsSet(_property),
                _target.GetValue(_property));
        }

        public override RenderPropertyState WithDeclaration(
            RenderPropertyState declaration)
        {
            return new AvaloniaRenderPropertyState(
                OwnerNodeId,
                Slot,
                _target,
                _property,
                declaration.DesiredValue,
                declaration.DeclarationIdentity,
                _baselineWasSet,
                _baselineValue);
        }
    }

    private abstract class RenderOperationResource
    {
        public bool IsApplied { get; private set; }

        public void Apply()
        {
            if (IsApplied)
            {
                return;
            }

            // Mark the resource before invoking user/runtime code so an
            // exception after a partial subscription can still be rolled back.
            IsApplied = true;
            ApplyCore();
        }

        public void Release()
        {
            if (!IsApplied)
            {
                return;
            }

            // Clear the state before invoking arbitrary release code so a
            // partial failure can be compensated by Apply during rollback.
            IsApplied = false;
            ReleaseCore();
        }

        protected abstract void ApplyCore();

        protected abstract void ReleaseCore();
    }

    private abstract class AvaloniaPropertyOperation : RenderOperationResource
    {
        private readonly bool _baselineWasSet;
        private readonly object? _baselineValue;

        protected AvaloniaPropertyOperation(
            AvaloniaObject target,
            AvaloniaProperty property,
            AvaloniaPropertyOperation? previous)
        {
            Target = target;
            Property = property;

            if (previous != null &&
                ReferenceEquals(previous.Target, target) &&
                ReferenceEquals(previous.Property, property))
            {
                _baselineWasSet = previous._baselineWasSet;
                _baselineValue = previous._baselineValue;
            }
            else
            {
                _baselineWasSet = target.IsSet(property);
                _baselineValue = target.GetValue(property);
            }
        }

        protected AvaloniaObject Target { get; }

        protected AvaloniaProperty Property { get; }

        protected void RestoreBaseline()
        {
            RestoreAvaloniaValue(
                Target,
                Property,
                _baselineWasSet,
                _baselineValue);
        }
    }

    private sealed class AvaloniaBindingOperation : AvaloniaPropertyOperation
    {
        private readonly BindingBase _binding;
        private IDisposable? _subscription;

        public AvaloniaBindingOperation(
            AvaloniaObject target,
            AvaloniaProperty property,
            BindingBase binding,
            AvaloniaPropertyOperation? previous)
            : base(target, property, previous)
        {
            _binding = binding;
        }

        protected override void ApplyCore()
        {
            _subscription = Target.Bind(
                Property,
                _binding);
        }

        protected override void ReleaseCore()
        {
            var subscription = _subscription;
            _subscription = null;
            subscription?.Dispose();
            RestoreBaseline();
        }
    }

    private sealed class AvaloniaObservableBindingOperation :
        AvaloniaPropertyOperation
    {
        private readonly IObservable<object?> _source;
        private IDisposable? _subscription;

        public AvaloniaObservableBindingOperation(
            AvaloniaObject target,
            AvaloniaProperty property,
            IObservable<object?> source,
            AvaloniaPropertyOperation? previous)
            : base(target, property, previous)
        {
            _source = source;
        }

        protected override void ApplyCore()
        {
            _subscription = Target.Bind(
                Property,
                _source);
        }

        protected override void ReleaseCore()
        {
            var subscription = _subscription;
            _subscription = null;
            subscription?.Dispose();
            RestoreBaseline();
        }
    }

    private sealed class ClrEventOperation : RenderOperationResource
    {
        private readonly object _target;
        private readonly EventInfo _event;
        private readonly Delegate _handler;

        public ClrEventOperation(
            object target,
            EventInfo eventInfo,
            Delegate handler)
        {
            _target = target;
            _event = eventInfo;
            _handler = handler;
        }

        protected override void ApplyCore()
        {
            _event.AddEventHandler(
                _target,
                _handler);
        }

        protected override void ReleaseCore()
        {
            _event.RemoveEventHandler(
                _target,
                _handler);
        }
    }

    private sealed class RoutedEventOperation : RenderOperationResource
    {
        private readonly Interactive _target;
        private readonly RoutedEvent _event;
        private readonly Delegate _handler;

        public RoutedEventOperation(
            Interactive target,
            RoutedEvent routedEvent,
            Delegate handler)
        {
            _target = target;
            _event = routedEvent;
            _handler = handler;
        }

        protected override void ApplyCore()
        {
            _target.AddHandler(
                _event,
                _handler);
        }

        protected override void ReleaseCore()
        {
            _target.RemoveHandler(
                _event,
                _handler);
        }
    }

    private sealed class AkcssStylesOperation : RenderOperationResource
    {
        private readonly object _target;
        private readonly ImmutableArray<AkcssStyleActivator> _styles;
        private readonly bool _baselineWasSet;
        private readonly ImmutableArray<AkcssStyleActivator> _baselineStyles;

        public AkcssStylesOperation(
            object target,
            ImmutableArray<AkcssStyleActivator> styles,
            AkcssStylesOperation? previous)
        {
            _target = target;
            _styles = styles.IsDefault ? [] : styles;

            if (previous != null &&
                ReferenceEquals(previous._target, target))
            {
                _baselineWasSet = previous._baselineWasSet;
                _baselineStyles = previous._baselineStyles;
                return;
            }

            if (target is Control control)
            {
                var baseline = control.GetBaseValue(
                    AkburaControl.AkcssStylesProperty);
                _baselineWasSet = baseline.HasValue;
                _baselineStyles = baseline.HasValue &&
                    !baseline.Value.IsDefault
                    ? baseline.Value
                    : [];
                return;
            }

            _baselineWasSet = true;
            _baselineStyles = AkcssRuntime.GetStyles(target);
        }

        protected override void ApplyCore()
        {
            AkburaControl.ReplaceAkcssStylesForHotReload(
                _target,
                _styles);
        }

        protected override void ReleaseCore()
        {
            if (_target is Control control && !_baselineWasSet)
            {
                control.ClearValue(AkburaControl.AkcssStylesProperty);
                return;
            }

            AkburaControl.ReplaceAkcssStylesForHotReload(
                _target,
                _baselineStyles);
        }
    }

    private abstract class RenderRevisionMutation
    {
        public abstract void Rollback();
    }

    private sealed class RenderOperationMutation : RenderRevisionMutation
    {
        private readonly RenderOperationResource? _previous;
        private readonly RenderOperationState? _replacement;

        public RenderOperationMutation(
            RenderOperationResource? previous,
            RenderOperationState? replacement)
        {
            _previous = previous;
            _replacement = replacement;
        }

        public override void Rollback()
        {
            Exception? releaseException = null;

            try
            {
                _replacement?.Resource?.Release();
            }
            catch (Exception exception)
            {
                releaseException = exception;
            }

            try
            {
                _previous?.Apply();
            }
            catch (Exception restoreException)
            {
                if (releaseException != null)
                {
                    throw new AggregateException(
                        "The replacement render operation could not be " +
                        "released and the previous operation could not be " +
                        "restored.",
                        releaseException,
                        restoreException);
                }

                throw;
            }

            if (releaseException != null)
            {
                ExceptionDispatchInfo.Capture(releaseException).Throw();
            }
        }
    }

    private sealed class RenderCollectionMutation<T> :
        RenderRevisionMutation
    {
        private readonly IList<T> _collection;
        private readonly AkburaControl? _component;
        private readonly T[] _snapshot;

        public RenderCollectionMutation(
            IList<T> collection,
            AkburaControl? component = null)
        {
            _collection = collection;
            _component = component;
            _snapshot = new T[collection.Count];

            for (var index = 0; index < collection.Count; index++)
            {
                _snapshot[index] = collection[index];
            }
        }

        public override void Rollback()
        {
            try
            {
                AkburaRenderCollectionReconciler.Restore(
                    _collection,
                    _snapshot);
            }
            finally
            {
                if (_component != null)
                {
                    SynchronizeComponentContentCollections(_component);
                }
            }
        }
    }

    private sealed class UntypedRenderCollectionMutation :
        RenderRevisionMutation
    {
        private readonly System.Collections.IList _collection;
        private readonly AkburaControl? _component;
        private readonly object[] _snapshot;

        public UntypedRenderCollectionMutation(
            System.Collections.IList collection,
            AkburaControl? component = null)
        {
            _collection = collection;
            _component = component;
            _snapshot = new object[collection.Count];

            for (var index = 0; index < collection.Count; index++)
            {
                _snapshot[index] = collection[index]!;
            }
        }

        public override void Rollback()
        {
            try
            {
                AkburaRenderCollectionReconciler.Restore(
                    _collection,
                    _snapshot);
            }
            finally
            {
                if (_component != null)
                {
                    SynchronizeComponentContentCollections(_component);
                }
            }
        }
    }

    private sealed class ClrRenderPropertyMutation : RenderRevisionMutation
    {
        private readonly object _target;
        private readonly PropertyInfo _property;
        private readonly object? _value;

        public ClrRenderPropertyMutation(
            object target,
            PropertyInfo property,
            object? value)
        {
            _target = target;
            _property = property;
            _value = value;
        }

        public override void Rollback()
        {
            _property.SetValue(_target, _value);
        }
    }

    private sealed class AvaloniaRenderPropertyMutation :
        RenderRevisionMutation
    {
        private readonly AvaloniaObject _target;
        private readonly AvaloniaProperty _property;
        private readonly bool _wasSet;
        private readonly object? _value;

        public AvaloniaRenderPropertyMutation(
            AvaloniaObject target,
            AvaloniaProperty property,
            bool wasSet,
            object? value)
        {
            _target = target;
            _property = property;
            _wasSet = wasSet;
            _value = value;
        }

        public override void Rollback()
        {
            RestoreAvaloniaValue(
                _target,
                _property,
                _wasSet,
                _value);
        }
    }

    private static void RestoreAvaloniaValue(
        AvaloniaObject target,
        AvaloniaProperty property,
        bool wasSet,
        object? value)
    {
        if (wasSet)
        {
            target.SetCurrentValue(property, value);
        }
        else
        {
            target.ClearValue(property);
        }
    }
}
