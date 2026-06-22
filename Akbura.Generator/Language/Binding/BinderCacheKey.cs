using Akbura.Language.Declarations;
using Akbura.Language.Syntax.Green;

namespace Akbura.Language.Binding;

internal readonly struct BinderCacheKey
{
    public BinderCacheKey(
        GreenNode syntax,
        BinderUsage usage,
        AkburaBinderFlags flags,
        AkburaDeclarationKind declarationKind,
        GreenNode? scopeDesignator,
        string nextScopeKey)
    {
        Syntax = syntax;
        Usage = usage;
        Flags = flags;
        DeclarationKind = declarationKind;
        ScopeDesignator = scopeDesignator;
        NextScopeKey = nextScopeKey ?? string.Empty;
    }

    public GreenNode Syntax { get; }

    public BinderUsage Usage { get; }

    public AkburaBinderFlags Flags { get; }

    public AkburaDeclarationKind DeclarationKind { get; }

    public GreenNode? ScopeDesignator { get; }

    public string NextScopeKey { get; }

    public override bool Equals(object? obj)
    {
        return obj is BinderCacheKey other &&
               ReferenceEquals(Syntax, other.Syntax) &&
               Usage == other.Usage &&
               Flags == other.Flags &&
               DeclarationKind == other.DeclarationKind &&
               ReferenceEquals(ScopeDesignator, other.ScopeDesignator) &&
               NextScopeKey == other.NextScopeKey;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Syntax);
            hash = (hash * 397) ^ (int)Usage;
            hash = (hash * 397) ^ (int)Flags;
            hash = (hash * 397) ^ (int)DeclarationKind;
            hash = (hash * 397) ^ (ScopeDesignator == null
                ? 0
                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ScopeDesignator));
            hash = (hash * 397) ^ NextScopeKey.GetHashCode();
            return hash;
        }
    }
}
