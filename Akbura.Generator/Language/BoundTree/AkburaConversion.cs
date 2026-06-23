using Microsoft.CodeAnalysis;
using CSharpConversion = Microsoft.CodeAnalysis.CSharp.Conversion;

namespace Akbura.Language.BoundTree;

internal readonly struct AkburaConversion
{
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
        Kind = kind;
        SourceType = sourceType;
        TargetType = targetType;
        CSharpConversion = csharpConversion;
    }

    public AkburaConversionKind Kind { get; }

    public ITypeSymbol? SourceType { get; }

    public ITypeSymbol? TargetType { get; }

    public CSharpConversion CSharpConversion { get; }

    public bool Exists => Kind != AkburaConversionKind.None;

    public bool IsIdentity => Kind == AkburaConversionKind.Identity;

    public bool IsImplicit =>
        Kind == AkburaConversionKind.Identity ||
        Kind == AkburaConversionKind.Implicit;

    public bool IsExplicit => Kind == AkburaConversionKind.Explicit;
}
