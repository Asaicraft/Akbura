using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Akbura;
internal static class AkburaRuntimeHelpers
{
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

    public static bool IsReferenceOrContainsReferences<T>()
    {
#if NET5_0_OR_GREATER
        return RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#else
        var type = typeof(T);
        var hashCode = type.GetHashCode();

        var index = hashCode & CacheMask;
        var entry = s_cache[index];

        if (entry.Type == type)
        {
            return entry.HasReferences;
        }

        return GetInformationSlow(type, index);
#endif
    }

    private static bool GetInformationSlow(Type type, int index)
    {
        var hasReferences = IsReferenceOrContainsReferencesRecursive(type, 0);
        s_cache[index] = new CacheEntry
        {
            Type = type,
            HasReferences = hasReferences
        };
        return hasReferences;
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
}
