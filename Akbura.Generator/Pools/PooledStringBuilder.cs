using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Pools;
/// <summary>
/// The usage is:
///        var inst = PooledStringBuilder.GetInstance();
///        var sb = inst.builder;
///        ... Do Stuff...
///        ... sb.ToString() ...
///        inst.Free();
/// </summary>
internal sealed partial class PooledStringBuilder
{
    public readonly StringBuilder Builder = new();
    private readonly ObjectPool<PooledStringBuilder> _pool;

    private PooledStringBuilder(ObjectPool<PooledStringBuilder> pool)
    {
        Debug.Assert(pool != null);
        _pool = pool!;
    }

    public int Length => Builder.Length;

    public void Free()
    {
        var builder = Builder;

        // do not store builders that are too large.
        if (builder.Capacity <= 1024)
        {
            builder.Clear();
            _pool.Free(this);
        }
        else
        {
            _pool.ForgetTrackedObject(this);
        }
    }

    [Obsolete("Consider calling ToStringAndFree instead.")]
    public new string ToString()
    {
        return Builder.ToString();
    }

    public string ToStringAndFree()
    {
        var result = Builder.ToString();
        Free();

        return result;
    }

    public string ToStringAndFree(int startIndex, int length)
    {
        var result = Builder.ToString(startIndex, length);
        Free();

        return result;
    }

    // global pool
    private static readonly ObjectPool<PooledStringBuilder> s_poolInstance = CreatePool();

    // if someone needs to create a private pool;
    /// <summary>
    /// If someone need to create a private pool
    /// </summary>
    /// <param name="size">The size of the pool.</param>
    /// <returns></returns>
    public static ObjectPool<PooledStringBuilder> CreatePool(int size = 32)
    {
        ObjectPool<PooledStringBuilder>? pool = null;
        pool = new ObjectPool<PooledStringBuilder>(() => new PooledStringBuilder(pool!), size);
        return pool;
    }

    public static PooledStringBuilder GetInstance()
    {
        var builder = s_poolInstance.Allocate();
        Debug.Assert(builder.Builder.Length == 0);
        return builder;
    }

    public static implicit operator StringBuilder(PooledStringBuilder obj)
    {
        return obj.Builder;
    }
}