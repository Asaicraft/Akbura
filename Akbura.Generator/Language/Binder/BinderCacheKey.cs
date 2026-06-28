using Akbura.Language.Syntax.Green;
using System;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Binder;

// Key in the per-semantic-model binder cache.
// PERF: keep this tiny; semantic cross-compilation reuse has a separate key.
internal readonly struct BinderCacheKey : IEquatable<BinderCacheKey>
{
    public BinderCacheKey(GreenNode syntax, BinderUsage usage)
    {
        Syntax = syntax;
        Usage = usage;
    }

    public GreenNode Syntax { get; }

    public BinderUsage Usage { get; }

    public bool Equals(BinderCacheKey other)
    {
        return ReferenceEquals(Syntax, other.Syntax) &&
               Usage == other.Usage;
    }

    public override bool Equals(object? obj)
    {
        return obj is BinderCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (RuntimeHelpers.GetHashCode(Syntax) * 397) ^ (int)Usage;
        }
    }
}
