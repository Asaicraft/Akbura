using Avalonia;
using System.Collections;
using System.Reflection;

namespace Akbura.HotReload;

internal static class AvaloniaPropertyRegistryHotReload
{
    private const BindingFlags FieldFlags =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo s_registeredField =
        GetField("_registered");
    private static readonly FieldInfo s_attachedField =
        GetField("_attached");
    private static readonly FieldInfo s_directField =
        GetField("_direct");
    private static readonly FieldInfo s_registeredCacheField =
        GetField("_registeredCache");
    private static readonly FieldInfo s_attachedCacheField =
        GetField("_attachedCache");
    private static readonly FieldInfo s_directCacheField =
        GetField("_directCache");
    private static readonly FieldInfo s_inheritedCacheField =
        GetField("_inheritedCache");
    private static readonly FieldInfo s_unregisteringLockerField =
        GetField("_unregisteringLocker");

    public static void Remove(
        Type ownerType,
        IReadOnlyCollection<AvaloniaProperty> properties)
    {
        if (properties.Count == 0)
        {
            return;
        }

        var registry = AvaloniaPropertyRegistry.Instance;
        var obsoleteProperties = new HashSet<AvaloniaProperty>(
            properties,
            ReferenceEqualityComparer.Instance);
        var locker = s_unregisteringLockerField.GetValue(registry) ??
            throw CreateLayoutException("_unregisteringLocker");

        lock (locker)
        {
            var registered = GetPropertyMap(registry, s_registeredField);
            var attached = GetPropertyMap(registry, s_attachedField);
            var direct = GetPropertyMap(registry, s_directField);

            RemoveFromOwner(registered, ownerType, obsoleteProperties);
            RemoveFromOwner(attached, ownerType, obsoleteProperties);
            RemoveFromOwner(direct, ownerType, obsoleteProperties);

            foreach (var property in obsoleteProperties)
            {
                property.Unregister(ownerType);
            }

            ClearCache(registry, s_registeredCacheField);
            ClearCache(registry, s_attachedCacheField);
            ClearCache(registry, s_directCacheField);
            ClearCache(registry, s_inheritedCacheField);
        }
    }

    private static void RemoveFromOwner(
        Dictionary<Type, Dictionary<int, AvaloniaProperty>> registrations,
        Type ownerType,
        HashSet<AvaloniaProperty> obsoleteProperties)
    {
        if (!registrations.TryGetValue(ownerType, out var ownerProperties))
        {
            return;
        }

        var propertyIds = new List<int>();

        foreach (var pair in ownerProperties)
        {
            if (obsoleteProperties.Contains(pair.Value))
            {
                propertyIds.Add(pair.Key);
            }
        }

        foreach (var propertyId in propertyIds)
        {
            ownerProperties.Remove(propertyId);
        }

        if (ownerProperties.Count == 0)
        {
            registrations.Remove(ownerType);
        }
    }

    private static Dictionary<Type, Dictionary<int, AvaloniaProperty>>
        GetPropertyMap(
            AvaloniaPropertyRegistry registry,
            FieldInfo field)
    {
        return field.GetValue(registry) as
            Dictionary<Type, Dictionary<int, AvaloniaProperty>> ??
            throw CreateLayoutException(field.Name);
    }

    private static void ClearCache(
        AvaloniaPropertyRegistry registry,
        FieldInfo field)
    {
        var cache = field.GetValue(registry) as IDictionary ??
            throw CreateLayoutException(field.Name);

        cache.Clear();
    }

    private static FieldInfo GetField(string name)
    {
        return typeof(AvaloniaPropertyRegistry).GetField(name, FieldFlags) ??
            throw CreateLayoutException(name);
    }

    private static NotSupportedException CreateLayoutException(
        string fieldName)
    {
        return new NotSupportedException(
            "The AvaloniaPropertyRegistry layout is incompatible with " +
            $"Akbura Hot Reload: field '{fieldName}' was not found or changed type.");
    }
}
