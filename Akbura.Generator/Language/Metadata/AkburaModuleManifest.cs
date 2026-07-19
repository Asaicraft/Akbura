using System;
using System.Collections.Immutable;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Language;

internal sealed class AkburaModuleManifest
{
    public const string ResourceName = "!Akbura";
    public const int CurrentFormatVersion = 2;

    public AkburaModuleManifest(
        int formatVersion,
        string assemblyName,
        ImmutableArray<AkburaModuleSource> sources)
    {
        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        }

        FormatVersion = formatVersion;
        AssemblyName = assemblyName ?? string.Empty;
        Sources = sources.IsDefault
            ? ImmutableArray<AkburaModuleSource>.Empty
            : sources;
    }

    public int FormatVersion { get; }

    public string AssemblyName { get; }

    public ImmutableArray<AkburaModuleSource> Sources { get; }
}

internal enum AkburaModuleSourceKind : byte
{
    Component,
    Akcss,
}

internal sealed class AkburaModuleSource
{
    public AkburaModuleSource(
        string sourceCodePath,
        AkburaModuleSourceKind kind,
        ImmutableArray<AkburaModuleDeclaration> declarations)
    {
        if (string.IsNullOrWhiteSpace(sourceCodePath))
        {
            throw new ArgumentException("Source code resource path cannot be empty.", nameof(sourceCodePath));
        }

        SourceCodePath = sourceCodePath;
        Kind = kind;
        Declarations = declarations.IsDefault
            ? ImmutableArray<AkburaModuleDeclaration>.Empty
            : declarations;
    }

    public string SourceCodePath { get; }

    public AkburaModuleSourceKind Kind { get; }

    public ImmutableArray<AkburaModuleDeclaration> Declarations { get; }
}

internal sealed class AkburaModuleDeclaration
{
    public AkburaModuleDeclaration(
        DeclarationKind kind,
        string name,
        string? metadataName,
        int sourceStart,
        int sourceLength,
        ImmutableArray<AkburaModuleDeclaration> children,
        AkburaModuleAkcssUtility? akcssUtility = null,
        AkburaModuleComponent? component = null)
    {
        if (sourceStart < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
        }

        if (sourceLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLength));
        }

        if ((kind == DeclarationKind.AkcssUtility) != (akcssUtility != null))
        {
            throw new ArgumentException(
                "AKCSS utility declarations must have exactly one utility signature.", nameof(akcssUtility));
        }

        if ((kind == DeclarationKind.Component) != (component != null))
        {
            throw new ArgumentException(
                "Component declarations must have exactly one component signature.", nameof(component));
        }

        Kind = kind;
        Name = name ?? string.Empty;
        MetadataName = string.IsNullOrWhiteSpace(metadataName)
            ? null
            : metadataName;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        Children = children.IsDefault
            ? ImmutableArray<AkburaModuleDeclaration>.Empty
            : children;
        AkcssUtility = akcssUtility;
        Component = component;
    }

    public DeclarationKind Kind { get; }

    public string Name { get; }

    public string? MetadataName { get; }

    public int SourceStart { get; }

    public int SourceLength { get; }

    public ImmutableArray<AkburaModuleDeclaration> Children { get; }

    public AkburaModuleAkcssUtility? AkcssUtility { get; }

    public AkburaModuleComponent? Component { get; }
}

internal sealed class AkburaModuleComponent
{
    public AkburaModuleComponent(
        string baseTypeName,
        ImmutableArray<AkburaModuleComponentParameter> parameters,
        ImmutableArray<AkburaModuleComponentInject> injectedServices)
    {
        if (string.IsNullOrWhiteSpace(baseTypeName))
        {
            throw new ArgumentException("Component base type cannot be empty.", nameof(baseTypeName));
        }

        BaseTypeName = baseTypeName;
        Parameters = parameters.IsDefault
            ? ImmutableArray<AkburaModuleComponentParameter>.Empty
            : parameters;
        InjectedServices = injectedServices.IsDefault
            ? ImmutableArray<AkburaModuleComponentInject>.Empty
            : injectedServices;
    }

    public string BaseTypeName { get; }

    public int ParameterCount => Parameters.Length;

    public ImmutableArray<AkburaModuleComponentParameter> Parameters { get; }

    public int InjectedServiceCount => InjectedServices.Length;

    public ImmutableArray<AkburaModuleComponentInject> InjectedServices { get; }
}

internal sealed class AkburaModuleComponentParameter
{
    public AkburaModuleComponentParameter(
        int ordinal,
        string name,
        string typeName,
        ParamBindingKind bindingKind,
        bool hasDefaultValue,
        int sourceStart,
        int sourceLength)
    {
        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Component parameter name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Component parameter type cannot be empty.", nameof(typeName));
        }

        if (sourceStart < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
        }

        if (sourceLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLength));
        }

        Ordinal = ordinal;
        Name = name;
        TypeName = typeName;
        BindingKind = bindingKind;
        HasDefaultValue = hasDefaultValue;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public string TypeName { get; }

    public ParamBindingKind BindingKind { get; }

    public bool HasDefaultValue { get; }

    public int SourceStart { get; }

    public int SourceLength { get; }
}

internal sealed class AkburaModuleComponentInject
{
    public AkburaModuleComponentInject(
        int ordinal,
        string name,
        string typeName,
        bool isOptional,
        int sourceStart,
        int sourceLength)
    {
        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Injected service name cannot be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Injected service type cannot be empty.", nameof(typeName));
        }

        if (sourceStart < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
        }

        if (sourceLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLength));
        }

        Ordinal = ordinal;
        Name = name;
        TypeName = typeName;
        IsOptional = isOptional;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public string TypeName { get; }

    public bool IsOptional { get; }

    public int SourceStart { get; }

    public int SourceLength { get; }
}

internal sealed class AkburaModuleAkcssUtility
{
    public AkburaModuleAkcssUtility(
        string? targetTypeName,
        ImmutableArray<AkburaModuleAkcssUtilityParameter> parameters)
    {
        TargetTypeName = string.IsNullOrWhiteSpace(targetTypeName)
            ? null
            : targetTypeName;
        Parameters = parameters.IsDefault
            ? ImmutableArray<AkburaModuleAkcssUtilityParameter>.Empty
            : parameters;
    }

    public string? TargetTypeName { get; }

    public int ParameterCount => Parameters.Length;

    public ImmutableArray<AkburaModuleAkcssUtilityParameter> Parameters { get; }
}

internal sealed class AkburaModuleAkcssUtilityParameter
{
    public AkburaModuleAkcssUtilityParameter(
        int ordinal,
        string name,
        string typeName,
        int sourceStart,
        int sourceLength)
    {
        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        if (sourceStart < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
        }

        if (sourceLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLength));
        }

        Ordinal = ordinal;
        Name = name ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public string TypeName { get; }

    public int SourceStart { get; }

    public int SourceLength { get; }
}

internal readonly struct AkburaModuleSourceText
{
    public AkburaModuleSourceText(string sourceCodePath, string text)
        : this(
            sourceCodePath,
            SourceText.From(text ?? throw new ArgumentNullException(nameof(text))))
    {
    }

    public AkburaModuleSourceText(string sourceCodePath, SourceText text)
    {
        SourceCodePath = sourceCodePath ?? throw new ArgumentNullException(nameof(sourceCodePath));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string SourceCodePath { get; }

    public SourceText Text { get; }
}
