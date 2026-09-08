using Akbura.ComponentTree;
using Avalonia;
using System.Collections.Immutable;
using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>
/// Coordinates generated descriptor reconciliation and attached component refreshes.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class AkburaHotReloadRuntime
{
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
    public static void Refresh(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        if (!typeof(AkburaControl).IsAssignableFrom(componentType))
        {
            throw new ArgumentException(
                $"Type '{componentType.FullName}' is not an AkburaControl.",
                nameof(componentType));
        }

        var components = AkburaComponentRegistry.GetAttachedComponents();
        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            if (componentType.IsAssignableFrom(component.GetType()))
            {
                component.ApplyHotReload();
            }
        }
    }

    /// <summary>
    /// Prepares and refreshes attached instances assignable to a generated component type.
    /// </summary>
    /// <typeparam name="TComponent">The generated component type to refresh.</typeparam>
    /// <param name="prepare">
    /// A generated callback that resets instance caches before runtime reinjection.
    /// </param>
    public static void Refresh<TComponent>(Action<TComponent> prepare)
        where TComponent : AkburaControl
    {
        ArgumentNullException.ThrowIfNull(prepare);

        var components = AkburaComponentRegistry.GetAttachedComponents();
        for (var index = 0; index < components.Length; index++)
        {
            if (components[index] is not TComponent component)
            {
                continue;
            }

            prepare(component);
            component.ApplyHotReload();
        }
    }
}
