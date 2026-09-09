using Akbura.ComponentTree;
using Avalonia;
using Avalonia.Threading;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Akbura.HotReload;

/// <summary>
/// Coordinates generated descriptor reconciliation and attached component refreshes.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class AkburaHotReloadRuntime
{
    private static readonly object s_refreshGate = new();
    // Structural plans reconcile directly from the last applied definition to
    // the latest one, so only the newest callback per component type is kept.
    private static readonly Dictionary<Type, IPendingComponentRefresh>
        s_pendingRefreshes = [];
    private static readonly ConditionalWeakTable<
        AkburaControl,
        AppliedComponentRefreshes> s_appliedRefreshes = new();
    private static long s_refreshRevision;
    private static long s_publishedRefreshRevision;

    /// <summary>
    /// Removes generated Avalonia properties that are absent from the current
    /// component descriptor shape.
    /// </summary>
    /// <param name="ownerType">The generated component type.</param>
    /// <param name="previousRegistrations">
    /// The descriptor manifest applied before the metadata update.
    /// </param>
    /// <param name="currentKeys">
    /// Stable descriptor identities produced by the updated component.
    /// </param>
    /// <returns>A stack-friendly update scope for generated code.</returns>
    public static AkburaHotReloadPropertyUpdate BeginPropertyUpdate(
        Type ownerType,
        ImmutableArray<AkburaHotReloadPropertyRegistration>
            previousRegistrations,
        IReadOnlyCollection<string> currentKeys)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(currentKeys);

        var retainedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in currentKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A Hot Reload descriptor key cannot be null or whitespace.",
                    nameof(currentKeys));
            }

            if (!retainedKeys.Add(key))
            {
                throw new ArgumentException(
                    $"Hot Reload descriptor key '{key}' occurs more than once.",
                    nameof(currentKeys));
            }
        }

        if (previousRegistrations.IsDefaultOrEmpty)
        {
            return default;
        }

        var obsoleteProperties = new HashSet<AvaloniaProperty>(
            ReferenceEqualityComparer.Instance);
        var previousKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < previousRegistrations.Length; index++)
        {
            ref readonly var registration =
                ref previousRegistrations.ItemRef(index);

            if (!previousKeys.Add(registration.Key))
            {
                throw new InvalidOperationException(
                    $"Hot Reload descriptor key '{registration.Key}' " +
                    "is registered more than once.");
            }

            if (!retainedKeys.Contains(registration.Key))
            {
                obsoleteProperties.Add(registration.Property);
            }
        }

        AvaloniaPropertyRegistryHotReload.Remove(
            ownerType,
            obsoleteProperties);

        return default;
    }

    /// <summary>
    /// Finds a compatible Avalonia property in a previous generated descriptor manifest.
    /// </summary>
    /// <typeparam name="TProperty">The expected Avalonia property type.</typeparam>
    /// <param name="registrations">The previous generated descriptor manifest.</param>
    /// <param name="key">The stable descriptor identity.</param>
    /// <returns>The compatible property, or <see langword="null"/> when it is absent.</returns>
    public static TProperty? FindProperty<TProperty>(
        ImmutableArray<AkburaHotReloadPropertyRegistration> registrations,
        string key)
        where TProperty : AvaloniaProperty
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        TProperty? result = null;

        for (var index = 0; index < registrations.Length; index++)
        {
            ref readonly var registration = ref registrations.ItemRef(index);
            if (!string.Equals(
                    registration.Key,
                    key,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (result != null)
            {
                throw new InvalidOperationException(
                    $"Hot Reload descriptor key '{key}' is registered more than once.");
            }

            result = registration.Property as TProperty ??
                throw new InvalidOperationException(
                    $"Hot Reload descriptor '{key}' contains " +
                    $"'{registration.Property?.GetType().FullName ?? "<null>"}', " +
                    $"not '{typeof(TProperty).FullName}'.");
        }

        return result;
    }

    /// <summary>
    /// Refreshes attached instances assignable to a generated component type.
    /// </summary>
    /// <param name="componentType">The generated component type to refresh.</param>
    /// <remarks>
    /// The revision is retained for component instances that already exist but
    /// are detached. They catch up when they next attach to a non-excluded
    /// visual tree. Instances created after this call start at this revision.
    /// This method must be called on the Avalonia UI thread.
    /// </remarks>
    public static void Refresh(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        if (!typeof(AkburaControl).IsAssignableFrom(componentType))
        {
            throw new ArgumentException(
                $"Type '{componentType.FullName}' is not an AkburaControl.",
                nameof(componentType));
        }

        Dispatcher.UIThread.VerifyAccess();

        var pendingRefresh = RegisterRefresh(componentType);
        RefreshAttachedComponents(pendingRefresh);
    }

    /// <summary>
    /// Prepares and refreshes attached instances assignable to a generated component type.
    /// </summary>
    /// <typeparam name="TComponent">The generated component type to refresh.</typeparam>
    /// <param name="prepare">
    /// A generated callback that resets instance caches before runtime reinjection.
    /// </param>
    /// <remarks>
    /// The revision is retained for component instances that already exist but
    /// are detached. Multiple missed revisions of the same component type are
    /// coalesced to the latest generated plan before the component next
    /// attaches to a non-excluded visual tree.
    /// This method must be called on the Avalonia UI thread.
    /// </remarks>
    public static void Refresh<TComponent>(Action<TComponent> prepare)
        where TComponent : AkburaControl
    {
        ArgumentNullException.ThrowIfNull(prepare);
        Dispatcher.UIThread.VerifyAccess();

        var pendingRefresh = RegisterRefresh(prepare);
        RefreshAttachedComponents(pendingRefresh);
    }

    internal static bool ApplyPendingRefreshes(AkburaControl component)
    {
        ArgumentNullException.ThrowIfNull(component);
        Dispatcher.UIThread.VerifyAccess();

        var pendingRefreshes = GetPendingRefreshes(component);
        if (pendingRefreshes.Count == 0)
        {
            return false;
        }

        var appliedRefreshes = s_appliedRefreshes.GetValue(
            component,
            static _ => new AppliedComponentRefreshes());
        lock (appliedRefreshes.Gate)
        {
            if (appliedRefreshes.IsApplying)
            {
                appliedRefreshes.NeedsDrain = true;
                return false;
            }

            appliedRefreshes.IsApplying = true;
        }

        var completed = false;
        try
        {
            var applied = false;

            while (true)
            {
                lock (appliedRefreshes.Gate)
                {
                    appliedRefreshes.NeedsDrain = false;
                }

                applied |= ApplyRefreshBatch(
                    component,
                    pendingRefreshes,
                    appliedRefreshes);

                lock (appliedRefreshes.Gate)
                {
                    if (!appliedRefreshes.NeedsDrain)
                    {
                        appliedRefreshes.IsApplying = false;
                        completed = true;
                        return applied;
                    }
                }

                pendingRefreshes = GetPendingRefreshes(component);
            }
        }
        finally
        {
            if (!completed)
            {
                lock (appliedRefreshes.Gate)
                {
                    appliedRefreshes.IsApplying = false;
                    appliedRefreshes.NeedsDrain = false;
                }
            }
        }
    }

    internal static void AcknowledgeRefreshes(
        AkburaControl component,
        long requestGeneration)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (!s_appliedRefreshes.TryGetValue(
                component,
                out var appliedRefreshes))
        {
            return;
        }

        List<Type>? acknowledgedTypes = null;

        lock (appliedRefreshes.Gate)
        {
            foreach (var pair in appliedRefreshes.ScheduledRevisions)
            {
                var scheduledRefresh = pair.Value;
                if (scheduledRefresh.RequestGeneration >
                    requestGeneration)
                {
                    continue;
                }

                if (!appliedRefreshes.Revisions.TryGetValue(
                        pair.Key,
                        out var appliedRevision) ||
                    appliedRevision < scheduledRefresh.Revision)
                {
                    appliedRefreshes.Revisions[pair.Key] =
                        scheduledRefresh.Revision;
                }

                acknowledgedTypes ??= [];
                acknowledgedTypes.Add(pair.Key);
            }

            if (acknowledgedTypes == null)
            {
                return;
            }

            for (var index = 0; index < acknowledgedTypes.Count; index++)
            {
                appliedRefreshes.ScheduledRevisions.Remove(
                    acknowledgedTypes[index]);
            }
        }
    }

    private static List<IPendingComponentRefresh> GetPendingRefreshes(
        AkburaControl component)
    {
        var pendingRefreshes = new List<IPendingComponentRefresh>();
        var componentType = component.GetType();
        var creationRevision = component.HotReloadCreationRevision;

        lock (s_refreshGate)
        {
            foreach (var refresh in s_pendingRefreshes.Values)
            {
                if (refresh.Revision > creationRevision &&
                    refresh.ComponentType.IsAssignableFrom(componentType))
                {
                    pendingRefreshes.Add(refresh);
                }
            }
        }

        pendingRefreshes.Sort(static (left, right) =>
            left.Revision.CompareTo(right.Revision));

        return pendingRefreshes;
    }

    internal static long CaptureRevision()
    {
        return Volatile.Read(ref s_publishedRefreshRevision);
    }

    private static IPendingComponentRefresh RegisterRefresh(Type componentType)
    {
        lock (s_refreshGate)
        {
            var refresh = new PendingComponentRefresh(
                componentType,
                ++s_refreshRevision);
            AddPendingRefresh(refresh);
            Volatile.Write(
                ref s_publishedRefreshRevision,
                refresh.Revision);
            return refresh;
        }
    }

    private static IPendingComponentRefresh RegisterRefresh<TComponent>(
        Action<TComponent> prepare)
        where TComponent : AkburaControl
    {
        lock (s_refreshGate)
        {
            var refresh = new PendingComponentRefresh<TComponent>(
                prepare,
                ++s_refreshRevision);
            AddPendingRefresh(refresh);
            Volatile.Write(
                ref s_publishedRefreshRevision,
                refresh.Revision);
            return refresh;
        }
    }

    private static void AddPendingRefresh(
        IPendingComponentRefresh pendingRefresh)
    {
        s_pendingRefreshes[pendingRefresh.ComponentType] =
            pendingRefresh;
    }

    private static void RefreshAttachedComponents(
        IPendingComponentRefresh pendingRefresh)
    {
        var components = AkburaComponentRegistry.GetAttachedComponents();
        List<Exception>? exceptions = null;

        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            if (pendingRefresh.ComponentType.IsAssignableFrom(
                    component.GetType()) &&
                AkburaComponentRegistry.IsParticipating(component))
            {
                try
                {
                    ApplyPendingRefreshes(component);
                }
                catch (Exception exception)
                {
                    exceptions ??= [];
                    exceptions.Add(exception);
                }
            }
        }

        ThrowRefreshFailures(exceptions);
    }

    private static void ThrowRefreshFailures(List<Exception>? exceptions)
    {
        if (exceptions == null)
        {
            return;
        }

        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        throw new AggregateException(
            "One or more components could not be refreshed.",
            exceptions);
    }

    private static bool ApplyRefreshBatch(
        AkburaControl component,
        IReadOnlyList<IPendingComponentRefresh> pendingRefreshes,
        AppliedComponentRefreshes appliedRefreshes)
    {
        var scheduledRevisions = new Dictionary<Type, long>();
        var hasNewRefresh = false;
        var hasScheduledRefresh = false;

        for (var index = 0; index < pendingRefreshes.Count; index++)
        {
            var pendingRefresh = pendingRefreshes[index];
            lock (appliedRefreshes.Gate)
            {
                if (appliedRefreshes.Revisions.TryGetValue(
                        pendingRefresh.ComponentType,
                        out var appliedRevision) &&
                    appliedRevision >= pendingRefresh.Revision)
                {
                    continue;
                }

                if (appliedRefreshes.ScheduledRevisions.TryGetValue(
                        pendingRefresh.ComponentType,
                        out var scheduledRefresh) &&
                    scheduledRefresh.Revision >= pendingRefresh.Revision)
                {
                    hasScheduledRefresh = true;
                    scheduledRevisions[pendingRefresh.ComponentType] =
                        scheduledRefresh.Revision;
                    continue;
                }
            }

            pendingRefresh.Prepare(component);
            hasNewRefresh = true;
            scheduledRevisions[pendingRefresh.ComponentType] =
                pendingRefresh.Revision;
        }

        if (!hasNewRefresh)
        {
            if (!hasScheduledRefresh)
            {
                return false;
            }

            component.RetryPendingHotReload();
            return true;
        }

        component.ApplyHotReload(
            requestGeneration =>
            {
                lock (appliedRefreshes.Gate)
                {
                    foreach (var pair in scheduledRevisions)
                    {
                        appliedRefreshes.ScheduledRevisions[pair.Key] =
                            new ScheduledComponentRefresh(
                                pair.Value,
                                requestGeneration);
                    }
                }
            });
        return true;
    }

    private interface IPendingComponentRefresh
    {
        Type ComponentType { get; }

        long Revision { get; }

        void Prepare(AkburaControl component);
    }

    private sealed class PendingComponentRefresh
        : IPendingComponentRefresh
    {
        public PendingComponentRefresh(Type componentType, long revision)
        {
            ComponentType = componentType;
            Revision = revision;
        }

        public Type ComponentType { get; }

        public long Revision { get; }

        public void Prepare(AkburaControl component)
        {
        }
    }

    private sealed class PendingComponentRefresh<TComponent>
        : IPendingComponentRefresh
        where TComponent : AkburaControl
    {
        private readonly Action<TComponent> _prepare;

        public PendingComponentRefresh(
            Action<TComponent> prepare,
            long revision)
        {
            _prepare = prepare;
            Revision = revision;
        }

        public Type ComponentType => typeof(TComponent);

        public long Revision { get; }

        public void Prepare(AkburaControl component)
        {
            _prepare((TComponent)component);
        }
    }

    private readonly struct ScheduledComponentRefresh
    {
        public ScheduledComponentRefresh(
            long revision,
            long requestGeneration)
        {
            Revision = revision;
            RequestGeneration = requestGeneration;
        }

        public long Revision { get; }

        public long RequestGeneration { get; }
    }

    private sealed class AppliedComponentRefreshes
    {
        public object Gate { get; } = new();

        public Dictionary<Type, long> Revisions { get; } = [];

        public Dictionary<Type, ScheduledComponentRefresh>
            ScheduledRevisions { get; } = [];

        public bool IsApplying { get; set; }

        public bool NeedsDrain { get; set; }
    }
}
