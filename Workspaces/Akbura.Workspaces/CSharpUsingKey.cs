using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Workspaces;

internal readonly struct CSharpUsingKey : IEquatable<CSharpUsingKey>
{
    public CSharpUsingKey(
        bool isGlobal,
        bool isStatic,
        bool isUnsafe,
        string? alias,
        string name)
    {
        IsGlobal = isGlobal;
        IsStatic = isStatic;
        IsUnsafe = isUnsafe;
        Alias = alias;
        Name = name ?? string.Empty;
    }

    public bool IsGlobal { get; }

    public bool IsStatic { get; }

    public bool IsUnsafe { get; }

    public string? Alias { get; }

    public string Name { get; }

    public static CSharpUsingKey Create(CSharp.UsingDirectiveSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        return new CSharpUsingKey(
            syntax.GlobalKeyword.RawKind != 0,
            syntax.StaticKeyword.RawKind != 0,
            syntax.UnsafeKeyword.RawKind != 0,
            syntax.Alias?.Name.Identifier.ValueText,
            syntax.Name?.ToString() ?? string.Empty);
    }

    public bool Equals(CSharpUsingKey other)
    {
        return IsGlobal == other.IsGlobal &&
            IsStatic == other.IsStatic &&
            IsUnsafe == other.IsUnsafe &&
            string.Equals(Alias, other.Alias, StringComparison.Ordinal) &&
            string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is CSharpUsingKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = IsGlobal ? 1 : 0;
            hashCode = (hashCode * 397) ^ (IsStatic ? 1 : 0);
            hashCode = (hashCode * 397) ^ (IsUnsafe ? 1 : 0);
            hashCode = (hashCode * 397) ^
                (Alias == null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(Alias));
            hashCode = (hashCode * 397) ^
                StringComparer.Ordinal.GetHashCode(Name);
            return hashCode;
        }
    }
}
