using Akbura.Language.BoundTree;
using Microsoft.CodeAnalysis;
using System;

namespace Akbura.Language.Binder;

internal sealed class AkburaConversions
{
    private readonly Binder _binder;

    public AkburaConversions(Binder binder)
    {
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
    }

    public AkburaConversion ClassifyConversion(
        BoundExpression expression,
        ITypeSymbol? targetType)
    {
        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        return ClassifyConversion(expression.Type, targetType);
    }

    public AkburaConversion ClassifyConversion(
        ITypeSymbol? sourceType,
        ITypeSymbol? targetType)
    {
        if (sourceType == null || targetType == null)
        {
            return AkburaConversion.None(sourceType, targetType);
        }

        var conversion = _binder.Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType);
        var kind = conversion.IsIdentity
            ? AkburaConversionKind.Identity
            : conversion.IsImplicit
                ? AkburaConversionKind.Implicit
                : conversion.IsExplicit
                    ? AkburaConversionKind.Explicit
                    : AkburaConversionKind.None;

        return new AkburaConversion(
            kind,
            sourceType,
            targetType);
    }
}
