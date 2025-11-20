// This file is ported and adapted from the Roslyn (dotnet/roslyn)
using Akbura;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if STATS
using System.Threading;
#endif

namespace Akbura.Language.Syntax.Green;

/// <summary>
/// Provides caching functionality for green nonterminals with up to 3 children.
/// Example:
///     When constructing a _node with given kind, flags, child1 and child2, we can look up 
///     in the cache whether we already have a _node that contains same kind, flags, 
///     child1 and child2 and use that.
///     
///     For the purpose of children comparison, reference equality is used as a much cheaper 
///     alternative to the structural/recursive equality. This implies that in order to de-duplicate
///     a _node to a cache _node, the children of two nodes must be already de-duplicated.     
///     When adding a _node to the cache we verify that cache does contain _node's children,
///     since otherwise there is no reason for the _node to be used.
///     Tokens/nulls are for this purpose considered deduplicated. Indeed most of the tokens
///     are deduplicated via quick-scanner caching, so we just assume they all are.
///     
///     As a result of above, "fat" nodes with 4 or more children or their recursive parents
///     will never be in the cache. This naturally limits the typical single cache item to be 
///     a relatively simple expression. We do not want the cache to be completely unbounded 
///     on the item size. 
///     While it still may be possible to store a gigantic nested binary expression, 
///     it should be a rare occurrence.
///     
///     We only consider "normal" nodes to be cacheable. 
///     Nodes with diagnostics/annotations/directives/skipped, etc... have more complicated identity 
///     and are not likely to be repetitive.
///     
/// </summary>
public class GreenStats
{
    // TODO: remove when done tweaking this cache.
#if STATS
        private static GreenStats stats = new();

        private int greenNodes;
        private int greenTokens;
        private int nontermsAdded;
        private int cacheableNodes;
        private int cacheHits;

        public static void NoteGreen(GreenNode node)
        {
            Interlocked.Increment(ref stats.greenNodes);
            if (node.IsToken)
            {
                Interlocked.Increment(ref stats.greenTokens);
            }
        }

        public static void ItemAdded()
        {
            Interlocked.Increment(ref stats.nontermsAdded);
        }
        
        public static void ItemCacheable()
        {
            Interlocked.Increment(ref stats.cacheableNodes);
        }

        public static void CacheHit()
        {
            Interlocked.Increment(ref stats.cacheHits);
        }

        ~GreenStats()
        {
            Console.WriteLine("Green: " + greenNodes);
            Console.WriteLine("GreenTk: " + greenTokens);
            Console.WriteLine("Nonterminals added: " + nontermsAdded);
            Console.WriteLine("Nonterminals cacheable: " + cacheableNodes);
            Console.WriteLine("CacheHits: " + cacheHits);
            Console.WriteLine("RateOfAll: " + (cacheHits * 100 / (cacheHits + greenNodes - greenTokens)) + "%");
            Console.WriteLine("RateOfCacheable: " + (cacheHits * 100 / (cacheableNodes)) + "%");
        }
#else
    internal static void NoteGreen(GreenNode _)
    {
    }

    [Conditional("DEBUG")]
    public static void ItemAdded()
    {
    }

    [Conditional("DEBUG")]
    public static void ItemCacheable()
    {
    }

    [Conditional("DEBUG")]
    public static void CacheHit()
    {
    }
#endif
}


internal static class GreenNodeCache
{
    internal readonly struct Entry(int hash, GreenNode node)
    {
        public readonly int Hash = hash;
        public readonly GreenNode? Node = node;
    }

    internal const int CacheSizeBits = 9;
    internal const int CacheSize = 1 << CacheSizeBits;
    internal const int CacheMask = CacheSize - 1;

    internal static readonly Entry[] Cache = new Entry[CacheSize];

    public static void AddNode(GreenNode node, int hash)
    {
        if (AllChildrenInCache(node) && !node.IsNotMissing)
        {
            GreenStats.ItemAdded();

            Debug.Assert(node.GetCacheHash() == hash);

            var idx = hash & CacheMask;
            Cache[idx] = new Entry(hash, node);
        }
    }

    internal static bool CanBeCached(GreenNode? child1)
    {
        return child1 == null || child1.IsCacheable;
    }


    internal static bool CanBeCached(GreenNode? child1, GreenNode? child2)
    {
        return CanBeCached(child1) && CanBeCached(child2);
    }

    internal static bool CanBeCached(GreenNode? child1, GreenNode? child2, GreenNode? child3)
    {
        return CanBeCached(child1) && CanBeCached(child2) && CanBeCached(child3);
    }

    public static bool ChildInCache(GreenNode? child)
    {
        // for the purpose of this function consider that 
        // null nodes, tokens and trivias are cached somewhere else.
        // TODO: should use slotCount
        if (child == null || child.SlotCount == 0)
        {
            return true;
        }

        var hash = child.GetCacheHash();
        var idx = hash & CacheMask;

        return Cache[idx].Node == child;
    }

    public static bool AllChildrenInCache(GreenNode node)
    {
        // TODO: should use slotCount
        var count = node.SlotCount;

        for (var i = 0; i < count; i++)
        {
            if (!ChildInCache(node.GetSlot(i)))
            {
                return false;
            }
        }

        return true;
    }

    public static GreenNode? TryGetNode(ushort kind, GreenNode? child1, out int hash)
    {
        var slotCount = CalculateSlotCount(child1);

        return TryGetNode(kind, child1, GetDefaultNodeFlags(slotCount), out hash);
    }

    public static GreenNode? TryGetNode(ushort kind, GreenNode? child1, ushort flags, out int hash)
    {
        if (CanBeCached(child1))
        {
            GreenStats.ItemCacheable();

            var h = hash = GetCacheHash(kind, flags, child1);
            var idx = h & CacheMask;
            var e = Cache[idx];
            if (e.Hash == h && e.Node != null && e.Node.IsCacheEquivalent(kind, flags, child1))
            {
                GreenStats.CacheHit();
                return e.Node;
            }
        }
        else
        {
            hash = -1;
        }

        return null;
    }

    public static GreenNode? TryGetNode(ushort kind, GreenNode? child1, GreenNode? child2, out int hash)
    {
        var slotCount = CalculateSlotCount(child1);
        slotCount += CalculateSlotCount(child2);

        return TryGetNode(kind, child1, child2, GetDefaultNodeFlags(slotCount), out hash);
    }

    public static GreenNode? TryGetNode(ushort kind, GreenNode? child1, GreenNode? child2, ushort flags, out int hash)
    {
        if (CanBeCached(child1, child2))
        {
            GreenStats.ItemCacheable();

            var h = hash = GetCacheHash(kind, flags, child1, child2);
            var idx = h & CacheMask;
            var e = Cache[idx];

            if (e.Hash == h && e.Node != null && e.Node.IsCacheEquivalent(kind, flags, child1, child2))
            {
                GreenStats.CacheHit();
                return e.Node;
            }
        }
        else
        {
            hash = -1;
        }

        return null;
    }

    public static GreenNode? TryGetNode(ushort kind, GreenNode? child1, GreenNode? child2, GreenNode? child3, out int hash)
    {
        var slotCount = CalculateSlotCount(child1);
        slotCount += CalculateSlotCount(child2);
        slotCount += CalculateSlotCount(child3);

        return TryGetNode(kind, child1, child2, child3, GetDefaultNodeFlags(slotCount), out hash);
    }

    public static GreenNode? TryGetNode(ushort kind, GreenNode? child1, GreenNode? child2, GreenNode? child3, ushort flags, out int hash)
    {
        if (CanBeCached(child1, child2, child3))
        {
            GreenStats.ItemCacheable();

            var h = hash = GetCacheHash(kind, flags, child1, child2, child3);
            var idx = h & CacheMask;
            var e = Cache[idx];

            if (e.Hash == h && e.Node != null && e.Node.IsCacheEquivalent(kind, flags, child1, child2, child3))
            {
                GreenStats.CacheHit();
                return e.Node;
            }
        }
        else
        {
            hash = -1;
        }

        return null;
    }

    public static ushort GetDefaultNodeFlags(byte slotCount)
    {
        ushort nodeFlags;

        nodeFlags = (ushort)(0b_0000_0000_0000_1111 & slotCount);
        nodeFlags = (ushort)(nodeFlags | 0b_0000_0000_0001_0000); // not missing

        return nodeFlags;
    }

    public static int GetCacheHash(ushort kind, ushort flags, GreenNode? child1)
    {
        var code = flags << 16 | kind;
        code = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child1), code);

        return code & int.MaxValue;
    }

    public static int GetCacheHash(ushort kind, ushort flags, GreenNode? child1, GreenNode? child2)
    {
        var code = flags << 16 | kind;
        code = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child1), code);
        code = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child2), code);

        return code & int.MaxValue;
    }

    public static int GetCacheHash(ushort kind, ushort flags, GreenNode? child1, GreenNode? child2, GreenNode? child3)
    {
        var code = flags << 16 | kind;
        code = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child1), code);
        code = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child2), code);
        code = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child3), code);

        return code & int.MaxValue;
    }

    private static byte CalculateSlotCount(GreenNode? node)
    {
        if (node == null)
        {
            return 0;
        }

        return 1;
    }
}
