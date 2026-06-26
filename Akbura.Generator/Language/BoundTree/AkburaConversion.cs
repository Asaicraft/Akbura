using Microsoft.CodeAnalysis;
using System;
using CSharpConversion = Microsoft.CodeAnalysis.CSharp.Conversion;

namespace Akbura.Language.BoundTree;

internal readonly struct AkburaConversion : IEquatable<AkburaConversion>
{
    private readonly AkburaConversionFlags _flags;
    private readonly UncommonData? _uncommonData;

    public static AkburaConversion None(ITypeSymbol? sourceType, ITypeSymbol? targetType)
    {
        return new AkburaConversion(
            AkburaConversionKind.None,
            sourceType,
            targetType);
    }

    public AkburaConversion(
        AkburaConversionKind kind,
        ITypeSymbol? sourceType,
        ITypeSymbol? targetType,
        CSharpConversion csharpConversion = default)
    {
        _flags = CreateFlags(kind, csharpConversion);
        _uncommonData = csharpConversion.Exists || csharpConversion.MethodSymbol != null
            ? new UncommonData(csharpConversion)
            : null;
        SourceType = sourceType;
        TargetType = targetType;
    }

    public AkburaConversionKind Kind => (AkburaConversionKind)(_flags & AkburaConversionFlags.KindMask);

    public ITypeSymbol? SourceType { get; }

    public ITypeSymbol? TargetType { get; }

    public CSharpConversion CSharpConversion => _uncommonData?.CSharpConversion ?? default;

    public IMethodSymbol? MethodSymbol => _uncommonData?.MethodSymbol;

    public bool Exists => HasFlag(AkburaConversionFlags.Exists);

    public bool IsIdentity => HasFlag(AkburaConversionFlags.Identity);

    public bool IsImplicit => HasFlag(AkburaConversionFlags.Implicit);

    public bool IsExplicit => HasFlag(AkburaConversionFlags.Explicit);

    public bool IsNumeric => HasFlag(AkburaConversionFlags.Numeric);

    public bool IsReference => HasFlag(AkburaConversionFlags.Reference);

    public bool IsBoxing => HasFlag(AkburaConversionFlags.Boxing);

    public bool IsUnboxing => HasFlag(AkburaConversionFlags.Unboxing);

    public bool IsNullable => HasFlag(AkburaConversionFlags.Nullable);

    public bool IsEnumeration => HasFlag(AkburaConversionFlags.Enumeration);

    public bool IsUserDefined => HasFlag(AkburaConversionFlags.UserDefined);

    public bool Equals(AkburaConversion other)
    {
        return _flags == other._flags &&
               SymbolEqualityComparer.Default.Equals(SourceType, other.SourceType) &&
               SymbolEqualityComparer.Default.Equals(TargetType, other.TargetType) &&
               SymbolEqualityComparer.Default.Equals(MethodSymbol, other.MethodSymbol) &&
               CSharpConversion.Equals(other.CSharpConversion);
    }

    public override bool Equals(object? obj)
    {
        return obj is AkburaConversion other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)_flags;
            hash = (hash * 397) ^ (SourceType == null ? 0 : SymbolEqualityComparer.Default.GetHashCode(SourceType));
            hash = (hash * 397) ^ (TargetType == null ? 0 : SymbolEqualityComparer.Default.GetHashCode(TargetType));
            hash = (hash * 397) ^ (MethodSymbol == null ? 0 : SymbolEqualityComparer.Default.GetHashCode(MethodSymbol));
            hash = (hash * 397) ^ CSharpConversion.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(AkburaConversion left, AkburaConversion right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(AkburaConversion left, AkburaConversion right)
    {
        return !left.Equals(right);
    }

    private bool HasFlag(AkburaConversionFlags flag)
    {
        return (_flags & flag) == flag;
    }

    private static AkburaConversionFlags CreateFlags(
        AkburaConversionKind kind,
        CSharpConversion csharpConversion)
    {
        var flags = (AkburaConversionFlags)kind;
        flags |= kind switch
        {
            AkburaConversionKind.Identity => AkburaConversionFlags.Exists |
                                             AkburaConversionFlags.Identity |
                                             AkburaConversionFlags.Implicit,
            AkburaConversionKind.Implicit => AkburaConversionFlags.Exists |
                                             AkburaConversionFlags.Implicit,
            AkburaConversionKind.Explicit => AkburaConversionFlags.Exists |
                                             AkburaConversionFlags.Explicit,
            _ => AkburaConversionFlags.None,
        };

        if (csharpConversion.IsNumeric)
        {
            flags |= AkburaConversionFlags.Numeric;
        }

        if (csharpConversion.IsReference)
        {
            flags |= AkburaConversionFlags.Reference;
        }

        if (csharpConversion.IsBoxing)
        {
            flags |= AkburaConversionFlags.Boxing;
        }

        if (csharpConversion.IsUnboxing)
        {
            flags |= AkburaConversionFlags.Unboxing;
        }

        if (csharpConversion.IsNullable)
        {
            flags |= AkburaConversionFlags.Nullable;
        }

        if (csharpConversion.IsEnumeration)
        {
            flags |= AkburaConversionFlags.Enumeration;
        }

        if (csharpConversion.IsUserDefined)
        {
            flags |= AkburaConversionFlags.UserDefined;
        }

        return flags;
    }

    [Flags]
    private enum AkburaConversionFlags : uint
    {
        None = 0,

        KindMask = 0x000000FF,

        Exists = 1u << 8,
        Identity = 1u << 9,
        Implicit = 1u << 10,
        Explicit = 1u << 11,

        Numeric = 1u << 12,
        Reference = 1u << 13,
        Boxing = 1u << 14,
        Unboxing = 1u << 15,
        Nullable = 1u << 16,
        Enumeration = 1u << 17,
        UserDefined = 1u << 18,
    }

    private sealed class UncommonData
    {
        public UncommonData(CSharpConversion csharpConversion)
        {
            CSharpConversion = csharpConversion;
            MethodSymbol = csharpConversion.MethodSymbol;
        }

        public CSharpConversion CSharpConversion { get; }

        public IMethodSymbol? MethodSymbol { get; }
    }
}
