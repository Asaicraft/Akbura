using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Akbura;
internal static class AkburaRuntimeHelpers
{
    // When tested with 50 different types the collision rate is roughly 30%.
    // In real usage, however, IsReferenceOrContainsReferences<T> is only invoked for
    // a small set of types (usually 2–3, such as char or string), which makes 
    // collisions practically impossible.

#if !NET5_0_OR_GREATER

    private const int CacheBits = 6;
    private const int CacheSize = 1 << CacheBits;
    private const int CacheMask = CacheSize - 1;

    const int MaxDepth = 8;

    struct CacheEntry
    {
        public Type Type;
        public bool HasReferences;
    }

    private static readonly CacheEntry[] s_cache = new CacheEntry[CacheSize];
#endif

    public static bool IsReferenceOrContainsReferences<T>()
    {
#if NET5_0_OR_GREATER
        return RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#else
        var type = typeof(T);
        var entry = TryGetCache(type, out var index);

        if (index >= 0 && entry.Type == type)
        {
            return entry.HasReferences;
        }

        return GetInformationSlow(type, index);
#endif
    }

#if !NET5_0_OR_GREATER
    private static bool GetInformationSlow(Type type, int index)
    {
        if (!type.IsValueType)
        {
            return true; // reference type
        }

        // value-type => checks its fields
        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (IsReferenceOrContainsReferencesRecursive(f.FieldType, 1))
            {
                s_cache[index] = new CacheEntry
                {
                    Type = type,
                    HasReferences = true
                };
                return true;
            }
        }

        s_cache[index] = new CacheEntry
        {
            Type = type,
            HasReferences = false
        };
        return false;
    }

    private static bool IsReferenceOrContainsReferencesRecursive(Type type, int depth)
    {
        if (!type.IsValueType)
        {
            return true; // reference type
        }

        if (depth >= MaxDepth)
        {
            return true; // assume the worst
        }

        var entry = TryGetCache(type, out var index);

        if(index >= 0 && entry.Type == type)
        {
            return entry.HasReferences;
        }

        // value-type => checks its fields
        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (IsReferenceOrContainsReferencesRecursive(f.FieldType, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static CacheEntry TryGetCache(Type type, out int index)
    {
        var hashCode = type.GetHashCode();
        index = hashCode & CacheMask;
        
        var entry = s_cache[index];
        
        if (entry.Type == type)
        {
            return entry;
        }

        return default;
    }
#endif
}
