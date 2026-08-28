using Akbura.Language.Syntax;
using System;

namespace Akbura.Language.Binder;

// Key in the per-semantic-model binder cache.
// PERF: keep this tiny; semantic cross-compilation reuse has a separate key.
internal readonly struct BinderCacheKey : IEquatable<BinderCacheKey>
{
    public BinderCacheKey(AkburaSyntax syntax, BinderUsage usage)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Usage = usage;
    }

    public AkburaSyntax Syntax { get; }

    public BinderUsage Usage { get; }

    public bool Equals(BinderCacheKey other)
    {
        return SemanticSyntaxIdentity.Equals(Syntax, other.Syntax) &&
               Usage == other.Usage;
    }

    public override bool Equals(object? obj)
    {
        return obj is BinderCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(SemanticSyntaxIdentity.GetHashCode(Syntax), (int)Usage);
    }
}
