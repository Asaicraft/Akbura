using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal enum MarkupExtensionResultKind : byte
{
    None,
    Value,
    DynamicResource,
    StaticResource,
    BindingBase,
    Runtime,
}

/// <summary>
/// Semantic information used to classify markup-extension results once,
/// before code generation begins.
/// </summary>
internal readonly struct MarkupExtensionResultEnvironment
{
    private const string BindingBaseMetadataName =
        "Avalonia.Data.BindingBase";
    private const string DynamicResourceMetadataName =
        "Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension";
    private const string StaticResourceMetadataName =
        "Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension";
    private const string UnsetValueMetadataName =
        "Avalonia.UnsetValueType";

    private readonly CSharpCompilation _compilation;
    private readonly INamedTypeSymbol? _bindingBaseType;
    private readonly INamedTypeSymbol? _dynamicResourceType;
    private readonly INamedTypeSymbol? _staticResourceType;
    private readonly INamedTypeSymbol? _unsetValueType;

    private MarkupExtensionResultEnvironment(
        CSharpCompilation compilation,
        INamedTypeSymbol? bindingBaseType,
        INamedTypeSymbol? dynamicResourceType,
        INamedTypeSymbol? staticResourceType,
        INamedTypeSymbol? unsetValueType)
    {
        _compilation = compilation;
        _bindingBaseType = bindingBaseType;
        _dynamicResourceType = dynamicResourceType;
        _staticResourceType = staticResourceType;
        _unsetValueType = unsetValueType;
    }

    public static MarkupExtensionResultEnvironment Create(
        AkburaSemanticModel semanticModel)
    {
        if (semanticModel == null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        return Create(semanticModel.Compilation.CSharpCompilation);
    }

    internal static MarkupExtensionResultEnvironment Create(
        CSharpCompilation compilation)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        return new MarkupExtensionResultEnvironment(
            compilation,
            compilation.GetTypeByMetadataName(BindingBaseMetadataName),
            compilation.GetTypeByMetadataName(DynamicResourceMetadataName),
            compilation.GetTypeByMetadataName(StaticResourceMetadataName),
            compilation.GetTypeByMetadataName(UnsetValueMetadataName));
    }

    public bool IsValid => _compilation != null;

    public MarkupExtensionResultKind GetResultKind(
        MarkupExtensionValue extension)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "The markup-extension result environment is not initialized.");
        }

        if (extension == null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        var extensionType = extension.ExtensionType.Symbol as ITypeSymbol;

        if (IsSameType(extensionType, _dynamicResourceType))
        {
            return MarkupExtensionResultKind.DynamicResource;
        }

        if (IsSameType(extensionType, _staticResourceType))
        {
            return MarkupExtensionResultKind.StaticResource;
        }

        if (extension.ResultType.Symbol is not ITypeSymbol resultType)
        {
            return MarkupExtensionResultKind.Runtime;
        }

        // Roslyn treats dynamic -> BindingBase as an implicit conversion,
        // but the concrete runtime result still has to be inspected.
        if (resultType.SpecialType == SpecialType.System_Object ||
            resultType.TypeKind == TypeKind.Dynamic ||
            IsSameType(resultType, _unsetValueType))
        {
            return MarkupExtensionResultKind.Runtime;
        }

        if (_bindingBaseType != null &&
            _compilation.ClassifyConversion(resultType, _bindingBaseType).IsImplicit)
        {
            return MarkupExtensionResultKind.BindingBase;
        }

        return MarkupExtensionResultKind.Value;
    }

    private static bool IsSameType(
        ITypeSymbol? left,
        ITypeSymbol? right)
    {
        return left != null &&
            right != null &&
            SymbolEqualityComparer.Default.Equals(left, right);
    }
}

/// <summary>
/// Stores the markup extension and its precomputed result strategy.
/// </summary>
internal readonly struct MarkupExtensionResultPlan
{
    public MarkupExtensionResultPlan(
        MarkupExtensionValue extension,
        MarkupExtensionResultKind kind)
    {
        Extension = extension ??
            throw new ArgumentNullException(nameof(extension));
        Kind = kind;
    }

    public MarkupExtensionValue Extension { get; }

    public MarkupExtensionResultKind Kind { get; }

    public bool IsValid =>
        Extension != null &&
        Kind != MarkupExtensionResultKind.None;

    public static MarkupExtensionResultPlan Create(
        in MarkupExtensionResultEnvironment environment,
        MarkupExtensionValue extension)
    {
        return new MarkupExtensionResultPlan(
            extension,
            environment.GetResultKind(extension));
    }
}

/// <summary>
/// Identifies an Avalonia property and its target expression without
/// creating generated-code strings.
/// </summary>
internal readonly struct AvaloniaPropertyWriteTarget
{
    public AvaloniaPropertyWriteTarget(
        string targetExpression,
        ISymbol avaloniaProperty)
    {
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));
        Debug.Assert(
            avaloniaProperty is IFieldSymbol { IsStatic: true } or
                IPropertySymbol { IsStatic: true });

        TargetExpression = targetExpression;
        AvaloniaProperty = avaloniaProperty;
    }

    public string TargetExpression { get; }

    public ISymbol AvaloniaProperty { get; }

    public bool IsValid =>
        !string.IsNullOrEmpty(TargetExpression) &&
        AvaloniaProperty is IFieldSymbol { IsStatic: true } or
            IPropertySymbol { IsStatic: true };
}
