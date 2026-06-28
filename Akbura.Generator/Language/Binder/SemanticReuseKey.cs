using Akbura.Language.Declarations;
using Akbura.Language.Syntax.Green;
using System;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Binder;

// Key for reusing semantic facts across incremental compilations.
// Unlike BinderCacheKey, this must include the stable binder scope shape.
internal readonly struct SemanticReuseKey : IEquatable<SemanticReuseKey>
{
    public SemanticReuseKey(
        GreenNode syntax,
        BinderUsage usage,
        AkburaBinderFlags flags,
        AkburaDeclarationKind declarationKind,
        string scopeKey)
    {
        Syntax = syntax;
        Usage = usage;
        Flags = flags;
        DeclarationKind = declarationKind;
        ScopeKey = scopeKey ?? string.Empty;
    }

    public GreenNode Syntax { get; }

    public BinderUsage Usage { get; }

    public AkburaBinderFlags Flags { get; }

    public AkburaDeclarationKind DeclarationKind { get; }

    public string ScopeKey { get; }

    public bool Equals(SemanticReuseKey other)
    {
        return ReferenceEquals(Syntax, other.Syntax) &&
               Usage == other.Usage &&
               Flags == other.Flags &&
               DeclarationKind == other.DeclarationKind &&
               ScopeKey == other.ScopeKey;
    }

    public override bool Equals(object? obj)
    {
        return obj is SemanticReuseKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = RuntimeHelpers.GetHashCode(Syntax);
            hash = (hash * 397) ^ (int)Usage;
            hash = (hash * 397) ^ (int)Flags;
            hash = (hash * 397) ^ (int)DeclarationKind;
            hash = (hash * 397) ^ ScopeKey.GetHashCode();
            return hash;
        }
    }
}
