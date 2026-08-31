using Akbura.Language;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Diagnostics;
using System.Threading;

namespace Akbura.BlackSilence;

internal static class IncrementalParseCache
{
    internal sealed class Entry(
        int hash,
        SyntaxTreeKind kind,
        string filePath,
        string logicalName,
        SourceText sourceText,
        AkburaSyntaxTree syntaxTree)
    {
        public readonly int Hash = hash;

        public readonly SyntaxTreeKind Kind = kind;

        public readonly string FilePath = filePath;

        public readonly string LogicalName = logicalName;

        public readonly SourceText SourceText = sourceText;

        public readonly AkburaSyntaxTree SyntaxTree = syntaxTree;
    }

    // Retain at most 32 regular syntax trees.
    // The fixed size limits the lifetime of stale files
    // while keeping collision rates reasonable.
    internal const int RegularCacheSizeBits = 5;
    internal const int RegularCacheSize = 1 << RegularCacheSizeBits;
    internal const int RegularCacheMask = RegularCacheSize - 1;

    // Large syntax trees are more expensive to retain,
    // so reserve only four cache slots for them.
    internal const int LargeCacheSizeBits = 2;
    internal const int LargeCacheSize = 1 << LargeCacheSizeBits;
    internal const int LargeCacheMask = LargeCacheSize - 1;

    // SourceText.Length is measured in UTF-16 code units,
    // not in bytes.
    internal const int LargeFileThreshold = 128 * 1024;

    internal static readonly Entry?[] RegularCache = new Entry?[RegularCacheSize];

    internal static readonly Entry?[] LargeCache = new Entry?[LargeCacheSize];

    public static Entry? TryGet(
        SyntaxTreeKind kind,
        string filePath,
        string logicalName,
        SourceText sourceText,
        out int hash)
    {
        hash = GetCacheHash(
            kind,
            filePath,
            logicalName);

        if (IsLargeFile(sourceText))
        {
            var entry = TryGetFromCache(
                LargeCache,
                LargeCacheMask,
                hash,
                kind,
                filePath,
                logicalName);

            if (entry != null)
            {
                return entry;
            }

            // The file may have crossed the large-file threshold
            // since its previous version was cached.
            return TryGetFromCache(
                RegularCache,
                RegularCacheMask,
                hash,
                kind,
                filePath,
                logicalName);
        }
        else
        {
            var entry = TryGetFromCache(
                RegularCache,
                RegularCacheMask,
                hash,
                kind,
                filePath,
                logicalName);

            if (entry != null)
            {
                return entry;
            }

            // The previous version may have been large.
            return TryGetFromCache(
                LargeCache,
                LargeCacheMask,
                hash,
                kind,
                filePath,
                logicalName);
        }
    }

    public static void Add(
        SyntaxTreeKind kind,
        string filePath,
        string logicalName,
        SourceText sourceText,
        AkburaSyntaxTree syntaxTree,
        int hash)
    {
        Debug.Assert(
            hash == GetCacheHash(
                kind,
                filePath,
                logicalName));

        Debug.Assert(
            ReferenceEquals(
                sourceText,
                syntaxTree.Text));

        var entry = new Entry(
            hash,
            kind,
            filePath,
            logicalName,
            sourceText,
            syntaxTree);

        if (IsLargeFile(sourceText))
        {
            AddToCache(
                LargeCache,
                LargeCacheMask,
                entry);

            RemoveFromCache(
                RegularCache,
                RegularCacheMask,
                hash,
                kind,
                filePath,
                logicalName);
        }
        else
        {
            AddToCache(
                RegularCache,
                RegularCacheMask,
                entry);

            RemoveFromCache(
                LargeCache,
                LargeCacheMask,
                hash,
                kind,
                filePath,
                logicalName);
        }
    }

    private static Entry? TryGetFromCache(
        Entry?[] cache,
        int cacheMask,
        int hash,
        SyntaxTreeKind kind,
        string filePath,
        string logicalName)
    {
        var index = hash & cacheMask;

        var entry = Volatile.Read(ref cache[index]);

        if (entry == null ||
            !IsMatchingEntry(
                entry,
                hash,
                kind,
                filePath,
                logicalName))
        {
            return null;
        }

        return entry;
    }

    private static void AddToCache(
        Entry?[] cache,
        int cacheMask,
        Entry entry)
    {
        var index = entry.Hash & cacheMask;

        Volatile.Write(
            ref cache[index],
            entry);
    }

    private static void RemoveFromCache(
        Entry?[] cache,
        int cacheMask,
        int hash,
        SyntaxTreeKind kind,
        string filePath,
        string logicalName)
    {
        var index = hash & cacheMask;

        var entry = Volatile.Read(
            ref cache[index]);

        if (entry == null ||
            !IsMatchingEntry(
                entry,
                hash,
                kind,
                filePath,
                logicalName))
        {
            return;
        }

        // Clear the slot only if it still contains
        // the entry that we have just inspected.
        Interlocked.CompareExchange(
            ref cache[index],
            null,
            entry);
    }

    private static bool IsMatchingEntry(
        Entry entry,
        int hash,
        SyntaxTreeKind kind,
        string filePath,
        string logicalName)
    {
        return entry.Hash == hash &&
               entry.Kind == kind &&
               StringComparer.Ordinal.Equals(
                   entry.FilePath,
                   filePath) &&
               StringComparer.Ordinal.Equals(
                   entry.LogicalName,
                   logicalName);
    }

    private static bool IsLargeFile(
        SourceText sourceText)
    {
        return sourceText.Length >=
               LargeFileThreshold;
    }

    private static int GetCacheHash(
        SyntaxTreeKind kind,
        string filePath,
        string logicalName)
    {
        // Use a deterministic FNV-1a hash instead of
        // HashCode.Combine so cache slot selection remains stable.
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        unchecked
        {
            var hash = offsetBasis;

            hash ^= (uint)kind;
            hash *= prime;

            // Include the length to distinguish boundaries
            // between the individual key components.
            hash ^= (uint)filePath.Length;
            hash *= prime;

            // Keep hashing consistent with the ordinal
            // file-path comparison used by the cache.
            for (var i = 0; i < filePath.Length; i++)
            {
                hash ^= filePath[i];
                hash *= prime;
            }

            hash ^= (uint)logicalName.Length;
            hash *= prime;

            for (var i = 0; i < logicalName.Length; i++)
            {
                hash ^= logicalName[i];
                hash *= prime;
            }

            return (int)(hash & 0x7FFF_FFFFu);
        }
    }
}
