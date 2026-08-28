using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal readonly struct AkburaComponentTypeInfo
{
    public AkburaComponentTypeInfo(
        INamedTypeSymbol? declaredType,
        INamedTypeSymbol? baseType,
        INamedTypeSymbol? akburaControlType,
        bool hasExplicitBaseType,
        bool hasValidBaseType)
    {
        DeclaredType = declaredType;
        BaseType = baseType;
        AkburaControlType = akburaControlType;
        HasExplicitBaseType = hasExplicitBaseType;
        HasValidBaseType = hasValidBaseType;
    }

    public INamedTypeSymbol? DeclaredType { get; }

    public INamedTypeSymbol? BaseType { get; }

    public INamedTypeSymbol? AkburaControlType { get; }

    public bool HasExplicitBaseType { get; }

    public bool HasValidBaseType { get; }

    public bool ShouldDeclareAkburaControlBase =>
        AkburaControlType != null &&
        (DeclaredType == null ||
         DeclaredType.TypeKind == TypeKind.Class && !HasExplicitBaseType);
}

internal static class AkburaComponentTypeResolver
{
    private const string AkburaControlMetadataName = "Akbura.AkburaControl";

    public static AkburaComponentTypeInfo Resolve(
        CSharpCompilation compilation,
        string componentMetadataName)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        var akburaControlType = compilation.GetTypeByMetadataName(AkburaControlMetadataName);
        var declaredType = string.IsNullOrWhiteSpace(componentMetadataName)
            ? null
            : compilation.GetTypeByMetadataName(componentMetadataName);
        var explicitBaseType = declaredType == null
            ? null
            : GetExplicitBaseType(compilation, declaredType);
        var hasExplicitBaseType = explicitBaseType != null;
        var baseType = hasExplicitBaseType
            ? explicitBaseType
            : declaredType == null || declaredType.TypeKind == TypeKind.Class
                ? akburaControlType
                : declaredType.BaseType;
        var hasValidBaseType = akburaControlType == null ||
            (declaredType == null || declaredType.TypeKind == TypeKind.Class) &&
            declaredType?.IsStatic != true &&
            baseType != null &&
            IsDerivedFromOrEqual(baseType, akburaControlType);

        return new AkburaComponentTypeInfo(
            declaredType,
            baseType,
            akburaControlType,
            hasExplicitBaseType,
            hasValidBaseType);
    }

    private static INamedTypeSymbol? GetExplicitBaseType(
        CSharpCompilation compilation,
        INamedTypeSymbol declaredType)
    {
        foreach (var syntaxReference in declaredType.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not CSharp.TypeDeclarationSyntax declaration ||
                declaration.BaseList == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var baseTypeSyntax in declaration.BaseList.Types)
            {
                if (semanticModel.GetTypeInfo(baseTypeSyntax.Type).Type is not INamedTypeSymbol baseType)
                {
                    continue;
                }

                if (baseType.TypeKind is TypeKind.Class or TypeKind.Error)
                {
                    return baseType;
                }
            }
        }

        return declaredType.BaseType is { SpecialType: not SpecialType.System_Object } declaredBaseType
            ? declaredBaseType
            : null;
    }

    private static bool IsDerivedFromOrEqual(
        INamedTypeSymbol type,
        INamedTypeSymbol expectedBaseType)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedBaseType))
            {
                return true;
            }
        }

        return false;
    }
}
